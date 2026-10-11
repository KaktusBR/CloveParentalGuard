using System.Runtime.Versioning;
using GameGuard.Core;

namespace GameGuard.Agent;

/// <summary>
/// One window, two faces: the tray "Unlock games" dialog, and the auto-popped
/// "&lt;game&gt; is blocked" prompt. Both unlock everything for a chosen duration.
/// Layout is an auto-sizing vertical stack so nothing clips at any DPI.
/// </summary>
[SupportedOSPlatform("windows")]
public class UnlockForm : Form
{
    private const int ContentWidth = 348;

    private readonly PipeClient _client;
    private readonly App.RoundedInput _code = new("Parent code", password: true);
    private readonly App.DurationPicker _durations = new();
    private readonly App.PillButton _unlock = new() { Text = "Unlock", Primary = true };
    private readonly Label _status = new();
    private readonly System.Windows.Forms.Timer _closeTimer = new() { Interval = 1300 };

    public UnlockForm(PipeClient client, IEnumerable<int> durations, string? blockedGame = null,
        bool previewMode = false)
    {
        _client = client;
        var blocked = !string.IsNullOrWhiteSpace(blockedGame);

        App.Theme.StyleForm(this);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Text = blocked ? "GameGuard" : "GameGuard — Unlock";

        var root = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(30, 26, 30, 26),
            BackColor = App.Theme.WindowBg,
        };

        var icon = new Label
        {
            AutoSize = true,
            Font = App.Theme.Glyph(42f),
            ForeColor = blocked ? App.Theme.Danger : App.Theme.Accent,
            Text = blocked ? App.Theme.GlyphLock : App.Theme.GlyphController,
            Margin = new Padding(0, 0, 0, 10),
        };
        var title = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Font = App.Theme.Title(),
            ForeColor = App.Theme.Text,
            Text = blocked ? $"{blockedGame} is blocked" : "Unlock games",
            Margin = new Padding(0, 0, 0, 4),
        };
        var subtitle = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Font = App.Theme.Body(),
            ForeColor = App.Theme.Secondary,
            Text = blocked
                ? $"GameGuard closed {blockedGame} because games are locked. Call a parent if you want to play. A parent can enter the code below to unlock games."
                : "Enter the parent code and choose how long to allow games.",
            Margin = new Padding(0, 0, 0, 20),
        };

        _code.Width = ContentWidth;
        _code.Margin = new Padding(0, 0, 0, 18);
        if (previewMode) _code.Enabled = false;

        _durations.Margin = new Padding(0, 0, 0, 22);
        _durations.SetDurations(durations);
        if (previewMode) _durations.Enabled = false;

        _unlock.Width = ContentWidth;
        if (previewMode) _unlock.Text = "Close preview";
        _unlock.Margin = new Padding(0, 0, 0, 12);
        _unlock.Click += (_, _) =>
        {
            if (previewMode) Close();
            else DoUnlock();
        };

        _status.AutoSize = true;
        _status.MaximumSize = new Size(ContentWidth, 0);
        _status.MinimumSize = new Size(ContentWidth, 20);
        _status.Font = App.Theme.Body();
        _status.Margin = Padding.Empty;

        AcceptButton = _unlock;
        _closeTimer.Tick += (_, _) => { _closeTimer.Stop(); Close(); };

        root.Controls.AddRange(new Control[]
        {
            icon, title, subtitle,
            App.Theme.FieldCaption("PARENT CODE"), _code,
            App.Theme.FieldCaption("FOR"), _durations,
            _unlock, _status,
        });
        Controls.Add(root);

        Shown += (_, _) => { if (_code.Enabled) _code.Box.Focus(); };
    }

    private void DoUnlock()
    {
        if (_durations.SelectedMinutes is not int minutes)
        {
            ShowStatus("Choose a duration.", App.Theme.Danger);
            return;
        }
        try
        {
            var resp = _client.Send(new PipeRequest("unlock", _code.Text, minutes));
            if (resp.Success)
            {
                _unlock.Enabled = _code.Enabled = false;
                ShowStatus($"✓  Unlocked for {Format(minutes)}.", App.Theme.Success);
                _closeTimer.Start();
            }
            else
            {
                ShowStatus(resp.Error ?? "Unlock failed.", App.Theme.Danger);
            }
        }
        catch (Exception ex)
        {
            ShowStatus("Service unavailable: " + ex.Message, App.Theme.Danger);
        }
    }

    private void ShowStatus(string text, Color color)
    {
        _status.ForeColor = color;
        _status.Text = text;
    }

    private static string Format(int minutes) =>
        minutes % 60 == 0 ? $"{minutes / 60} hour(s)" : $"{minutes} minutes";

    protected override void Dispose(bool disposing)
    {
        if (disposing) _closeTimer.Dispose();
        base.Dispose(disposing);
    }
}
