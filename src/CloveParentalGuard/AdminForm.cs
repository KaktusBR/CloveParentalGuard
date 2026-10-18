using CloveParentalGuard.Core;
using CloveParentalGuard.Service;

namespace CloveParentalGuard.Admin;

public class AdminForm : Form
{
    private const string ConfigPath = @"C:\ProgramData\CloveParentalGuard\config.json";
    private CloveParentalGuardConfig _config = CloveParentalGuardConfig.Default();

    private readonly BlocklistFile _builtIn = BuiltInBlocklist.Load();
    private readonly BlocklistCache _cache = new(Constants.DataDir, BuiltInBlocklist.TrustedKeys());

    private readonly TextBox _code = new() { PlaceholderText = "New parent code (blank = keep)", Width = 360, UseSystemPasswordChar = true };
    private readonly TextBox _exes = new() { Multiline = true, Width = 360, Height = 70, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _folders = new() { Multiline = true, Width = 360, Height = 70, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _domains = new() { Multiline = true, Width = 360, Height = 70, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _allow = new() { Multiline = true, Width = 360, Height = 70, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _durations = new() { Width = 360 };

    private readonly Label _listStatus = new() { Left = 16, Width = 360, Height = 36, AutoSize = false };
    private readonly App.PillButton _fetch = new() { Text = "Fetch latest", Primary = false, Width = 176, Height = 36 };
    private readonly App.PillButton _reset = new() { Text = "Reset to built-in", Primary = false, Width = 176, Height = 36 };

    public AdminForm()
    {
        Text = "CloveParentalGuard — Admin";
        Width = 410; Height = 800; StartPosition = FormStartPosition.CenterScreen;
        BackColor = App.Theme.WindowBg; ForeColor = App.Theme.Text;
        ShowIcon = false;
        AutoScroll = true;

        try { _config = ConfigStore.Load(ConfigPath); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not read config ({ex.Message}). Starting from defaults.");
            _config = CloveParentalGuardConfig.Default();
        }

        _exes.Text = string.Join(Environment.NewLine, _config.BlockedExecutables);
        _folders.Text = string.Join(Environment.NewLine, _config.BlockedFolders);
        _domains.Text = string.Join(Environment.NewLine, _config.BlockedDomains);
        _allow.Text = string.Join(Environment.NewLine, _config.Allow);
        _durations.Text = string.Join(",", _config.DurationsMinutes);

        int y = 12;

        // Online blocklist: status + manual refresh.
        Controls.Add(new Label { Text = "Blocklist (updated automatically from GitHub):", Left = 16, Top = y, AutoSize = true });
        _listStatus.Top = y + 20;
        Controls.Add(_listStatus);
        y = _listStatus.Bottom + 4;
        _fetch.Left = 16; _fetch.Top = y;
        _reset.Left = 16 + 184; _reset.Top = y;
        _fetch.Click += async (_, _) => await FetchLatest();
        _reset.Click += (_, _) => ResetToBuiltIn();
        Controls.Add(_fetch);
        Controls.Add(_reset);
        y = _fetch.Bottom + 16;
        RefreshListStatus();

        AddRow("Parent code:", _code, ref y);
        AddRow("Extra blocked executables (one per line):", _exes, ref y);
        AddRow("Extra blocked folders (one per line):", _folders, ref y);
        AddRow("Extra blocked domains (one per line):", _domains, ref y);
        AddRow("Never block (allow list - overrides every list):", _allow, ref y);
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

    private void RefreshListStatus()
    {
        var cached = _cache.Load();
        var active = EffectiveBlocklist.Pick(_builtIn, cached);
        var source = ReferenceEquals(active, _builtIn) ? "built-in" : "downloaded";
        var date = DateTimeOffset.FromUnixTimeSeconds(active.Version).LocalDateTime;
        _listStatus.Text = $"Using the {source} list from {date:d MMM yyyy HH:mm}: " +
                           $"{active.Executables.Count} programs, {active.Folders.Count} folders, " +
                           $"{active.Domains.Count} sites.";
    }

    private async Task FetchLatest()
    {
        _fetch.Enabled = _reset.Enabled = false;
        _listStatus.Text = "Checking GitHub…";
        try
        {
            var current = EffectiveBlocklist.Pick(_builtIn, _cache.Load()).Version;
            var result = await Task.Run(() => BlocklistFetcher.UpdateAsync(_cache, current));
            RefreshListStatus();
            _listStatus.Text = result.Message + "  " + _listStatus.Text;
        }
        catch (Exception ex) { _listStatus.Text = "Fetch failed: " + ex.Message; }
        finally { _fetch.Enabled = _reset.Enabled = true; }
    }

    private void ResetToBuiltIn()
    {
        _cache.Clear();
        RefreshListStatus();
    }

    private void Save()
    {
        if (!string.IsNullOrWhiteSpace(_code.Text))
            _config.Code = CodeHasher.Hash(_code.Text.Trim());

        _config.BlockedExecutables = Lines(_exes.Text);
        _config.BlockedFolders = Lines(_folders.Text);
        _config.BlockedDomains = Lines(_domains.Text);
        _config.Allow = Lines(_allow.Text);
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
            MessageBox.Show("Saved. Changes apply within a few seconds.");
        }
        catch (Exception ex) { MessageBox.Show("Save failed: " + ex.Message); }
    }

    private static List<string> Lines(string text) =>
        text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
