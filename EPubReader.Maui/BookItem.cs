namespace EPubReader.Maui;

public class BookItem
{
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";

    /// <summary>
    /// The actual file path (or content:// URI on Android, or gdrive:// URI for Drive books)
    /// used to open the file. Do NOT use this as a LibraryData key.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Portable cross-platform key used for all LibraryData lookups (fandoms, categories,
    /// reading positions). Format: "Author/BookFolder/FileName.epub" — identical on Windows
    /// and Android. Defaults to FilePath when not explicitly set (e.g. Drive books).
    /// </summary>
    public string LookupKey
    {
        get => _lookupKey ?? FilePath;
        set => _lookupKey = value;
    }
    private string? _lookupKey;

    public string FileType { get; set; } = "";
    public string Fandom { get; set; } = "";
    public string Category { get; set; } = "";
    public string? CoverImagePath { get; set; }
    public string? Description { get; set; }
    public bool HasCover => CoverImagePath != null;
    public float SeriesIndex { get; set; } = 0f;
    public bool IsFinished { get; set; } = false;
    public string AccentColor => IsFinished ? "#E50914" : "#B8860B";
}