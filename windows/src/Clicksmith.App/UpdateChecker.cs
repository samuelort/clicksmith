using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Clicksmith.Core;

namespace Clicksmith.App;

/// <summary>2.4: opt-in check against the project's GitHub releases. Only reads public release
/// metadata; sends nothing about the user. Never downloads or installs by itself.</summary>
internal static class UpdateChecker
{
    public sealed record Result(bool Newer, string Message, string? Url);

    public static async Task<Result> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Clicksmith-Windows/" + ClicksmithInfo.Version);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            var json = await http.GetStringAsync($"https://api.github.com/repos/{ClicksmithInfo.Repository}/releases?per_page=30");
            using var doc = JsonDocument.Parse(json);
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                if (r.TryGetProperty("draft", out var d) && d.GetBoolean()) continue;
                var tag = r.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                if (tag is null || !tag.StartsWith(ClicksmithInfo.ReleaseTagPrefix, StringComparison.Ordinal)) continue;
                var url = r.TryGetProperty("html_url", out var u) ? u.GetString() : null;
                if (url is not null && !url.StartsWith("https://github.com/", StringComparison.Ordinal)) url = null;
                return UpdatePolicy.IsNewer(tag, ClicksmithInfo.Version)
                    ? new Result(true, $"Clicksmith {tag[ClicksmithInfo.ReleaseTagPrefix.Length..]} is available. Download it, then run update.cmd.", url)
                    : new Result(false, $"You're up to date ({ClicksmithInfo.DisplayVersion}).", url);
            }
            return new Result(false, "No Windows release has been published yet.", null);
        }
        catch (Exception e)
        {
            return new Result(false, "Couldn't check for updates: " + e.Message, null);
        }
    }
}
