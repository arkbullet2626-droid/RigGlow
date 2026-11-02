using System.Diagnostics;
using System.Reflection;

namespace RigGlow;

/// <summary>Branding, links and version in one place.</summary>
public static class AppInfo
{
    public const string Name = "RigGlow";
    public const string Company = "Opifex Studios";
    public const string Tagline = "Free RGB lighting and fan control for Windows";
    public const string Website = "https://opifexstudios.ca";

    public const string RepoOwner = "arkbullet2626-droid";
    public const string RepoName = "RigGlow";
    public static string RepoUrl => $"https://github.com/{RepoOwner}/{RepoName}";
    public static string IssuesUrl => RepoUrl + "/issues";
    public static string ReleasesUrl => RepoUrl + "/releases";
    public static string LatestReleaseApi => $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    public const string OpenRgbUrl = "https://openrgb.org";
    public const string PawnIoUrl = "https://pawnio.eu";

    public static Version Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static string VersionText => Version.ToString(3);

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Couldn't open " + url + ": " + ex.Message); }
    }

    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Warn("Couldn't open folder: " + ex.Message); }
    }
}
