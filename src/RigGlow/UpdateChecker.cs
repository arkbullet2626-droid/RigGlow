using System.Net.Http;
using System.Text.Json;

namespace RigGlow;

/// <summary>
/// Asks GitHub for the latest release. This is the only network request RigGlow makes,
/// and it can be turned off in Settings.
/// </summary>
public static class UpdateChecker
{
    public sealed class Result
    {
        public Version Latest;
        public string Url;
        public bool IsNewer;
    }

    public static async Task<Result> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.Name}/{AppInfo.VersionText}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            var json = await http.GetStringAsync(AppInfo.LatestReleaseApi).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() : AppInfo.ReleasesUrl;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
            latest = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));
            return new Result { Latest = latest, Url = url, IsNewer = latest > AppInfo.Version };
        }
        catch (Exception ex)
        {
            Log.Info("Update check skipped: " + ex.Message);
            return null;
        }
    }
}
