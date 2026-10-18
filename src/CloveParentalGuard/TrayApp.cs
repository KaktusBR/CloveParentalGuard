using System.Reflection;
using CloveParentalGuard.Agent;
using CloveParentalGuard.Core;

namespace CloveParentalGuard.App;

/// <summary>The user-session tray app: status, unlock menu, and the auto-popped block prompt.</summary>
static class TrayApp
{
    private const string PipeName = "CloveParentalGuardPipe";
    private static readonly TimeSpan PopCooldown = TimeSpan.FromSeconds(30);
    private static readonly int[] WarnThresholds = { 600, 300, 60 }; // seconds remaining, descending

    public static void Run()
    {
        RegisterToastApp();
        var client = new PipeClient(PipeName);
        var fallbackDurations = new List<int> { 30, 60, 120, 180 };

        UnlockForm? openWindow = null;
        long lastBlockSeq = -1;                       // -1 = backlog not yet baselined
        var lastPop = DateTime.MinValue;
        bool? wasLocked = null;                        // null = first poll not yet seen
        var warned = new HashSet<int>();               // expiry thresholds already toasted this unlock

        // Opens the (single) unlock window. blockedGame != null => auto-pop "blocked" mode.
        void OpenUnlock(string? blockedGame)
        {
            if (openWindow is { IsDisposed: false })
            {
                openWindow.Activate();
                return;
            }

            var durations = fallbackDurations;
            try
            {
                var s = client.Send(new PipeRequest("status"));
                if (s.Durations is { Count: > 0 }) durations = new List<int>(s.Durations);
            }
            catch { /* service offline — use fallback */ }

            var form = new UnlockForm(client, durations, blockedGame);
            if (blockedGame is not null) form.TopMost = true; // surface over the game
            form.FormClosed += (_, _) => openWindow = null;
            openWindow = form;
            form.Show();
            form.Activate();
            if (blockedGame is not null)
            {
                form.TopMost = true;
                form.BringToFront();
            }
        }

        var tray = new NotifyIcon
        {
            Icon = TrayIcon.BuildController(),
            Visible = true,
            Text = "CloveParentalGuard",
        };

        // Native-rendered menu so it matches the OS theme.
        var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.System };
        var header = new ToolStripMenuItem("CloveParentalGuard") { Enabled = false };
        var unlockItem = new ToolStripMenuItem("Unlock…", null, (_, _) => OpenUnlock(null));
        var exitItem = new ToolStripMenuItem("Exit", null,
            (_, _) => { tray.Visible = false; Application.Exit(); });
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(unlockItem);
        menu.Items.Add(exitItem);
        // While the menu is open, tick the header every second against a local deadline
        // so the countdown is live without hammering the service.
        DateTime? menuDeadline = null;
        var menuTick = new System.Windows.Forms.Timer { Interval = 1000 };
        menuTick.Tick += (_, _) =>
        {
            if (menuDeadline is not DateTime end) return;
            var left = (int)Math.Ceiling((end - DateTime.UtcNow).TotalSeconds);
            header.Text = left > 0
                ? $"CloveParentalGuard — unlocked, {Clock(left)} left"
                : "CloveParentalGuard — games are locked";
        };
        menu.Opening += (_, _) =>
        {
            try
            {
                var s = client.Send(new PipeRequest("status"));
                menuDeadline = !s.IsLocked && s.RemainingSeconds > 0
                    ? DateTime.UtcNow.AddSeconds(s.RemainingSeconds)
                    : null;
                header.Text = menuDeadline is null
                    ? Describe(s)
                    : $"CloveParentalGuard — unlocked, {Clock(s.RemainingSeconds)} left";
            }
            catch
            {
                menuDeadline = null;
                header.Text = "Service offline";
            }
            menuTick.Start();
        };
        menu.Closed += (_, _) => menuTick.Stop();
        tray.ContextMenuStrip = menu;

        // Left-click also pops the menu. Reuse NotifyIcon's own private ShowContextMenu
        // so focus/dismiss behaviour matches a right-click.
        var showMenu = typeof(NotifyIcon).GetMethod(
            "ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) showMenu?.Invoke(tray, null);
        };

        var timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += (_, _) =>
        {
            try
            {
                var s = client.Send(new PipeRequest("status"));
                tray.Text = Truncate(Describe(s));

                // Unlock / expiry toasts (skip the first poll — that's just the baseline).
                if (wasLocked is bool prev)
                {
                    if (prev && !s.IsLocked)
                    {
                        warned.Clear();
                        Notify(tray, "Games unlocked",
                            $"Game time: {Duration(s.RemainingSeconds)} — until {Until(s.RemainingSeconds)}.",
                            ToolTipIcon.Info);
                    }
                    else if (!prev && s.IsLocked)
                    {
                        Notify(tray, "Time's up", "Game time is over — games are locked again.", ToolTipIcon.Info);
                    }
                }
                else if (!s.IsLocked)
                {
                    // First poll already unlocked: suppress warnings for thresholds already passed.
                    foreach (var t in WarnThresholds)
                        if (s.RemainingSeconds <= t) warned.Add(t);
                }

                // Heads-up toasts as remaining time crosses each threshold (10, 5, then 1 min).
                if (!s.IsLocked && s.RemainingSeconds > 0)
                {
                    // Time went back up (unlock extended): re-arm thresholds that are ahead again.
                    warned.RemoveWhere(t => s.RemainingSeconds > t);

                    foreach (var t in WarnThresholds)
                        if (s.RemainingSeconds <= t && warned.Add(t))
                        {
                            Notify(tray, "Heads up",
                                $"{(t == 60 ? "1 minute" : $"{t / 60} minutes")} of game time left " +
                                $"(until {Until(s.RemainingSeconds)}).", ToolTipIcon.Warning);
                            break; // at most one heads-up per poll
                        }
                }
                wasLocked = s.IsLocked;

                // Auto-pop the blocked window on a new block.
                if (lastBlockSeq < 0)
                {
                    lastBlockSeq = s.BlockSeq;        // skip events that predate startup
                }
                else if (s.RecentBlocks is { Count: > 0 } && s.BlockSeq > lastBlockSeq)
                {
                    var game = s.RecentBlocks.Where(b => b.Seq > lastBlockSeq)
                                             .Select(b => b.AppName).FirstOrDefault();
                    lastBlockSeq = s.BlockSeq;
                    if (game is not null && DateTime.UtcNow - lastPop > PopCooldown)
                    {
                        lastPop = DateTime.UtcNow;
                        OpenUnlock(game);
                    }
                }
            }
            catch { tray.Text = "CloveParentalGuard: service offline"; }
        };
        timer.Start();

        Application.Run();
        tray.Visible = false;
        tray.Dispose();
    }

    private static string StatusLine(PipeClient client)
    {
        try { return Describe(client.Send(new PipeRequest("status"))); }
        catch { return "Service offline"; }
    }

    private static string Describe(PipeResponse s) =>
        s.IsLocked
            ? "CloveParentalGuard — games are locked"
            : $"CloveParentalGuard — unlocked, {Remaining(s.RemainingSeconds)} left (until {Until(s.RemainingSeconds)})";

    private const string ToastAppId = "CloveParentalGuard";

    // Windows only shows toasts for apps it knows. An unpackaged exe registers a
    // per-user AppUserModelId (name shown on the toast) once; it is cheap to repeat.
    private static void RegisterToastApp()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\AppUserModelId\" + ToastAppId);
            key.SetValue("DisplayName", "CloveParentalGuard");
        }
        catch { /* toasts fall back to tray balloons */ }
    }

    private static void Notify(NotifyIcon tray, string title, string text, ToolTipIcon icon)
    {
        try
        {
            var xml = new Windows.Data.Xml.Dom.XmlDocument();
            xml.LoadXml("<toast><visual><binding template=\"ToastGeneric\">" +
                        $"<text>{System.Security.SecurityElement.Escape(title)}</text>" +
                        $"<text>{System.Security.SecurityElement.Escape(text)}</text>" +
                        "</binding></visual></toast>");
            Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(ToastAppId)
                .Show(new Windows.UI.Notifications.ToastNotification(xml));
            return;
        }
        catch { /* fall back to a tray balloon */ }

        tray.BalloonTipTitle = title;
        tray.BalloonTipText = text;
        tray.BalloonTipIcon = icon;
        tray.ShowBalloonTip(5000);
    }

    // Wall-clock time the current unlock expires, in the user's locale format.
    private static string Until(int seconds) => DateTime.Now.AddSeconds(seconds).ToString("t");

    private static string Duration(int seconds)
    {
        var m = (int)Math.Round(seconds / 60.0);
        return m >= 60 && m % 60 == 0 ? $"{m / 60} hour(s)" : $"{m} minutes";
    }

    // Live countdown for the menu header: "42:07" or "1:05:09".
    private static string Clock(int seconds) =>
        seconds >= 3600
            ? $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}"
            : $"{seconds / 60}:{seconds % 60:00}";

    // Compact remaining time for the tray tooltip: "45 sec", "42 min", "1h 05m".
    private static string Remaining(int seconds)
    {
        if (seconds < 60) return $"{seconds} sec";
        if (seconds < 3600) return $"{seconds / 60} min";
        return $"{seconds / 3600}h {seconds % 3600 / 60:00}m";
    }

    private static string Truncate(string text) =>
        text.Length <= 63 ? text : text[..63]; // NotifyIcon.Text hard limit
}
