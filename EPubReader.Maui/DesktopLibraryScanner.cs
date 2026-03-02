using System.Diagnostics;

namespace EPubReader.Maui;

public class DesktopLibraryScanner : ILibraryScanner
{
    public List<BookItem> ScanLibrary(string libraryPath)
    {
        return LibraryScanner.ScanLibrary(libraryPath);
    }

    public string? ReadFileText(string path)
    {
        try { return File.ReadAllText(path); }
        catch (Exception ex) { Debug.WriteLine($"Error reading file: {ex.Message}"); return null; }
    }

    public Stream? OpenFileStream(string path)
    {
        try { return File.OpenRead(path); }
        catch (Exception ex) { Debug.WriteLine($"Error opening file: {ex.Message}"); return null; }
    }
}
