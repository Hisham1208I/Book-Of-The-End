using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using BookOfTheEnd.Views;

namespace BookOfTheEnd.Services;

/// <summary>Outcome of querying GitHub Releases for a newer version.</summary>
public sealed record UpdateInfo(
    Version Version,
    string TagName,
    string ReleaseName,
    string Notes,
    string DownloadUrl,
    string AssetName,
    long Size);

public sealed record UpdateCheckResult(bool UpdateAvailable, UpdateInfo? Info, string? Error);

/// <summary>
/// Checks the project's GitHub Releases for a newer build, downloads the published
/// Setup.exe, and launches it to upgrade in place. Fully driven by what you publish
/// to GitHub Releases — no separate update server required.
/// </summary>
public sealed class UpdateService
{
    // Publish a GitHub release (tag like "v2.4.5") with the Setup.exe attached, and
    // the app will offer it to users. Change these if the repo moves.
    public const string RepoOwner = "Hisham1208I";
    public const string RepoName = "Book-Of-The-End";

    private static readonly HttpClient Http = CreateClient();
    private readonly LoggingService _log;

    public UpdateService(LoggingService log) => _log = log;

    /// <summary>Current app version as a 3-part value (Major.Minor.Build).</summary>
    public Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("BookOfTheEnd", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken token)
    {
        try
        {
            string url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            using var response = await Http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return new UpdateCheckResult(false, null, "No releases have been published yet.");
                return new UpdateCheckResult(false, null, $"GitHub returned {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var root = doc.RootElement;

            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string name = root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag;
            string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

            if (!TryParseVersion(tag, out Version? latest) || latest is null)
                return new UpdateCheckResult(false, null, "Latest release has no recognizable version tag.");

            (string assetUrl, string assetName, long size) = PickInstallerAsset(root);
            if (string.IsNullOrEmpty(assetUrl))
                return new UpdateCheckResult(false, null, "Latest release has no installer (.exe) attached.");

            bool available = latest > CurrentVersion;
            var info = new UpdateInfo(latest, tag, name, notes, assetUrl, assetName, size);
            return new UpdateCheckResult(available, info, null);
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult(false, null, "Update check timed out.");
        }
        catch (Exception ex)
        {
            _log.Warn($"Update check failed: {ex.Message}");
            return new UpdateCheckResult(false, null, ex.Message);
        }
    }

    public async Task<string> DownloadInstallerAsync(
        UpdateInfo info, IProgress<double>? progress, CancellationToken token)
    {
        string dir = Path.Combine(Path.GetTempPath(), "BookOfTheEndUpdate");
        Directory.CreateDirectory(dir);
        string safeName = string.IsNullOrWhiteSpace(info.AssetName)
            ? $"BookOfTheEnd-{info.TagName}-Setup.exe"
            : info.AssetName;
        string target = Path.Combine(dir, safeName);

        using var response = await Http.GetAsync(
            info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? info.Size;
        await using var src = await response.Content.ReadAsStreamAsync(token);
        await using var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[1024 * 256];
        long readTotal = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, token)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), token);
            readTotal += read;
            if (total > 0) progress?.Report(Math.Min(100, readTotal * 100.0 / total));
        }

        return target;
    }

    /// <summary>Launches the installer and shuts the app down so files can be replaced.</summary>
    public void LaunchInstallerAndExit(string installerPath)
    {
        Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
        _log.Info($"Launched updater installer: {installerPath}");
        Application.Current.Shutdown();
    }

    /// <summary>
    /// Full interactive flow: check, prompt, download with progress, then launch.
    /// When <paramref name="silent"/> is true, nothing is shown unless an update exists.
    /// </summary>
    public async Task CheckAndUpdateInteractiveAsync(Window? owner, bool silent)
    {
        var result = await CheckForUpdatesAsync(CancellationToken.None);

        if (!result.UpdateAvailable || result.Info is null)
        {
            if (silent) return;
            if (result.Error is not null)
                AppDialogWindow.ShowMessage(owner, "Update check",
                    "Could not check for updates.", result.Error, AppDialogKind.Warning);
            else
                AppDialogWindow.ShowMessage(owner, "You're up to date",
                    $"Book of the End {CurrentVersion} is the latest version.", null, AppDialogKind.Success);
            return;
        }

        var info = result.Info;
        string sizeText = info.Size > 0 ? $"  ({HumanSize(info.Size)})" : "";
        bool proceed = AppDialogWindow.ShowConfirm(
            owner,
            "Update available",
            $"Version {info.Version} is available. You have {CurrentVersion}.",
            $"The installer{sizeText} will download, then run to upgrade your installation.",
            confirmText: "Download & install",
            cancelText: "Not now");
        if (!proceed) return;

        var progressWindow = new UpdateProgressWindow { Owner = owner };
        string? installerPath = null;
        string? error = null;

        progressWindow.Loaded += async (_, _) =>
        {
            var progress = new Progress<double>(p => progressWindow.SetProgress(p));
            try
            {
                installerPath = await DownloadInstallerAsync(info, progress, CancellationToken.None);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _log.Warn($"Update download failed: {ex.Message}");
            }
            finally
            {
                progressWindow.Close();
            }
        };
        progressWindow.ShowDialog();

        if (installerPath is not null && File.Exists(installerPath))
        {
            LaunchInstallerAndExit(installerPath);
        }
        else
        {
            AppDialogWindow.ShowMessage(owner, "Update failed",
                "The update could not be downloaded.", error, AppDialogKind.Warning);
        }
    }

    private static (string url, string name, long size) PickInstallerAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return ("", "", 0);

        JsonElement? exe = null, setupExe = null, msi = null;
        foreach (var asset in assets.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase)) setupExe ??= asset;
                else exe ??= asset;
            }
            else if (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                msi ??= asset;
            }
        }

        var chosen = setupExe ?? exe ?? msi;
        if (chosen is null) return ("", "", 0);

        string url = chosen.Value.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
        string assetName = chosen.Value.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
        long size = chosen.Value.TryGetProperty("size", out var s) && s.TryGetInt64(out var sv) ? sv : 0;
        return (url, assetName, size);
    }

    private static bool TryParseVersion(string tag, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(tag)) return false;

        int start = 0;
        while (start < tag.Length && !char.IsDigit(tag[start])) start++;
        string digits = tag[start..];

        var parts = digits.Split('.', StringSplitOptions.RemoveEmptyEntries);
        int major = 0, minor = 0, build = 0;
        if (parts.Length < 1 || !int.TryParse(TrimNonDigits(parts[0]), out major)) return false;
        if (parts.Length >= 2) int.TryParse(TrimNonDigits(parts[1]), out minor);
        if (parts.Length >= 3) int.TryParse(TrimNonDigits(parts[2]), out build);

        version = new Version(major, minor, build);
        return true;
    }

    private static string TrimNonDigits(string s)
    {
        int end = 0;
        while (end < s.Length && char.IsDigit(s[end])) end++;
        return s[..end];
    }

    private static string HumanSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} {units[u]}" : $"{v:0.#} {units[u]}";
    }
}
