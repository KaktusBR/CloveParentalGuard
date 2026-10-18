namespace CloveParentalGuard.Service;

public static class Constants
{
    public const string PipeName = "CloveParentalGuardPipe";
    public static readonly string ConfigPath =
        @"C:\ProgramData\CloveParentalGuard\config.json";
    public static readonly string HostsPath =
        Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\drivers\etc\hosts");
    public static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(2);
}
