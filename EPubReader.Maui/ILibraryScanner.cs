namespace EPubReader.Maui;

public interface ILibraryScanner
{
    List<BookItem> ScanLibrary(string libraryPath);

    /// <summary>
    /// Read all text from a file. On Android with SAF URIs this uses ContentResolver.
    /// On Windows/desktop this uses File.ReadAllText.
    /// </summary>
    string? ReadFileText(string path);

    /// <summary>
    /// Open a readable stream for a file. On Android with SAF URIs this uses ContentResolver.
    /// </summary>
    Stream? OpenFileStream(string path);

    /// <summary>
    /// On Android: resolve a content:// URI for a book directly from its CalibreKey
    /// without requiring a full library scan. Returns null on non-Android or on failure.
    /// </summary>
    string? ResolveFileUriFromCalibreKey(string calibreKey) => null;
}

