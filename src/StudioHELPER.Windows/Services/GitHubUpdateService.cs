using StudioHELPER.Core;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace StudioHELPER.Windows.Services;

internal sealed class GitHubUpdateService
{
    private const string LatestReleaseApi = "https" + "://api.github.com/repos/fct-sk/StudioHELPER/releases/latest";
    private const string InstallerAssetName = "StudioHELPER-Setup.exe";
    private const string ChecksumAssetName = "StudioHELPER-Setup.exe.sha256";
    private readonly HttpClient _httpClient;

    public GitHubUpdateService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("StudioHELPER-Updater/0.1");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public Version CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 1, 0, 0);

    public async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(LatestReleaseApi, cancellationToken);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!TryParseVersion(tag, out Version? version) || version <= CurrentVersion) return null;

        string? installerUrl = null;
        string? checksumUrl = null;
        foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? string.Empty;
            string? url = asset.GetProperty("browser_download_url").GetString();
            if (name.Equals(InstallerAssetName, StringComparison.OrdinalIgnoreCase)) installerUrl = url;
            if (name.Equals(ChecksumAssetName, StringComparison.OrdinalIgnoreCase)) checksumUrl = url;
        }
        if (string.IsNullOrWhiteSpace(installerUrl)) return null;
        string? notes = root.TryGetProperty("body", out JsonElement body) ? body.GetString() : null;
        return new UpdateInfo(version!, tag, installerUrl, checksumUrl, notes);
    }

    public async Task<string> DownloadInstallerAsync(UpdateInfo update, IProgress<int>? progress,
        CancellationToken cancellationToken = default)
    {
        string directory = Path.Combine(Path.GetTempPath(), "StudioHELPER", "updates", update.Version.ToString());
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, InstallerAssetName);
        using HttpResponseMessage response = await _httpClient.GetAsync(
            update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;
        using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using FileStream target = new(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copied += read;
            if (total > 0) progress?.Report((int)Math.Min(100, copied * 100 / total.Value));
        }
        await target.FlushAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(update.ChecksumUrl))
        {
            string checksumText = await _httpClient.GetStringAsync(update.ChecksumUrl, cancellationToken);
            string expected = checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            using FileStream downloaded = File.OpenRead(destination);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(downloaded, cancellationToken));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update checksum verification failed.");
        }
        return destination;
    }

    public static void StartInstaller(string installerPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = $"/update {Environment.ProcessId}",
            UseShellExecute = true
        });
    }

    internal static bool TryParseVersion(string tag, out Version? version) =>
        Version.TryParse(tag.Trim().TrimStart('v', 'V'), out version);
}
