using CloveParentalGuard.Core;

namespace CloveParentalGuard.Admin;

public class AdminForm : Form
{
    private const string ConfigPath = @"C:\ProgramData\CloveParentalGuard\config.json";
    private CloveParentalGuardConfig _config = CloveParentalGuardConfig.Default();

    private readonly TextBox _code = new() { PlaceholderText = "New parent code (blank = keep)", Width = 360, UseSystemPasswordChar = true };
    private readonly TextBox _exes = new() { Multiline = true, Width = 360, Height = 90, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _folders = new() { Multiline = true, Width = 360, Height = 90, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _domains = new() { Multiline = true, Width = 360, Height = 90, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _durations = new() { Width = 360 };

    public AdminForm()
    {
        Text = "CloveParentalGuard — Admin";
        Width = 410; Height = 560; StartPosition = FormStartPosition.CenterScreen;
        BackColor = App.Theme.WindowBg; ForeColor = App.Theme.Text;
        ShowIcon = false;

        try { _config = ConfigStore.Load(ConfigPath); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not read config ({ex.Message}). Starting from defaults.");
            _config = CloveParentalGuardConfig.Default();
        }

        _exes.Text = string.Join(Environment.NewLine, _config.BlockedExecutables);
        _folders.Text = string.Join(Environment.NewLine, _config.BlockedFolders);
        _domains.Text = string.Join(Environment.NewLine, _config.BlockedDomains);
        _durations.Text = string.Join(",", _config.DurationsMinutes);

        int y = 12;
        AddRow("Parent code:", _code, ref y);
        AddRow("Blocked executables (one per line):", _exes, ref y);
        AddRow("Blocked folders (one per line):", _folders, ref y);
        AddRow("Blocked domains (one per line):", _domains, ref y);
        AddRow("Durations (minutes, comma-separated):", _durations, ref y);

        var save = new App.PillButton { Text = "Save", Primary = true, Left = 16, Top = y + 8, Width = 360, Height = 42 };
        save.Click += (_, _) => Save();
        Controls.Add(save);
    }

    private void AddRow(string label, Control input, ref int y)
    {
        Controls.Add(new Label { Text = label, Left = 16, Top = y, AutoSize = true });
        input.Left = 16; input.Top = y + 20;
        Controls.Add(input);
        y = input.Top + input.Height + 12;
    }

    private void Save()
    {
        if (!string.IsNullOrWhiteSpace(_code.Text))
            _config.Code = CodeHasher.Hash(_code.Text.Trim());

        _config.BlockedExecutables = Lines(_exes.Text);
        _config.BlockedFolders = Lines(_folders.Text);
        _config.BlockedDomains = Lines(_domains.Text);
        _config.DurationsMinutes = _durations.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => int.TryParse(s, out _)).Select(int.Parse).ToList();

        if (_config.Code is null)
        {
            MessageBox.Show("Set a parent code before saving.");
            return;
        }

        try
        {
            ConfigStore.Save(ConfigPath, _config);
            MessageBox.Show("Saved. Restart the CloveParentalGuard service to apply blocklist changes.");
        }
        catch (Exception ex) { MessageBox.Show("Save failed: " + ex.Message); }
    }

    private static List<string> Lines(string text) =>
        text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
