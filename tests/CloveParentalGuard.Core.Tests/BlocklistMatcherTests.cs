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

    [Theory]
    [InlineData(@"javaw -Xmx2G net.minecraft.client.main.Main --username kid")]                       // vanilla
    [InlineData(@"javaw cpw.mods.bootstraplauncher.BootstrapLauncher --launchTarget forgeclient")]    // Forge
    [InlineData(@"javaw cpw.mods.bootstraplauncher.BootstrapLauncher --launchTarget neoforgeclient")] // NeoForge
    [InlineData(@"javaw net.fabricmc.loader.impl.launch.knot.KnotClient")]                            // Fabric
    [InlineData(@"javaw org.quiltmc.loader.impl.launch.knot.KnotClient")]                             // Quilt
    [InlineData(@"javaw net.minecraft.launchwrapper.Launch --tweakClass x")]                          // legacy
    [InlineData(@"JAVAW NET.MINECRAFT.CLIENT.MAIN.MAIN")]                                             // case-insensitive
    public void Blocks_Minecraft_Game_By_Command_Line_Whatever_The_Launcher(string commandLine)
        => Assert.True(BlocklistMatcher.IsBlocked("javaw.exe", @"C:\Java\bin\javaw.exe", BuiltInCfg(), commandLine));

    [Theory]
    [InlineData(@"javaw -jar server.jar nogui net.minecraft.server.Main")]   // dedicated server, not the client
    [InlineData(@"javaw -jar some-ide.jar")]
    [InlineData(null)]
    [InlineData("")]
    public void Allows_Other_Java_Programs(string? commandLine)
        => Assert.False(BlocklistMatcher.IsBlocked("javaw.exe", @"C:\Java\bin\javaw.exe", BuiltInCfg(), commandLine));

    // The built-in list merged into an empty parent config, as the service enforces it.
    private static CloveParentalGuardConfig BuiltInCfg()
        => EffectiveBlocklist.Build(new CloveParentalGuardConfig(), BuiltInBlocklist.Load());

    [Fact]
    public void Malformed_Folder_Entry_Does_Not_Throw()
    {
        var cfg = new CloveParentalGuardConfig { BlockedFolders = new() { "::::invalid::::" } };
        var ex = Record.Exception(() => BlocklistMatcher.IsBlocked("game.exe", @"C:\x\game.exe", cfg));
        Assert.Null(ex);
    }
}
