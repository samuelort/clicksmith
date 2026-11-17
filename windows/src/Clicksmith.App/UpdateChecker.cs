using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Clicksmith.Core;

namespace Clicksmith.App;

/// <summary>2.4: opt-in update check and install. Only reads public release data from the project's
/// GitHub releases and sends nothing about the user. Installing downloads the package for this PC,
/// verifies its SHA-256 against release-windows.json, and hands over to the package's own installer
/// (which backs up the setup, keeps the previous version and rolls back on failure).</summary>
internal static class UpdateChecker
{
    public sealed record Result(bool Newer, string Message, string? Url, string? Tag);

    private static readonly Regex TagPattern = new(@"^windows-v\d+\.\d+\.\d+(-[0-9A-Za-z.\-]+)?$");
    public static string Arch => RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";

    private static HttpClient Client()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Clicksmith-Windows/" + ClicksmithInfo.Version);
        return http;
    }

    public static async Task<Result> CheckAsync()
    {
        try
        {
            using var http = Client();
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            var json = await http.GetStringAsync($"https://api.github.com/repos/{ClicksmithInfo.Repository}/releases?per_page=30");
            using var doc = JsonDocument.Parse(json);
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                if (r.TryGetProperty("draft", out var d) && d.GetBoolean()) continue;
                var tag = r.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                if (tag is null || !TagPattern.IsMatch(tag)) continue;
                var url = r.TryGetProperty("html_url", out var u) ? u.GetString() : null;
                if (url is not null && !url.StartsWith("https://github.com/", StringComparison.Ordinal)) url = null;
                return UpdatePolicy.IsNewer(tag, ClicksmithInfo.Version)
                    ? new Result(true, $"Clicksmith {tag[ClicksmithInfo.ReleaseTagPrefix.Length..]} is available.", url, tag)
                    : new Result(false, $"You're up to date ({ClicksmithInfo.DisplayVersion}).", url, null);
            }
            return new Result(false, "No Windows release has been published yet.", null, null);
        }
        catch (Exception e)
        {
            return new Result(false, "Couldn't check for updates: " + e.Message, null, null);
        }
    }

    /// <summary>Downloads, verifies and starts the installer. Returns an error, or null when the
    /// installer has started (the app should then quit).</summary>
    public static async Task<string?> InstallAsync(string tag, IProgress<string> progress)
    {
        if (!TagPattern.IsMatch(tag)) return "Invalid release tag.";
        var dir = Path.Combine(Path.GetTempPath(), "Clicksmith-update", tag);
        try
        {
            using var http = Client();
            var baseUrl = $"https://github.com/{ClicksmithInfo.Repository}/releases/download/{tag}/";
            progress.Report("Reading the release…");
            var index = ReleaseIndex.Parse(await http.GetByteArrayAsync(baseUrl + "release-windows.json"));
            var manifest = index.Pick(Arch);
            if (!UpdatePolicy.IsNewer(ClicksmithInfo.ReleaseTagPrefix + manifest.Version, ClicksmithInfo.Version))
                return "This release isn't newer than the installed version.";

            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            Directory.CreateDirectory(dir);
            var zip = Path.Combine(dir, manifest.Artifact);
            progress.Report("Downloading " + manifest.Artifact + "…");
            using (var response = await http.GetAsync(baseUrl + manifest.Artifact, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > 300_000_000) return "The package is unexpectedly large.";
                await using var src = await response.Content.ReadAsStreamAsync();
                await using var dst = File.Create(zip);
                await src.CopyToAsync(dst);
            }

            progress.Report("Verifying the package…");
            manifest.VerifyFile(zip);
            var extract = Path.Combine(dir, "package");
            ZipFile.ExtractToDirectory(zip, extract); // refuses entries outside the folder
            var installer = Path.Combine(extract, "scripts", "install-release.ps1");
            if (!File.Exists(installer)) return "The package has no installer.";

            progress.Report("Starting the installer…");
            Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{installer}\" -Title Update")
            { UseShellExecute = true, WorkingDirectory = extract })?.Dispose();
            return null;
        }
        catch (UpdateException e) { return e.Message; }
        catch (Exception e) { return "Update failed: " + e.Message; }
    }
}
