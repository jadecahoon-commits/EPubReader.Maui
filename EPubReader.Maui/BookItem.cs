namespace EPubReader.Maui;

public class BookItem
{
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string FileType { get; set; } = "";
    public string Fandom { get; set; } = "";
    public string Category { get; set; } = "";
    public string? CoverImagePath { get; set; }
    public string? Description { get; set; }
    public bool HasCover => CoverImagePath != null;
    public float SeriesIndex { get; set; } = 0f;
    public bool IsFinished { get; set; } = false;
    public string AccentColor => IsFinished ? "#E50914" : "#B8860B";

    /// <summary>
    /// Platform-independent key derived from the Calibre folder structure:
    /// "AuthorName/BookFolder/filename.ext"
    /// Used as the dictionary key in library-data.json so fandom/category
    /// assignments sync correctly across Windows, Android, and Google Drive.
    /// </summary>
    public string CalibreKey { get; set; } = "";
}