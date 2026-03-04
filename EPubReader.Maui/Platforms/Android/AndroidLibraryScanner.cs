using Android.Content;
using Android.Provider;
using AndroidNet = Android.Net;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace EPubReader.Maui;

public class AndroidLibraryScanner : ILibraryScanner
{
    private static readonly string[] BookExtensions =
        [".epub", ".mobi", ".azw", ".azw3", ".pdf", ".doc", ".docx", ".txt"];

    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png"];

    private ContentResolver Resolver =>
        Platform.CurrentActivity?.ContentResolver
        ?? throw new InvalidOperationException("No ContentResolver available");

    public List<BookItem> ScanLibrary(string libraryPath)
    {
        // Regular file path — use standard file system APIs
        if (!libraryPath.StartsWith("content://"))
        {
            return LibraryScanner.ScanLibrary(libraryPath);
        }

        // SAF content:// URI — use ContentResolver
        var books = new List<BookItem>();

        try
        {
            var treeUri = AndroidNet.Uri.Parse(libraryPath);
            if (treeUri == null) return books;

            var rootDocId = DocumentsContract.GetTreeDocumentId(treeUri);
            if (rootDocId == null) return books;

            // Enumerate author directories (depth 1)
            var authorDirs = GetChildDocuments(treeUri, rootDocId);

            foreach (var authorDoc in authorDirs)
            {
                if (!authorDoc.IsDirectory) continue;

                try
                {
                    var author = authorDoc.DisplayName;

                    // Enumerate book directories (depth 2)
                    var bookDirs = GetChildDocuments(treeUri, authorDoc.DocumentId);

                    foreach (var bookDoc in bookDirs)
                    {
                        if (!bookDoc.IsDirectory) continue;

                        try
                        {
                            var folderTitle = bookDoc.DisplayName;

                            // Get all files in book folder
                            var files = GetChildDocuments(treeUri, bookDoc.DocumentId);

                            // Find cover image
                            string? coverUri = null;
                            try
                            {
                                var coverDoc = files.FirstOrDefault(f =>
                                    !f.IsDirectory &&
                                    ImageExtensions.Contains(
                                        Path.GetExtension(f.DisplayName).ToLowerInvariant()));
                                coverUri = coverDoc?.Uri;
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error finding cover: {ex.Message}");
                            }

                            // Read OPF metadata
                            var opfDoc = files.FirstOrDefault(f =>
                                !f.IsDirectory &&
                                f.DisplayName.EndsWith(".opf", StringComparison.OrdinalIgnoreCase));

                            string? title = folderTitle;
                            string? description = null;
                            float seriesIndex = 0f;
                            bool isFinished = false;

                            if (opfDoc != null)
                            {
                                var opfContent = ReadFileText(opfDoc.Uri);
                                if (opfContent != null)
                                {
                                    var meta = ParseOpfMetadata(opfContent);
                                    title = meta.Title ?? folderTitle;
                                    description = meta.Description;
                                    seriesIndex = meta.SeriesIndex;
                                    isFinished = meta.IsFinished;
                                }
                            }

                            // Add book entries for each book file
                            foreach (var file in files)
                            {
                                if (file.IsDirectory) continue;

                                try
                                {
                                    var ext = Path.GetExtension(file.DisplayName).ToLowerInvariant();
                                    if (BookExtensions.Contains(ext))
                                    {
                                        // Build the portable key that matches what Windows produces:
                                        // "Author/BookFolder/FileName.epub"
                                        var calibreKey = LibraryData.BuildCalibreKey(author, folderTitle, file.DisplayName);
                                            books.Add(new BookItem
                                             {
                                                 Title = title ?? folderTitle,
                                                 Author = author,
                                                 FilePath = file.Uri,
                                                 FileType = ext.TrimStart('.'),
                                                 CoverImagePath = coverUri,
                                                 Description = description,
                                                 SeriesIndex = seriesIndex,
                                                 IsFinished = isFinished,
                                                 CalibreKey = calibreKey
                                             });
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Error processing file {file.DisplayName}: {ex.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error processing book dir {bookDoc.DisplayName}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing author dir {authorDoc.DisplayName}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error scanning library: {ex}");
        }

        // Apply saved fandom and category data using the portable LookupKey
        foreach (var book in books)
     {
         try
         {
             book.Fandom = LibraryData.GetFandom(book.CalibreKey);
             book.Category = LibraryData.GetCategory(book.CalibreKey);
         }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error loading data for {book.LookupKey}: {ex.Message}");
                book.Fandom = "";
                book.Category = "";
            }
        }

        return books;
    }

    public string? ReadFileText(string path)
    {
        if (!path.StartsWith("content://"))
        {
            try { return File.ReadAllText(path); }
            catch (Exception ex) { Debug.WriteLine($"Error reading file: {ex.Message}"); return null; }
        }
        try
        {
            var uri = AndroidNet.Uri.Parse(path);
            if (uri == null) return null;

            using var stream = Resolver.OpenInputStream(uri);
            if (stream == null) return null;

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error reading file text: {ex.Message}");
            return null;
        }
    }

    public Stream? OpenFileStream(string path)
    {
        if (!path.StartsWith("content://"))
        {
            try { return File.OpenRead(path); }
            catch (Exception ex) { Debug.WriteLine($"Error opening file: {ex.Message}"); return null; }
        }
        try
        {
            var uri = AndroidNet.Uri.Parse(path);
            if (uri == null) return null;

            var stream = Resolver.OpenInputStream(uri);
            if (stream == null) return null;

            // Copy to MemoryStream since the Android input stream may not support seeking
            var memStream = new MemoryStream();
            stream.CopyTo(memStream);
            stream.Close();
            memStream.Position = 0;
            return memStream;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error opening file stream: {ex.Message}");
            return null;
        }
    }

    // ── SAF document enumeration ──────────────────────────────────────────────

    private record DocumentInfo(
        string DocumentId,
        string DisplayName,
        string Uri,
        bool IsDirectory);

    private List<DocumentInfo> GetChildDocuments(AndroidNet.Uri treeUri, string parentDocId)
    {
        var results = new List<DocumentInfo>();

        var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, parentDocId);
        if (childrenUri == null) return results;

        string[] projection =
        [
            DocumentsContract.Document.ColumnDocumentId,
            DocumentsContract.Document.ColumnDisplayName,
            DocumentsContract.Document.ColumnMimeType
        ];

        try
        {
            using var cursor = Resolver.Query(childrenUri, projection, null, null, null);
            if (cursor == null)
            {
                Debug.WriteLine($"Null cursor for {childrenUri} — provider may not support tree queries");
                return results;
            }

            while (cursor.MoveToNext())
            {
                // ... existing cursor reading code
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetChildDocuments failed for {parentDocId}: {ex.Message}");
            // Google Drive and some other providers throw here
        }

        return results;
    }

    // ── OPF parsing (same logic as LibraryScanner) ────────────────────────────

    private static (string? Title, string? Description, float SeriesIndex, bool IsFinished)
        ParseOpfMetadata(string content)
    {
        var title = ExtractOpfField(content, "dc:title");
        var description = ExtractOpfField(content, "dc:description");

        if (description != null)
        {
            description = System.Net.WebUtility.HtmlDecode(description);
            description = Regex.Replace(description, "<.*?>", "");
            if (string.IsNullOrWhiteSpace(description))
                description = null;
        }

        float seriesIndex = 0f;
        var seriesIndexStr = ExtractMetaContent(content, "calibre:series_index");
        if (seriesIndexStr != null)
            float.TryParse(seriesIndexStr, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out seriesIndex);

        bool isFinished = false;
        var finishedMeta = ExtractMetaContent(content, "calibre:user_metadata:#finished");
        if (finishedMeta != null)
        {
            var decoded = System.Net.WebUtility.HtmlDecode(finishedMeta);
            var match = Regex.Match(decoded, @"""#value#""\s*:\s*(true|false|null)");
            if (match.Success)
                isFinished = match.Groups[1].Value == "true";
        }

        return (title, description, seriesIndex, isFinished);
    }

    private static string? ExtractOpfField(string content, string tagName)
    {
        var startTag = $"<{tagName}>";
        var endTag = $"</{tagName}>";
        var start = content.IndexOf(startTag);
        if (start < 0) return null;

        start += startTag.Length;
        var end = content.IndexOf(endTag, start);
        if (end < 0) return null;

        var value = content[start..end].Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ExtractMetaContent(string content, string nameValue)
    {
        var marker = $"name=\"{nameValue}\"";
        var idx = content.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var tagStart = content.LastIndexOf('<', idx);
        if (tagStart < 0) return null;
        var tagEnd = content.IndexOf('>', idx);
        if (tagEnd < 0) return null;
        var tag = content[tagStart..tagEnd];

        var contentMarker = "content=\"";
        var ci = tag.IndexOf(contentMarker, StringComparison.OrdinalIgnoreCase);
        if (ci < 0) return null;
        ci += contentMarker.Length;
        var ce = tag.IndexOf('"', ci);
        if (ce < 0) return null;
        return tag[ci..ce];
    }
}