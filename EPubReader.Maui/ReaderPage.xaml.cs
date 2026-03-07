using System.Diagnostics;
using VersOne.Epub;
using VersOne.Epub.Options;


namespace EPubReader.Maui;

public partial class ReaderPage : ContentPage
{
    // ── State ─────────────────────────────────────────────────────────────────

    private EpubBook? _book;
    private List<EpubLocalTextContentFile> _chapters = new();
    private int _currentChapter = 0;
    private int _currentPage = 0;
    private int _totalPages = 0;
    private bool _loaded = false;
    private bool _isNavigating = false;
    private bool _goToLastPageOnLoad = false;
    private int _savedPage = -1;
    private string _calibreKey = "";
    private string _filePath = "";
    private BookItem _bookItem = null!;
    private DateTime? _sessionStart;  


    // TOC
    private List<TocEntry> _tocEntries = new();
    private bool _tocOpen = false;

    private readonly ILibraryScanner _scanner;

    // ── Constructor ───────────────────────────────────────────────────────────

    public ReaderPage(BookItem bookItem, ILibraryScanner scanner)
    {
        InitializeComponent();
        _scanner = scanner;
        //Title = bookItem.Title;
        _filePath = bookItem.FilePath;
        _calibreKey = bookItem.CalibreKey;
       _bookItem = bookItem; 
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _sessionStart = DateTime.UtcNow;   // start timing this session

        if (_loaded) return;
        _loaded = true;
        await LoadBookAsync(_filePath);
        HookKeyboard();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        UnhookKeyboard();
        FlushReadingSession();
    }

    // ── Keyboard hook (Windows) ───────────────────────────────────────────────
    // Left/Right arrow → prev/next PAGE (handled in JS inside WebView too, but
    // this catches focus when the WebView doesn't have it).
    // The top-bar chapter buttons also respond to keyboard via their own handler
    // (see PrevChapter_Click / NextChapter_Click), but we want modifier-free
    // arrow presses on the nav bar to jump chapters.  We achieve this by
    // checking focus: if a nav Button has focus, arrow = chapter; otherwise
    // arrow = page.

#if WINDOWS
    private Microsoft.UI.Xaml.Window? _winWindow;

    private void HookKeyboard()
    {
        try
        {
            if (Application.Current?.Windows is { Count: > 0 } wins)
            {
                var mauiWin = wins[0];
                var nativeWin = mauiWin.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
                if (nativeWin != null)
                {
                    _winWindow = nativeWin;
                    nativeWin.Content.KeyDown += OnWindowKeyDown;
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"HookKeyboard: {ex.Message}"); }
    }

    private void UnhookKeyboard()
    {
        try
        {
            if (_winWindow != null)
                _winWindow.Content.KeyDown -= OnWindowKeyDown;
        }
        catch { }
    }

    private void OnWindowKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        // Determine whether focus is on one of the chapter-nav buttons in the top bar.
        // If so, arrow keys jump chapters; otherwise they jump pages.
        bool chapterFocus = IsChapterButtonFocused();

        if (e.Key == Windows.System.VirtualKey.Left)
        {
            e.Handled = true;
            if (chapterFocus)
                _ = NavigatePrevChapterAsync();
            else
                _ = NavigatePrevPageAsync();
        }
        else if (e.Key == Windows.System.VirtualKey.Right)
        {
            e.Handled = true;
            if (chapterFocus)
                _ = NavigateNextChapterAsync();
            else
                _ = NavigateNextPageAsync();
        }
    }

    private bool IsChapterButtonFocused()
    {
        try
        {
            var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(
                _winWindow?.Content.XamlRoot) as Microsoft.UI.Xaml.FrameworkElement;
            if (focused == null) return false;
            // Check by automation id / tag — simplest: see if it's one of our chapter buttons
            // We tag them via AutomationProperties in XAML or just check by name mapping.
            // Fallback: check if the focused element's name contains "Chapter"
            return focused.Name is "PrevChapterButton" or "NextChapterButton";
        }
        catch { return false; }
    }
#else
    private void HookKeyboard() { }
    private void UnhookKeyboard() { }
#endif

    // ── Book loading ──────────────────────────────────────────────────────────

    private async Task LoadBookAsync(string filePath)
    {
        try
        {
            string resolvedPath = filePath;

            if (DriveLibraryManifest.ParseDriveFileId(filePath) != null)
            {
                StatusText.Text = "Downloading from Google Drive…";
                LoadingOverlay.IsVisible = true;

                var localPath = await DriveLibraryScanner.ResolveToLocalPathAsync(filePath);
                if (localPath == null)
                {
                    StatusText.Text = "Failed to download book from Google Drive.";
                    return;
                }
                resolvedPath = localPath;
            }

            string? tempFilePath = null;

            try
            {
                var stream = _scanner.OpenFileStream(resolvedPath);

                if (stream != null)
                {
                    // On Android with content:// URIs, write to a temp file first
                    // to ensure EpubReader can properly read all content
                    tempFilePath = Path.Combine(
                        FileSystem.CacheDirectory,
                        $"epub_temp_{Guid.NewGuid():N}.epub");

                    using (var fileStream = File.Create(tempFilePath))
                    {
                        await stream.CopyToAsync(fileStream);
                    }
                    stream.Dispose();

                    _book = await EpubReader.ReadBookAsync(tempFilePath);
                }
                else
                {
                    _book = await EpubReader.ReadBookAsync(resolvedPath);
                }

                _chapters = _book.ReadingOrder
                    .OfType<EpubLocalTextContentFile>()
                    .Where(c => !string.IsNullOrWhiteSpace(c.Content))
                    .ToList();

                if (_chapters.Count == 0)
                {
                    StatusText.Text = "No readable content found in this ePub.";
                    return;
                }

                BuildToc();

                var savedPos = LibraryData.GetPosition(_calibreKey);
                _currentChapter = Math.Clamp(savedPos?.Chapter ?? 0, 0, _chapters.Count - 1);
                _savedPage = savedPos?.Page ?? 0;
                LibraryData.SetLastReadBook(_bookItem);


                await ShowChapterAsync(_currentChapter, goToLastPage: false);
            }
            finally
            {
                // Clean up temp file
                if (tempFilePath != null && File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); }
                    catch { /* ignore cleanup errors */ }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadBookAsync error: {ex}");
            StatusText.Text = $"Error loading book: {ex.Message}";
        }
    }

    // ── TOC builder ───────────────────────────────────────────────────────────

    private void BuildToc()
    {
        _tocEntries = new List<TocEntry>();
        if (_book == null) return;

        try
        {
            var keyToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _chapters.Count; i++)
                keyToIndex[_chapters[i].Key] = i;

            if (_book.Navigation != null)
                WalkNavItems(_book.Navigation, keyToIndex, 0);

            if (_tocEntries.Count == 0)
            {
                for (int i = 0; i < _chapters.Count; i++)
                    _tocEntries.Add(new TocEntry
                    {
                        Title = Path.GetFileNameWithoutExtension(_chapters[i].Key),
                        ChapterIndex = i,
                        Depth = 0
                    });
            }
        }
        catch (Exception ex) { Debug.WriteLine($"BuildToc error: {ex.Message}"); }

        TocListView.ItemsSource = _tocEntries;
    }

    private void WalkNavItems(IEnumerable<EpubNavigationItem> items,
                              Dictionary<string, int> keyToIndex, int depth)
    {
        foreach (var item in items)
        {
            int chapterIndex = -1;
            if (item.Link?.ContentFilePath is string path)
            {
                var filePart = path.Contains('#') ? path[..path.IndexOf('#')] : path;
                filePart = filePart.Replace('\\', '/');
                foreach (var kv in keyToIndex)
                {
                    var key = kv.Key.Replace('\\', '/');
                    if (key.EndsWith(filePart, StringComparison.OrdinalIgnoreCase) ||
                        filePart.EndsWith(key, StringComparison.OrdinalIgnoreCase))
                    { chapterIndex = kv.Value; break; }
                }
            }

            _tocEntries.Add(new TocEntry
            {
                Title = item.Title ?? "(untitled)",
                ChapterIndex = chapterIndex,
                Depth = depth
            });

            if (item.NestedItems?.Count > 0)
                WalkNavItems(item.NestedItems, keyToIndex, depth + 1);
        }
    }

    // ── TOC panel events ──────────────────────────────────────────────────────

    private void TocButton_Click(object? sender, EventArgs e)
    {
        _tocOpen = !_tocOpen;
        TocOverlay.IsVisible = _tocOpen;
    }

    private void TocClose_Click(object? sender, EventArgs e)
    {
        _tocOpen = false;
        TocOverlay.IsVisible = false;
    }

    private void TocOverlay_Tapped(object? sender, TappedEventArgs e)
    {
        _tocOpen = false;
        TocOverlay.IsVisible = false;
    }

    private void TocListView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TocListView.SelectedItem is not TocEntry entry) return;
        TocListView.SelectedItem = null;
        if (entry.ChapterIndex < 0 || entry.ChapterIndex >= _chapters.Count) return;

        _tocOpen = false;
        TocOverlay.IsVisible = false;
        _ = ShowChapterAsync(entry.ChapterIndex);
    }

    // ── Theme ─────────────────────────────────────────────────────────────────



    public async void RefreshTheme()
    {
        if (_chapters.Count == 0) return;
        _savedPage = _currentPage;
        var chapter = _chapters[_currentChapter];
        ContentWebView.Source = new HtmlWebViewSource
        {
            Html = BuildPagedHtml(chapter.Content ?? "", LibraryData.Theme == "Dark", GetBookCss())
        };
    }

    // -- Reader settings ───────────────────────────────────────────────────────
    private async void ReaderSettings_Click(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new ReaderSettingsPage(RefreshReaderSettings));
    }
    public void RefreshReaderSettings()
     {
         // Re-render the current chapter with updated font size and text color
         if (_chapters.Count == 0) return;
         _savedPage = _currentPage;
         var chapter = _chapters[_currentChapter];
         ContentWebView.Source = new HtmlWebViewSource
         {
             Html = BuildPagedHtml(chapter.Content ?? "", LibraryData.Theme == "Dark", GetBookCss())
         };
     }

    // ── Chapter display ───────────────────────────────────────────────────────

    private async Task ShowChapterAsync(int chapterIndex, bool goToLastPage = false, int overridePage = -1)
    {
        if (_chapters.Count == 0) return;

        chapterIndex = Math.Clamp(chapterIndex, 0, _chapters.Count - 1);
        _currentChapter = chapterIndex;
        _currentPage = 0;
        _totalPages = 0;
        _goToLastPageOnLoad = goToLastPage;
        if (overridePage >= 0) _savedPage = overridePage;

        LoadingOverlay.IsVisible = true;

        var chapter = _chapters[chapterIndex];
        ContentWebView.Source = new HtmlWebViewSource
        {
            Html = BuildPagedHtml(chapter.Content ?? "", LibraryData.Theme == "Dark", GetBookCss())
        };

        // Fallback: if OnWebViewNavigated never fires, force recalculate
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (LoadingOverlay.IsVisible)
                    await RecalculatePagesAsync();
            });
        });
    }

    // ── WebView events ────────────────────────────────────────────────────────

    private async void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
        => await RecalculatePagesAsync();

    private async void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        // Intercept reader://nav/* URLs emitted by the JS inside the page
        if (!e.Url.StartsWith("reader://nav/")) return;

        e.Cancel = true;
        var cmd = e.Url["reader://nav/".Length..];

        switch (cmd)
        {
            case "next": await NavigateNextPageAsync(); break;
            case "prev": await NavigatePrevPageAsync(); break;
        }
    }

    private async Task RecalculatePagesAsync()
    {
        try
        {
            await Task.Delay(150); // let WebView paint

            var totalStr = await ContentWebView.EvaluateJavaScriptAsync("window.__getTotalPages()");
            if (int.TryParse(totalStr, out var total) && total > 0)
            {
                _totalPages = total;

                int targetPage;
                if (_savedPage >= 0)
                {
                    targetPage = Math.Min(_savedPage, _totalPages - 1);
                    _savedPage = -1;
                }
                else if (_goToLastPageOnLoad)
                {
                    targetPage = _totalPages - 1;
                    _goToLastPageOnLoad = false;
                }
                else
                {
                    targetPage = 0;
                }

                await GoToPageAsync(targetPage);
            }
        }
        catch (Exception ex) { Debug.WriteLine($"RecalculatePages error: {ex.Message}"); }
        finally { LoadingOverlay.IsVisible = false; }
    }

    private async Task GoToPageAsync(int page)
    {
        page = Math.Clamp(page, 0, Math.Max(0, _totalPages - 1));
        _currentPage = page;

        await ContentWebView.EvaluateJavaScriptAsync($"window.__goToPage({page})");
        UpdateNavUi();
        SavePosition();
    }

    // ── Page navigation ───────────────────────────────────────────────────────

    /// <summary>Navigate forward one page; auto-advance chapter at end.</summary>
    private async Task NavigateNextPageAsync()
    {
        if (_isNavigating) return;
        _isNavigating = true;
        try
        {
            if (_currentPage < _totalPages - 1)
                await GoToPageAsync(_currentPage + 1);
            else if (_currentChapter < _chapters.Count - 1)
                await ShowChapterAsync(_currentChapter + 1, goToLastPage: false);
        }
        finally { _isNavigating = false; }
    }

    /// <summary>Navigate back one page; auto-retreat chapter at beginning.</summary>
    private async Task NavigatePrevPageAsync()
    {
        if (_isNavigating) return;
        _isNavigating = true;
        try
        {
            if (_currentPage > 0)
                await GoToPageAsync(_currentPage - 1);
            else if (_currentChapter > 0)
                await ShowChapterAsync(_currentChapter - 1, goToLastPage: true);
        }
        finally { _isNavigating = false; }
    }

    // ── Chapter navigation ────────────────────────────────────────────────────

    private async Task NavigateNextChapterAsync()
    {
        if (_isNavigating) return;
        var navEntries = _tocEntries.Where(e => e.ChapterIndex >= 0).ToList();
        int navIndex = navEntries.FindIndex(e => e.ChapterIndex == _currentChapter);
        if (navIndex < 0 || navIndex >= navEntries.Count - 1) return;
        _isNavigating = true;
        try { await ShowChapterAsync(navEntries[navIndex + 1].ChapterIndex); }
        finally { _isNavigating = false; }
    }

    private async Task NavigatePrevChapterAsync()
    {
        if (_isNavigating) return;
        var navEntries = _tocEntries.Where(e => e.ChapterIndex >= 0).ToList();
        int navIndex = navEntries.FindIndex(e => e.ChapterIndex == _currentChapter);
        if (navIndex <= 0) return;
        _isNavigating = true;
        try { await ShowChapterAsync(navEntries[navIndex - 1].ChapterIndex); }
        finally { _isNavigating = false; }
    }

    // ── Button / tap-zone click handlers ─────────────────────────────────────

    private void PrevChapter_Click(object? sender, EventArgs e) => _ = NavigatePrevChapterAsync();
    private void NextChapter_Click(object? sender, EventArgs e) => _ = NavigateNextChapterAsync();

    /// <summary>Left edge tap zone (XAML BoxView) → prev page.</summary>
    private void LeftTap_Tapped(object? sender, TappedEventArgs e) => _ = NavigatePrevPageAsync();

    /// <summary>Right edge tap zone (XAML BoxView) → next page.</summary>
    private void RightTap_Tapped(object? sender, TappedEventArgs e) => _ = NavigateNextPageAsync();

    // ── UI update & persistence ───────────────────────────────────────────────

    private void UpdateNavUi()
    {
        var navEntries = _tocEntries.Where(e => e.ChapterIndex >= 0).ToList();
        int navIndex = navEntries.FindIndex(e => e.ChapterIndex == _currentChapter);
        var chapterLabel = navEntries.Count > 1
            ? $"Ch. {Math.Max(navIndex + 1, 1)}/{navEntries.Count}  ·  "
            : "";

        PageIndexText.Text = _totalPages > 0
            ? $"{chapterLabel}Page {_currentPage + 1} of {_totalPages}"
            : "";

        PrevChapterButton.IsEnabled = navIndex > 0;
        NextChapterButton.IsEnabled = navIndex < navEntries.Count - 1;

        StatusText.Text = _book?.Title ?? "";
    }
    // @\ GO_CTRL-ALT-DELETE YOUR_FACE.PNG
    private void SavePosition()
    {
        if (!string.IsNullOrEmpty(_filePath))
            LibraryData.SetPosition(_calibreKey, _currentChapter, _currentPage);
    }

    /// <summary>
    /// Commits elapsed time for this session to LibraryData.
    /// Called automatically when the page disappears.
    /// </summary>
    private void FlushReadingSession()
    {
        try
        {
            if (_sessionStart.HasValue)
            {
                var elapsed = (long)(DateTime.UtcNow - _sessionStart.Value).TotalSeconds;
                if (elapsed > 0)
                    LibraryData.RecordReadingSession(_calibreKey, elapsed);
                _sessionStart = null;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"FlushReadingSession error: {ex.Message}"); }
    }

    // ── HTML builder ──────────────────────────────────────────────────────────

    private static string BuildPagedHtml(string rawHtml, bool isDark, string? epubLinkedCss = null)
    {
        var bg = isDark ? "#0f0f0f" : "#f5f5f5";
        var fg = !string.IsNullOrEmpty(LibraryData.ReaderTextColor)
            ? LibraryData.ReaderTextColor
            : (isDark ? "#DCDCDC" : "#1a1a1a");
        var headingColor = isDark ? "#FFFFFF" : "#000000";

        var linkColor = "#E50914";
        var hrColor = "#DE3163";
        var blockquoteColor = "#DE3163";

        // Extract epub's own <style> blocks from <head> so class-based
        // formatting (e.g. .italic, .calibre2) is preserved
        var epubStyles = new System.Text.StringBuilder();
        var headStart = rawHtml.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        var headEnd = rawHtml.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headStart >= 0 && headEnd > headStart)
        {
            var headContent = rawHtml[headStart..headEnd];
            var searchFrom = 0;
            while (true)
            {
                var styleOpen = headContent.IndexOf("<style", searchFrom, StringComparison.OrdinalIgnoreCase);
                if (styleOpen < 0) break;
                var styleClose = headContent.IndexOf("</style>", styleOpen, StringComparison.OrdinalIgnoreCase);
                if (styleClose < 0) break;
                var innerStart = headContent.IndexOf('>', styleOpen) + 1;
                epubStyles.AppendLine(headContent[innerStart..styleClose]);
                searchFrom = styleClose + 8;
            }
        }

        // After the existing epubStyles extraction block, add:
        if (!string.IsNullOrWhiteSpace(epubLinkedCss))
        {
            // Strip color/background declarations so epub CSS doesn't override our theme,
            // but preserve font-style, font-weight, font-variant, text-decoration etc.
            var filtered = System.Text.RegularExpressions.Regex.Replace(
                epubLinkedCss,
                @"(?<![a-z-])(?:color|background(?:-color)?)\s*:[^;]+;",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            epubStyles.AppendLine(filtered);
        }

        // Extract body content
        var body = rawHtml;
        var bodyStart = rawHtml.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        var bodyEnd = rawHtml.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyStart >= 0 && bodyEnd > bodyStart)
        {
            var innerStart = rawHtml.IndexOf('>', bodyStart) + 1;
            body = rawHtml[innerStart..bodyEnd];
        }

        return $@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"" />
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no"" />
<meta name=""color-scheme"" content=""only light"" />

<style>
/* ── Epub's own styles (class-based formatting) ── */
{epubStyles}
</style>

<style>
:root {{ color-scheme: only light; }}

* {{
  box-sizing: border-box; margin: 0; padding: 0;
  forced-color-adjust: none;
  font-style: inherit;
  font-weight: inherit;
}}

html, body {{
  width: 100%;
  height: 100%;
  overflow: hidden;
  background-color: {bg};
  color: {fg};
}}

#pager {{
  column-width: 100vw;
  column-gap: 0;
  column-fill: auto;
  height: 100vh;
  width: max-content;
  background-color: {bg};
  transition: transform 0.3s cubic-bezier(0.4, 0, 0.2, 1);
}}

#content {{
  width: 100vw;
  padding: 32px 20px 40px;
  font-family: '{LibraryData.ReaderFont}', Georgia, serif;
  font-size: {LibraryData.ReaderFontSize}px;
  line-height: 1.75;
  color: {fg};
  background-color: {bg};
}}

@media (min-width: 600px) {{
  #content {{ padding: 40px 60px 48px; }}
}}

@media (min-width: 900px) {{
  #content {{ padding: 48px 120px 56px; }}
}}

@keyframes fadeIn {{ from {{ opacity: 0; }} to {{ opacity: 1; }} }}
#pager {{ animation: fadeIn 0.2s ease; }}

h1, h2, h3, h4, h5, h6 {{
  color: {headingColor};
  margin: 1em 0 0.5em;
  font-family: Georgia, serif;
}}
p {{ margin-bottom: 0.9em; }}
a {{ color: {linkColor}; text-decoration: none; }}
hr {{ border: none; border-top: 1px solid {hrColor}; margin: 1.5em 0; }}
blockquote {{
  border-left: 3px solid {hrColor};
  padding-left: 1em;
  color: {blockquoteColor};
  font-style: italic;
  margin: 1em 0;
}}
img {{ max-width: 100%; height: auto; }}

/* Override epub color/background without touching font styling */
.calibre, .calibre1, .calibre2, .calibre3,
div, p, section, article {{
  color: {fg};
  background: transparent;
}}

/* ── Explicitly preserve inline formatting ── */
em, i, cite {{ font-style: italic; }}
strong, b {{ font-weight: bold; }}
u {{ text-decoration: underline; }}
s, del, strike {{ text-decoration: line-through; }}
sup {{ vertical-align: super; font-size: 0.75em; }}
sub {{ vertical-align: sub; font-size: 0.75em; }}

/* Epub often uses span with inline style or class for italics/bold — don't nuke them */
span {{ color: inherit; background: transparent; }}

</style>
</head>
<body>
<div id=""pager""><div id=""content"">{body}</div></div>

<script>
(function() {{
  var pager      = document.getElementById('pager');
  var pageWidth  = 0;
  var totalPages = 0;
  var curPage    = 0;

  function computeLayout() {{
    pageWidth  = window.innerWidth;
    totalPages = Math.max(1, Math.round(pager.scrollWidth / pageWidth));
  }}

  window.__getTotalPages = function() {{
    computeLayout();
    return totalPages;
  }};

  window.__goToPage = function(page) {{
    computeLayout();
    curPage = Math.max(0, Math.min(page, totalPages - 1));
    pager.style.transform = 'translateX(' + (-curPage * pageWidth) + 'px)';
    return curPage;
  }};

  function emitNav(cmd) {{
    var iframe = document.createElement('iframe');
    iframe.style.display = 'none';
    iframe.src = 'reader://nav/' + cmd;
    document.body.appendChild(iframe);
    setTimeout(function() {{ document.body.removeChild(iframe); }}, 200);
    try {{ window.location.href = 'reader://nav/' + cmd; }} catch(ex) {{}}
  }}

  document.addEventListener('keydown', function(e) {{
    if (e.key === 'ArrowRight') {{ e.preventDefault(); emitNav('next'); }}
    else if (e.key === 'ArrowLeft') {{ e.preventDefault(); emitNav('prev'); }}
  }});

  var tapStartX = -1;
  document.addEventListener('pointerdown', function(e) {{ tapStartX = e.clientX; }});
  document.addEventListener('pointerup', function(e) {{
    if (tapStartX < 0) return;
    var dx = Math.abs(e.clientX - tapStartX);
    tapStartX = -1;
    if (dx > 20) return;
    var target = e.target;
    while (target && target !== document.body) {{
      if (target.tagName === 'A') return;
      target = target.parentElement;
    }}
    var x = e.clientX, width = window.innerWidth;
    if (x < width * 0.15) emitNav('prev');
    else if (x > width * 0.85) emitNav('next');
  }});

  var touchStartX = -1;
  document.addEventListener('touchstart', function(e) {{
    touchStartX = e.touches[0].clientX;
  }}, {{ passive: true }});
  document.addEventListener('touchend', function(e) {{
    if (touchStartX < 0) return;
    var dx = Math.abs(e.changedTouches[0].clientX - touchStartX);
    touchStartX = -1;
    if (dx > 20) return;
    var x = e.changedTouches[0].clientX, width = window.innerWidth;
    if (x < width * 0.15) emitNav('prev');
    else if (x > width * 0.85) emitNav('next');
  }});

  window.addEventListener('resize', function() {{
    computeLayout();
    window.__goToPage(curPage);
  }});
}})();
</script>
</body>
</html>";
    }


    private string GetBookCss()
    {
        if (_book == null) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var cssFile in _book.Content.Css.Local)
        {
            if (!string.IsNullOrWhiteSpace(cssFile.Content))
                sb.AppendLine(cssFile.Content);
        }
        return sb.ToString();
    }
}