using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;
using GameGuard.Core;

namespace GameGuard.App;

/// <summary>
/// Shown when GameGuard.exe is double-clicked: set the parent code and
/// install/update or uninstall. Privileged work runs in a self-elevated
/// "--install" / "--uninstall" relaunch, so this window stays unelevated.
/// </summary>
[SupportedOSPlatform("windows")]
public class SetupForm : Form
{
    private const int ContentWidth = 360;

    private readonly FlowLayoutPanel _setup = Stack();
    private readonly FlowLayoutPanel _success = Stack();

    private readonly Label _subtitle = new();
    private readonly RoundedInput _code = new("Parent code", password: true);
    private readonly RoundedInput _confirm = new("Confirm parent code", password: true);
    private readonly PillButton _install = new() { Primary = true };
    private readonly PillButton _uninstall = new() { Primary = false, Text = "Uninstall" };
    private readonly Label _status = new();
    private readonly Label _successText = new();

    public SetupForm()
    {
        Theme.StyleForm(this);
        Text = "GameGuard Setup";

        BuildSetupPanel();
        BuildSuccessPanel();
        _success.Visible = false;
        Controls.Add(_setup);
        Controls.Add(_success);

        RefreshState();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        SizeToPanel(_setup);
    }

    // Size the window from the actual laid-out control positions — reliable at any DPI,
    // unlike FlowLayoutPanel.PreferredSize which under-reports trailing margins.
    private void SizeToPanel(FlowLayoutPanel panel)
    {
        panel.PerformLayout();
        int right = 0, bottom = 0;
        foreach (Control c in panel.Controls)
        {
            if (!c.Visible) continue;
            right = Math.Max(right, c.Right + c.Margin.Right);
            bottom = Math.Max(bottom, c.Bottom + c.Margin.Bottom);
        }
        ClientSize = new Size(right + panel.Padding.Right, bottom + panel.Padding.Bottom);
    }

    private static FlowLayoutPanel Stack() => new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Location = new Point(0, 0),
        Padding = new Padding(32, 28, 32, 28),
        BackColor = Theme.WindowBg,
    };

    private void BuildSetupPanel()
    {
        var icon = new Label
        {
            AutoSize = true, Font = Theme.Glyph(42f), ForeColor = Theme.Accent,
            Text = Theme.GlyphController, Margin = new Padding(0, 0, 0, 10),
        };
        var title = new Label
        {
            AutoSize = true, Font = Theme.Title(), ForeColor = Theme.Text,
            Text = "GameGuard", Margin = new Padding(0, 0, 0, 4),
        };
        _subtitle.AutoSize = true;
        _subtitle.MaximumSize = new Size(ContentWidth, 0);
        _subtitle.Font = Theme.Body();
        _subtitle.ForeColor = Theme.Secondary;
        _subtitle.Margin = new Padding(0, 0, 0, 20);

        _code.Width = ContentWidth;
        _code.Margin = new Padding(0, 0, 0, 16);
        _confirm.Width = ContentWidth;
        _confirm.Margin = new Padding(0, 0, 0, 22);

        _install.Width = ContentWidth;
        _install.Margin = new Padding(0, 0, 0, 12);
        _install.Click += (_, _) => DoInstall();

        _uninstall.Width = ContentWidth;
        _uninstall.Margin = new Padding(0, 0, 0, 14);
        _uninstall.Click += (_, _) => DoUninstall();

        _status.AutoSize = true;
        _status.MaximumSize = new Size(ContentWidth, 0);
        _status.MinimumSize = new Size(ContentWidth, 20);
        _status.Font = Theme.Body();
        _status.ForeColor = Theme.Secondary;

        _setup.Controls.AddRange(new Control[]
        {
            icon, title, _subtitle,
            Theme.FieldCaption("PARENT CODE"), _code,
            Theme.FieldCaption("CONFIRM CODE"), _confirm,
            _install, _uninstall, _status,
        });
    }

    private void BuildSuccessPanel()
    {
        var badge = new CheckBadge { Margin = new Padding((ContentWidth - 60) / 2, 24, 0, 18) };
        var heading = new Label
        {
            AutoSize = false, Width = ContentWidth, Height = 46, TextAlign = ContentAlignment.MiddleCenter,
            Font = Theme.Title(), ForeColor = Theme.Text, Text = "All set", Margin = new Padding(0, 0, 0, 6),
        };
        _successText.AutoSize = false;
        _successText.Width = ContentWidth;
        _successText.Height = 72;
        _successText.TextAlign = ContentAlignment.MiddleCenter;
        _successText.Font = Theme.Body();
        _successText.ForeColor = Theme.Secondary;
        _successText.Margin = new Padding(0, 0, 0, 22);

        var done = new PillButton { Primary = true, Text = "Done", Width = 200 };
        done.Margin = new Padding((ContentWidth - 200) / 2, 0, 0, 4);
        done.Click += (_, _) => Close();

        _success.Controls.AddRange(new Control[] { badge, heading, _successText, done });
    }

    private void RefreshState()
    {
        var installed = Installer.IsInstalled();
        _install.Text = installed ? "Update" : "Install";
        _uninstall.Enabled = installed;
        _subtitle.Text = installed
            ? "GameGuard is installed. Leave the code blank to keep the current one, or set a new code and Update."
            : "Set a parent code, then Install. Games stay blocked until the code is entered.";
    }

    // ---- actions ----------------------------------------------------------

    private void DoInstall()
    {
        var installed = Installer.IsInstalled();
        var code = _code.Text.Trim();

        if (!installed && code.Length == 0)
        {
            Status("Enter a parent code first.", Theme.Danger);
            return;
        }
        if (code.Length > 0 && code != _confirm.Text.Trim())
        {
            Status("The two codes do not match.", Theme.Danger);
            return;
        }

        string? hashFile = null;
        if (code.Length > 0)
        {
            hashFile = Path.Combine(Path.GetTempPath(), $"gg-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(hashFile, JsonSerializer.Serialize(CodeHasher.Hash(code)));
        }

        var args = hashFile is null ? new[] { "--install" } : new[] { "--install", hashFile };
        RunElevated(args, installed ? "Updating…" : "Installing…", () =>
        {
            Installer.StartAgentInteractive();
            _successText.Text = "Games are blocked and the controller icon is now " +
                                "in your system tray.";
            ShowSuccess();
        });
    }

    private void DoUninstall()
    {
        var confirm = MessageBox.Show(
            "Remove GameGuard and stop blocking games on this PC?",
            "GameGuard", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        RunElevated(new[] { "--uninstall" }, "Removing…", () =>
        {
            _code.Clear(); _confirm.Clear();
            RefreshState();
            Status("GameGuard has been removed.", Theme.Secondary);
            // Running from the installed copy: close so its folder can be deleted.
            if (string.Equals(Environment.ProcessPath, Installer.InstalledExe,
                    StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("GameGuard has been removed.", "GameGuard");
                Close();
            }
        });
    }

    private async void RunElevated(string[] args, string busyText, Action success)
    {
        SetBusy(true);
        Status(busyText + " Approve the Windows prompt.", Theme.Secondary);

        var code = await Task.Run(() => Installer.RelaunchElevated(args));

        SetBusy(false);
        switch (code)
        {
            case 0:
                success();
                break;
            case -1:
                Status("Administrator approval is required. Please try again and choose Yes.", Theme.Danger);
                break;
            default:
                Status($"Setup failed (code {code}). See gameguard-setup.log in your temp folder.", Theme.Danger);
                break;
        }
    }

    private void ShowSuccess()
    {
        _setup.Visible = false;
        _success.Visible = true;
        _success.BringToFront();
        SizeToPanel(_success); // resize the window to the success panel
    }

    private void SetBusy(bool busy)
    {
        _install.Enabled = !busy;
        _uninstall.Enabled = !busy && Installer.IsInstalled();
        _code.Enabled = _confirm.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void Status(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }
}
