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
                    // Build the same portable key used by LibraryData:
                    // "Author/BookFolder/FileName.epub"
                    var lookupKey = $"{author.Name}/{book.FolderName}/{file.FileName}";

                    books.Add(new BookItem
                    {
                        Title = book.Title,
                        Author = author.Name,
                        FilePath = DriveFilePath(file.DriveFileId),
                        LookupKey = lookupKey,
                        FileType = file.Extension.TrimStart('.'),
                        CoverImagePath = book.CoverDriveFileId is { } coverId
                            ? DriveFilePath(coverId)
                            : null,
                        Description = book.Description,
                        SeriesIndex = book.SeriesIndex,
                        IsFinished = book.IsFinished,
                        Fandom = LibraryData.GetFandom(lookupKey),
                        Category = LibraryData.GetCategory(lookupKey)
                    });
                }
            }
        }

        return books;
    }

    /// <summary>
    /// Canonical path scheme for Drive files used as BookItem.FilePath.
    /// Format: gdrive://{driveFileId}
    /// </summary>
    public static string DriveFilePath(string driveFileId) => $"gdrive://{driveFileId}";

    /// <summary>Extracts the Drive file ID from a gdrive:// path, or null if not a Drive path.</summary>
    public static string? ParseDriveFileId(string path)
        => path.StartsWith("gdrive://") ? path["gdrive://".Length..] : null;
}

public class DriveAuthorEntry
{
    public string Name { get; set; } = "";
    public List<DriveBookEntry> Books { get; set; } = new();
}

public class DriveBookEntry
{
    /// <summary>
    /// The OPF-parsed display title (may differ from the folder name).
    /// Used for display only — do NOT use as a LibraryData key.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// The exact Calibre folder name (e.g. "A Thin Flame (7)").
    /// This is the stable identifier used to build the portable LibraryData lookup key.
    /// </summary>
    public string FolderName { get; set; } = "";

    public string? Description { get; set; }
    public float SeriesIndex { get; set; }
    public bool IsFinished { get; set; }

    /// <summary>Drive file ID of the cover image, or null if no cover found.</summary>
    public string? CoverDriveFileId { get; set; }

    /// <summary>Drive file ID of the .opf metadata file, or null.</summary>
    public string? OpfDriveFileId { get; set; }

    /// <summary>All book files in this folder (epub, mobi, etc.).</summary>
    public List<DriveBookFile> Files { get; set; } = new();
}

public class DriveBookFile
{
    public string DriveFileId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Extension { get; set; } = "";
}