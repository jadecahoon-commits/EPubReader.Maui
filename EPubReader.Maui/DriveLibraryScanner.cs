using System.Diagnostics;

namespace EPubReader.Maui;

/// <summary>
/// ILibraryScanner implementation that reads from the local DriveLibraryManifest
/// (a lightweight JSON index of the Drive folder tree) and downloads actual book
/// files from Google Drive on demand at read-time.
///
/// FilePaths use the gdrive://{fileId} scheme so the rest of the app can treat
/// them like any other path — only this class knows how to resolve them.
/// </summary>
public class DriveLibraryScanner : ILibraryScanner
{
    // ── Local cache for downloaded books ──────────────────────────────────────

    /// <summary>
    /// Folder where downloaded book files are cached locally.
    /// Books are cached by Drive file ID so we only download once.
    /// </summary>
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EPubReader",
        "DriveCache");

    // ── ILibraryScanner ───────────────────────────────────────────────────────

    public List<BookItem> ScanLibrary(string libraryPath)
    {
        // libraryPath is ignored for Drive — we always use the local manifest.
        var manifest = DriveLibraryManifest.Load();
        if (manifest == null)
        {
            Debug.WriteLine("DriveLibraryScanner: no manifest found — sync first");
            return new List<BookItem>();
        }

        return manifest.ToBookItems();
    }

    public string? ReadFileText(string path)
    {
        var localPath = ResolveToLocalPath(path);
        if (localPath == null) return null;

        try { return File.ReadAllText(localPath); }
        catch (Exception ex)
        {
            Debug.WriteLine($"DriveLibraryScanner.ReadFileText: {ex.Message}");
            return null;
        }
    }

    public Stream? OpenFileStream(string path)
    {
        var localPath = ResolveToLocalPath(path);
        if (localPath == null) return null;

        try { return File.OpenRead(localPath); }
        catch (Exception ex)
        {
            Debug.WriteLine($"DriveLibraryScanner.OpenFileStream: {ex.Message}");
            return null;
        }
    }

    // ── Drive file resolution ─────────────────────────────────────────────────

    /// <summary>
    /// Resolves a gdrive:// path to a local file path, downloading from Drive
    /// if the file is not already cached.  Returns null on failure.
    /// </summary>
    public static async Task<string?> ResolveToLocalPathAsync(string path)
    {
        var fileId = DriveLibraryManifest.ParseDriveFileId(path);
        if (fileId == null) return path;

        var localPath = LocalCachePath(path);

        if (File.Exists(localPath))
        {
            // Check if Drive has a newer version
            var driveModified = DriveLibraryManifest.ParseDriveModifiedTime(path);
            if (driveModified.HasValue)
            {
                var localModified = File.GetLastWriteTimeUtc(localPath);
                if (localModified >= driveModified.Value)
                {
                    Debug.WriteLine($"DriveLibraryScanner: cache hit (current) for {fileId}");
                    return localPath;
                }
                Debug.WriteLine($"DriveLibraryScanner: cache stale for {fileId}, re-downloading");
            }
            else
            {
                // No timestamp in path — legacy cache hit, trust it
                Debug.WriteLine($"DriveLibraryScanner: cache hit (no timestamp) for {fileId}");
                return localPath;
            }
        }

        Debug.WriteLine($"DriveLibraryScanner: downloading {fileId}…");
        var ok = await GoogleAuthService.Instance.DownloadFileByIdAsync(fileId, localPath);
        if (ok)
            File.SetLastWriteTimeUtc(localPath, DateTime.UtcNow); // mark when we downloaded it
        return ok ? localPath : null;
    }

    private static string? ResolveToLocalPath(string path)
    {
        var fileId = DriveLibraryManifest.ParseDriveFileId(path);
        if (fileId == null) return path;

        var localPath = LocalCachePath(path); // pass full path, not just fileId
        return File.Exists(localPath) ? localPath : null;
    }

    private static string LocalCachePath(string path)
{
    // Extract the raw fileId (without extension) for the Drive API call,
    // but keep the extension for the local filename so EpubReader recognises it.
    var afterScheme = path.StartsWith("gdrive://") ? path["gdrive://".Length..] : path;
    var safe = string.Concat(afterScheme.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    Directory.CreateDirectory(CacheDir);
    return Path.Combine(CacheDir, safe);
}

    // ── Cache management ──────────────────────────────────────────────────────

    /// <summary>Deletes all locally cached Drive book files.</summary>
    public static void ClearCache()
    {
        try
        {
            if (Directory.Exists(CacheDir))
                Directory.Delete(CacheDir, recursive: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DriveLibraryScanner.ClearCache: {ex.Message}");
        }
    }

    /// <summary>Returns the total size in bytes of all cached Drive files.</summary>
    public static long CacheSizeBytes()
    {
        try
        {
            if (!Directory.Exists(CacheDir)) return 0;
            return Directory.GetFiles(CacheDir)
                .Sum(f => new FileInfo(f).Length);
        }
        catch { return 0; }
    }
}
