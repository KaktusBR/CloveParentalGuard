namespace GameGuard.Core;

public class GameGuardConfig
{
    public HashedCode? Code { get; set; }
    public List<string> BlockedExecutables { get; set; } = new();
    public List<string> BlockedFolders { get; set; } = new();
    public List<string> BlockedDomains { get; set; } = new();
    public List<int> DurationsMinutes { get; set; } = new();

    public static GameGuardConfig Default() => new()
    {
        BlockedExecutables = new()
        {
            "steam.exe", "EpicGamesLauncher.exe", "Battle.net.exe",
            "GalaxyClient.exe", "Origin.exe", "EADesktop.exe",
            "RiotClientServices.exe", "Ubisoft Connect.exe", "upc.exe",
            "MinecraftLauncher.exe", "Minecraft.exe", "Minecraft.Windows.exe"
        },
        BlockedFolders = new()
        {
            @"C:\Program Files (x86)\Steam\steamapps\common",
            @"C:\Program Files\Epic Games",
            @"C:\Users\*\AppData\Roaming\.minecraft",
            @"C:\Users\*\AppData\Local\Packages\Microsoft.4297127D64EC6_8wekyb3d8bbwe\LocalCache\Local\runtime",
            @"C:\Program Files (x86)\Minecraft Launcher\runtime"
        },
        BlockedDomains = new()
        {
            "poki.com", "crazygames.com", "miniclip.com", "roblox.com", "now.gg",
            "coolmathgames.com", "addictinggames.com", "kongregate.com", "minecraft.net"
        },
        DurationsMinutes = new() { 30, 60, 120, 180 }
    };
}
