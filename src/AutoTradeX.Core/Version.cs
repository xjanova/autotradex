namespace AutoTradeX.Core;

public static class AppVersion
{
    public const string Major = "1";
    public const string Minor = "0";
    public const string Patch = "0";
    public const string Build = "2024112901";

    public static string Full => $"{Major}.{Minor}.{Patch}";
    public static string FullWithBuild => $"{Major}.{Minor}.{Patch}.{Build}";
    public static string Display => $"v{Full}";
}
