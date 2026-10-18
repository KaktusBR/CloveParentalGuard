namespace CloveParentalGuard.Core.Tests;

public class BlocklistMatcherTests
{
    private static CloveParentalGuardConfig Cfg() => new()
    {
        BlockedExecutables = new() { "steam.exe", "Battle.net.exe" },
        BlockedFolders = new() { @"C:\Games\steamapps\common" }
    };

    [Fact]
    public void Blocks_By_Exe_Name_Case_Insensitive()
        => Assert.True(BlocklistMatcher.IsBlocked("STEAM.EXE", null, Cfg()));

    [Fact]
    public void Blocks_By_Exe_Name_Without_Extension()
        => Assert.True(BlocklistMatcher.IsBlocked("steam", null, Cfg()));

    [Fact]
    public void Blocks_By_Folder_Root()
        => Assert.True(BlocklistMatcher.IsBlocked("game.exe", @"C:\Games\steamapps\common\Doom\doom.exe", Cfg()));

    [Fact]
    public void Allows_Unrelated_Process()
        => Assert.False(BlocklistMatcher.IsBlocked("notepad.exe", @"C:\Windows\notepad.exe", Cfg()));

    [Fact]
    public void Malformed_Folder_Entry_Does_Not_Throw()
    {
        var cfg = new CloveParentalGuardConfig { BlockedFolders = new() { "::::invalid::::" } };
        var ex = Record.Exception(() => BlocklistMatcher.IsBlocked("game.exe", @"C:\x\game.exe", cfg));
        Assert.Null(ex);
    }
}
