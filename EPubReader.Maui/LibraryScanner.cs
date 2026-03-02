using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EPubReader.Maui;

public static class LibraryScanner
{
    private static readonly string[] BookExtensions =
        [".epub", ".mobi", ".azw", ".azw3", ".pdf", ".doc", ".docx", ".txt"];

    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png"];

    public static List<BookItem> ScanLibrary(string libraryPath)
    {
        var books = new List<BookItem>();

        if (!Directory.Exists(libraryPath))
        {
            Debug.WriteLine($"Library path does not exist: {libraryPath}");
            return books;
        }

        string[] authorDirs;
        try
        {
            authorDirs = Directory.GetDirectories(libraryPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error reading library directory: {ex}");
            return books;
        }

        foreach (var authorDir in authorDirs)
        {
            try
            {
                var author = Path.GetFileName(authorDir);

                foreach (var bookDir in Directory.GetDirectories(authorDir))
                {
                    try
                    {
                        var folderTitle = Path.GetFileName(bookDir);

                        // Find cover image in the book folder
                        string? coverImage = null;
                        try
                        {
                            coverImage = Directory.GetFiles(bookDir)
                                .FirstOrDefault(f => ImageExtensions.Contains(
                                    Path.GetExtension(f).ToLowerInvariant()));
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error scanning for cover in {bookDir}: {ex.Message}");
                        }

                        // Read metadata from OPF file
                        var opfMetadata = ReadOpfMetadata(bookDir);
                        var title = opfMetadata.Title ?? folderTitle;
                        var description = opfMetadata.Description;
                        var seriesIndex = opfMetadata.SeriesIndex;
                        var isFinished = opfMetadata.IsFinished;


                        foreach (var file in Directory.GetFiles(bookDir))
                        {
                            try
                            {
                                var ext = Path.GetExtension(file).ToLowerInvariant();
                                if (BookExtensions.Contains(ext))
                                {
                                    books.Add(new BookItem
                                    {
                                        Title = title,
                                        Author = author,
                                        FilePath = file,
                                        FileType = ext.TrimStart('.'),
                                        CoverImagePath = coverImage,
                                        Description = description,
                                        SeriesIndex = seriesIndex,
                                        IsFinished = isFinished
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error processing file {file}: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing book directory {bookDir}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing author directory {authorDir}: {ex.Message}");
            }
        }

        // Apply saved fandom data
        foreach (var book in books)
        {
            try
            {
                book.Fandom = LibraryData.GetFandom(book.FilePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading fandom for {book.FilePath}: {ex.Message}");
                book.Fandom = "";
            }
        }

        return books;
    }

    private static (string? Title, string? Description, float SeriesIndex, bool IsFinished) ReadOpfMetadata(string bookDir)
    {
        try
        {
            var opfFile = Directory.GetFiles(bookDir, "*.opf").FirstOrDefault();
            if (opfFile == null) return (null, null, 0f, false);

            var content = File.ReadAllText(opfFile);

            var title = ExtractOpfField(content, "dc:title");
            var description = ExtractOpfField(content, "dc:description");

            if (description != null)
            {
                // Decode HTML entities first (so encoded tags like &lt;p&gt; become <p>)
                description = System.Net.WebUtility.HtmlDecode(description);
                // Then strip any HTML tags
                description = System.Text.RegularExpressions.Regex.Replace(description, "<.*?>", "");
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
                // The JSON is HTML-entity-encoded in the attribute, so decode it first
                var decoded = System.Net.WebUtility.HtmlDecode(finishedMeta);
                var match = System.Text.RegularExpressions.Regex.Match(
                    decoded, @"""#value#""\s*:\s*(true|false|null)");
                if (match.Success)
                    isFinished = match.Groups[1].Value == "true";
            }

            return (title, description, seriesIndex, isFinished);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error reading OPF from {bookDir}: {ex.Message}");
            return (null, null, 0f, false);
        }
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

        // Look for content="..." on the same <meta ...> tag (search backwards to tag start)
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
