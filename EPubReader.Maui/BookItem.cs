using System.ComponentModel;

namespace EPubReader.Maui;

public class BookItem : INotifyPropertyChanged
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
    public string CalibreKey { get; set; } = "";

    // ── Async cover source ────────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private ImageSource? _coverSource;
    private bool _coverSourceLoaded = false;

    /// <summary>
    /// Async-loaded ImageSource for the cover image. Safe to bind to directly —
    /// returns null until the image is ready, then fires PropertyChanged.
    /// Never blocks the UI thread.
    /// </summary>
    public ImageSource? CoverSource
    {
        get
        {
            if (!_coverSourceLoaded)
            {
                _coverSourceLoaded = true; // prevent re-entry
                _ = LoadCoverSourceAsync();
            }
            return _coverSource;
        }
        private set
        {
            _coverSource = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CoverSource)));
        }
    }

    private async Task LoadCoverSourceAsync()
    {
        if (string.IsNullOrEmpty(CoverImagePath))
            return;

        try
        {
            ImageSource? source = null;

            if (CoverImagePath.StartsWith("gdrive://"))
            {
                var localPath = await DriveLibraryScanner.ResolveToLocalPathAsync(CoverImagePath);
                if (localPath != null)
                {
                    var bytes = await File.ReadAllBytesAsync(localPath);
                    source = ImageSource.FromStream(() => new MemoryStream(bytes));
                }
            }
#if ANDROID
            else if (CoverImagePath.StartsWith("content://"))
            {
                // Copy SAF stream to memory on a background thread
                source = await Task.Run(() =>
                {
                    try
                    {
                        var resolver = Android.App.Application.Context.ContentResolver;
                        if (resolver == null) return null;
                        var uri = Android.Net.Uri.Parse(CoverImagePath);
                        if (uri == null) return null;
                        using var input = resolver.OpenInputStream(uri);
                        if (input == null) return null;
                        var mem = new MemoryStream();
                        input.CopyTo(mem);
                        mem.Position = 0;
                        var bytes = mem.ToArray();
                        return ImageSource.FromStream(() => new MemoryStream(bytes));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"BookItem SAF cover error: {ex.Message}");
                        return null;
                    }
                });
            }
#endif
            else
            {
                source = ImageSource.FromFile(CoverImagePath);
            }

            // Marshal back to UI thread for the property change notification
            await MainThread.InvokeOnMainThreadAsync(() => CoverSource = source);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BookItem.LoadCoverSourceAsync error: {ex.Message}");
        }
    }
}