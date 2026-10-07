using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace MarkdownPro.Services
{
    /// <summary>
    /// Represents information about an available update from GitHub Releases.
    /// </summary>
    public class UpdateInfo
    {
        public string TagName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string ReleaseTitle { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public string ReleasePageUrl { get; set; } = string.Empty;
        public string? InstallerDownloadUrl { get; set; }
        public string? PortableDownloadUrl { get; set; }
        public long InstallerSize { get; set; }
        public DateTime PublishedAt { get; set; }
    }

    /// <summary>
    /// Checks GitHub Releases for newer versions and optionally downloads &amp; launches the installer.
    /// </summary>
    public static class UpdateService
    {
        // ════════════════════════════════════════════════════════════════
        //  GitHub Repository Configuration
        // ════════════════════════════════════════════════════════════════
        public const string GitHubOwner = "KevinK56";
        public const string GitHubRepo = "MarkdownPro";

        private static readonly string LatestReleaseApiUrl =
            $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

        private static readonly HttpClient SharedHttpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("MarkdownPro-App", GetCurrentVersionString()));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }

        /// <summary>
        /// Gets the currently running application version string (e.g. "1.0.0").
        /// </summary>
        public static string GetCurrentVersionString()
        {
            var ver = GetCurrentVersion();
            return ver.Revision > 0
                ? $"{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}"
                : $"{ver.Major}.{ver.Minor}.{ver.Build}";
        }

        /// <summary>
        /// Gets the currently running application <see cref="Version"/>.
        /// </summary>
        public static Version GetCurrentVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
        }

        /// <summary>
        /// Queries GitHub Releases API to check if a newer version is available.
        /// Returns <c>null</c> if the app is up-to-date or if the check fails silently.
        /// </summary>
        public static async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            using var response = await SharedHttpClient.GetAsync(LatestReleaseApiUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tagProp)
                ? tagProp.GetString() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(tagName))
            {
                return null;
            }

            // Strip leading 'v' or 'V' from tag (e.g. "v2026.10.06.1" -> "2026.10.06.1")
            var cleanVer = tagName.TrimStart('v', 'V').Split('-')[0];
            if (!TryParseVersion(cleanVer, out var remoteVersion))
            {
                return null;
            }

            var currentVersion = GetCurrentVersion();
            if (remoteVersion <= currentVersion)
            {
                return null; // Already up to date
            }

            var update = new UpdateInfo
            {
                TagName = tagName,
                Version = cleanVer,
                ReleaseTitle = root.TryGetProperty("name", out var nameProp)
                    ? nameProp.GetString() ?? tagName
                    : tagName,
                ReleaseNotes = root.TryGetProperty("body", out var bodyProp)
                    ? bodyProp.GetString() ?? string.Empty
                    : string.Empty,
                ReleasePageUrl = root.TryGetProperty("html_url", out var urlProp)
                    ? urlProp.GetString() ?? $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases"
                    : $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases",
                PublishedAt = root.TryGetProperty("published_at", out var pubProp) &&
                              DateTime.TryParse(pubProp.GetString(), out var dt)
                    ? dt
                    : DateTime.UtcNow
            };

            // Inspect release assets for Setup .exe and Portable .zip
            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    var assetName = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var downloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    var size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0L;

                    if (string.IsNullOrEmpty(downloadUrl))
                    {
                        continue;
                    }

                    if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        assetName.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                    {
                        update.InstallerDownloadUrl = downloadUrl;
                        update.InstallerSize = size;
                    }
                    else if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        update.PortableDownloadUrl = downloadUrl;
                    }
                }
            }

            return update;
        }

        /// <summary>
        /// Downloads the installer executable to the user's temp folder and reports progress (0..100).
        /// </summary>
        public static async Task<string> DownloadInstallerAsync(
            string downloadUrl,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "MarkdownPro_Updates");
            Directory.CreateDirectory(tempDir);

            var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
            if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                fileName = "MarkdownPro-Setup.exe";
            }

            var destinationPath = Path.Combine(tempDir, fileName);

            using var response = await SharedHttpClient.GetAsync(
                downloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;

                if (totalBytes > 0 && progress != null)
                {
                    var pct = Math.Round((double)totalRead / totalBytes * 100, 1);
                    progress.Report(pct);
                }
            }

            progress?.Report(100);
            return destinationPath;
        }

        /// <summary>
        /// Launches the downloaded Inno Setup installer and closes the current application so files can be replaced.
        /// </summary>
        public static void LaunchInstallerAndShutdown(string installerPath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                UseShellExecute = true
            };

            Process.Start(psi);
            Environment.Exit(0);
        }

        /// <summary>
        /// Opens a URL in the user's default browser.
        /// </summary>
        public static void OpenUrlInBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore if browser launch fails
            }
        }

        private static bool TryParseVersion(string input, out Version version)
        {
            var parts = input.Split('.');
            while (parts.Length < 4)
            {
                input += ".0";
                parts = input.Split('.');
            }

            if (Version.TryParse(input, out var parsed))
            {
                version = parsed;
                return true;
            }

            version = new Version(1, 0, 0, 0);
            return false;
        }
    }
}
