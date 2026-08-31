using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;

namespace PlogDock;

/// <summary>
/// Resolves a plugin's icon, fetches it once and keeps it on disk.
/// <para>
/// Never blocks the draw thread: until an icon is on disk and decoded, TryGet
/// returns null and the caller falls back to an initials tile. The bar is therefore
/// usable on its first frame and fills in behind the user.
/// </para>
/// </summary>
internal sealed class IconService : IDisposable
{
    /// <summary>
    /// Icons for plugins from the official repository are not in the local manifest.
    /// They live in the distribution repository at a path built from the channel the
    /// plugin was installed from.
    /// </summary>
    private const string Dip17IconUrlFormat =
        "https://raw.githubusercontent.com/goatcorp/PluginDistD17/main/{0}/{1}/images/icon.png";

    private readonly HttpClient http;
    private readonly SemaphoreSlim gate = new(4, 4);
    private readonly ConcurrentDictionary<string, byte> attempted = new(StringComparer.Ordinal);

    /// <summary>Icons confirmed on disk, mapped to their path. Every one of these was
    /// hit by exactly one File.Exists, ever.</summary>
    private readonly ConcurrentDictionary<string, string> ready = new(StringComparer.Ordinal);

    /// <summary>Plugins with no icon to be had: no url to try, or a download that
    /// failed. Recorded so the draw thread stops touching the filesystem for them.</summary>
    private readonly ConcurrentDictionary<string, byte> hopeless = new(StringComparer.Ordinal);

    private readonly string cacheDirectory;
    private readonly string? ownIconPath;

    public IconService()
    {
        this.http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        this.http.DefaultRequestHeaders.UserAgent.ParseAdd("PlogDock/0.1");

        this.cacheDirectory = Path.Combine(
            Service.PluginInterface.GetPluginConfigDirectory(),
            "icons");

        Directory.CreateDirectory(this.cacheDirectory);

        var directory = Service.PluginInterface.AssemblyLocation.Directory;
        this.ownIconPath = directory is null
            ? null
            : Path.Combine(directory.FullName, "images", "icon.png");
    }

    /// <summary>The icon shipped with PlogDock itself, for its own toggle button.</summary>
    public IDalamudTextureWrap? TryGetOwnIcon()
        => this.ownIconPath is null
            ? null
            : Service.Textures.GetFromFileAbsolute(this.ownIconPath).GetWrapOrDefault();

    /// <summary>
    /// The plugin's icon, or null while it is missing, pending or unavailable.
    /// <para>
    /// Called once per shortcut per frame, so it must not touch the filesystem on the
    /// steady path: a hundred shortcuts at sixty frames a second would otherwise mean
    /// thousands of File.Exists calls a second, all returning the same answer. Each
    /// plugin costs exactly one such call over the life of the session, and lands in
    /// either the ready set or the hopeless one.
    /// </para>
    /// </summary>
    public IDalamudTextureWrap? TryGet(CatalogEntry entry)
    {
        if (this.hopeless.ContainsKey(entry.InternalName))
            return null;

        if (this.ready.TryGetValue(entry.InternalName, out var known))
            return Service.Textures.GetFromFileAbsolute(known).GetWrapOrDefault();

        var path = this.CachePath(entry.InternalName);

        if (File.Exists(path))
        {
            this.ready[entry.InternalName] = path;
            return Service.Textures.GetFromFileAbsolute(path).GetWrapOrDefault();
        }

        // One attempt per plugin per session. A plugin whose icon 404s must not
        // re-request it on every frame.
        if (this.attempted.TryAdd(entry.InternalName, 0))
            _ = Task.Run(() => this.DownloadAsync(entry, path));

        return null;
    }

    public void Dispose()
    {
        this.http.Dispose();
        this.gate.Dispose();
    }

    private static string? ResolveUrl(CatalogEntry entry)
    {
        var manifest = entry.Source?.Manifest;
        if (manifest is null)
            return null;

        if (!string.IsNullOrWhiteSpace(manifest.IconUrl))
            return manifest.IconUrl;

        var channel = manifest.Dip17Channel;

        return string.IsNullOrWhiteSpace(channel)
            ? null
            : string.Format(Dip17IconUrlFormat, channel, entry.InternalName);
    }

    private string CachePath(string internalName)
    {
        var safe = string.Join("_", internalName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(this.cacheDirectory, $"{safe}.png");
    }

    private async Task DownloadAsync(CatalogEntry entry, string path)
    {
        var url = ResolveUrl(entry);
        if (url is null)
        {
            this.hopeless[entry.InternalName] = 0;
            Service.Log.Debug("PlogDock: no icon url for {Plugin}", entry.InternalName);
            return;
        }

        await this.gate.WaitAsync().ConfigureAwait(false);

        try
        {
            var bytes = await this.http.GetByteArrayAsync(url).ConfigureAwait(false);

            // Written aside then moved: an interrupted download would otherwise leave
            // a truncated png in the cache that every later session would reload
            // without ever repairing it.
            var staging = path + ".part";
            await File.WriteAllBytesAsync(staging, bytes).ConfigureAwait(false);
            File.Move(staging, path, overwrite: true);

            Service.Log.Debug("PlogDock: fetched icon for {Plugin}", entry.InternalName);
        }
        catch (Exception ex)
        {
            // Silent by design: the initials tile stays, and the bar keeps working
            // with no network at all.
            this.hopeless[entry.InternalName] = 0;

            Service.Log.Debug(
                "PlogDock: no icon for {Plugin} ({Url}): {Reason}",
                entry.InternalName,
                url,
                ex.Message);
        }
        finally
        {
            this.gate.Release();
        }
    }
}
