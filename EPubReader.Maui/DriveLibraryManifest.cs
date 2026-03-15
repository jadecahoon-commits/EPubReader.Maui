using System.Text.Json;
using System.Text.Json.Serialization;

namespace EPubReader.Maui;

/// <summary>
/// A lightweight local snapshot of the Calibre library folder structure on Google Drive.
/// Stores Drive file IDs so books can be fetched on demand at read-time.
/// Never stores actual book content — just the tree metadata.
/// </summary>
public class DriveLibraryManifest
{
    private static readonly string ManifestPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EPubReader",
        "drive-manifest.json");

    public DateTime LastSynced { get; set; } = DateTime.MinValue;

    /// <summary>The Drive folder ID that was scanned (the Calibre library root).</summary>
    public string RootFolderId { get; set; } = "";

    public List<DriveAuthorEntry> Authors { get; set; } = new();

    // ── Persistence ───────────────────────────────────────────────────────────

    public static DriveLibraryManifest? Load()
    {
        try
        {
            if (!File.Exists(ManifestPath)) return null;
            var json = File.ReadAllText(ManifestPath);
            return JsonSerializer.Deserialize<DriveLibraryManifest>(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DriveManifest.Load: {ex.Message}");
            return null;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ManifestPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DriveManifest.Save: {ex.Message}");
        }
    }

    public static bool Exists() => File.Exists(ManifestPath);

    public static void Delete()
    {
        try { if (File.Exists(ManifestPath)) File.Delete(ManifestPath); }
        catch { /* ignore */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Flatten the manifest into BookItems for use by the main page.</summary>
    public List<BookItem> ToBookItems()
    {
        var books = new List<BookItem>();

        foreach (var author in Authors)
        {
            foreach (var book in author.Books)
            {
                foreach (var file in book.Files)
                {
                    // Build a platform-independent Calibre key from the folder structure
                    // This matches the same key that Windows LibraryScanner produces
                    var calibreKey = LibraryData.BuildCalibreKey(
                        author.Name,
                        book.FolderName ?? book.Title,  // Use FolderName if available, fall back to Title
                        file.FileName);

                    books.Add(new BookItem
                    {
                        Title = book.Title,
                        Author = author.Name,
                        FilePath = DriveFilePath(file.DriveFileId, file.Extension),
                        FileType = file.Extension.TrimStart('.'),
                        CoverImagePath = book.CoverDriveFileId is { } coverId
                            ? DriveFilePath(coverId, book.CoverExtension ?? ".jpg", book.CoverModifiedTime)
                            : null,
                        Description = book.Description,
                        SeriesIndex = book.SeriesIndex,
                        IsFinished = book.IsFinished,
                        CalibreKey = calibreKey,
                        Fandom = LibraryData.GetFandom(calibreKey),
                        Category = LibraryData.GetCategory(calibreKey)
                    });
                }
            }
        }

        return books;
    }

    /// <summary>
    /// Canonical path scheme for Drive files used as BookItem.FilePath.
    /// </summary>
    // Format: gdrive://{driveFileId}.{ext}  (e.g. gdrive://1h_WzuT245m4....epub)

    public static string DriveFilePath(string driveFileId, string extension, DateTime? modifiedTime = null)
    {
        var base_ = $"gdrive://{driveFileId}{extension}";
        if (modifiedTime.HasValue)
            base_ += $"?t={new DateTimeOffset(modifiedTime.Value).ToUnixTimeSeconds()}";
        return base_;
    }

    public static string? ParseDriveFileId(string path)
    {
        if (!path.StartsWith("gdrive://")) return null;
        var rest = path["gdrive://".Length..];
        // Strip query string
        var q = rest.IndexOf('?');
        if (q >= 0) rest = rest[..q];
        var dot = rest.LastIndexOf('.');
        return dot > 0 ? rest[..dot] : rest;
    }

    /// <summary>Returns the modifiedTime encoded in a gdrive:// path, or null.</summary>
    public static DateTime? ParseDriveModifiedTime(string path)
    {
        var q = path.IndexOf("?t=");
        if (q < 0) return null;
        if (long.TryParse(path[(q + 3)..], out var secs))
            return DateTimeOffset.FromUnixTimeSeconds(secs).UtcDateTime;
        return null;
    }
}

public class DriveAuthorEntry
{
    public string Name { get; set; } = "";
    public List<DriveBookEntry> Books { get; set; } = new();
}

public class DriveBookEntry
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public float SeriesIndex { get; set; }
    public bool IsFinished { get; set; }

    public string? CoverExtension { get; set; }

    /// <summary>
    /// The original folder name from Google Drive (e.g. "A Difference That Makes All The Differen (17)").
    /// Stored so we can reconstruct the same CalibreKey that the Windows scanner would produce.
    /// </summary>
    public string? FolderName { get; set; }

    /// <summary>Drive file ID of the cover image, or null if no cover found.</summary>
    public string? CoverDriveFileId { get; set; }

    /// <summary>Drive file ID of the .opf metadata file, or null.</summary>
    public string? OpfDriveFileId { get; set; }

    /// <summary>All book files in this folder (epub, mobi, etc.).</summary>
    public List<DriveBookFile> Files { get; set; } = new();

    //<summary>ModifiedTime of the cover file from Drive, for cache invalidation.</summary>
    public DateTime? CoverModifiedTime { get; set; }
}

public class DriveBookFile
{
    public string DriveFileId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Extension { get; set; } = "";
    
}