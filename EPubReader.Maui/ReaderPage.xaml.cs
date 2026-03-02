using System.Diagnostics;
using VersOne.Epub;

namespace EPubReader.Maui;

public partial class ReaderPage : ContentPage
{
    private EpubBook? _book;
    private List<EpubLocalTextContentFile> _chapters = new();
    private int _currentChapter = 0;
    private int _currentPage = 0;
    private int _totalPages = 0;
    private bool _loaded = false;
    private bool _isNavigating = false;
    private bool _goToLastPageOnLoad = false;
    private int _savedPage = -1;

    private string _filePath = "";

    // TOC
    private List<TocEntry> _tocEntries = new();
    private bool _tocOpen = false;

    private readonly ILibraryScanner _scanner;

    public ReaderPage(BookItem bookItem, ILibraryScanner scanner)
    {
        InitializeComponent();
        _scanner = scanner;
        Title = bookItem.Title;
        BookTitleText.Text = bookItem.Title;
        BookAuthorText.Text = bookItem.Author;
        _filePath = bookItem.FilePath;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        await LoadBookAsync(_filePath);
    }

    private async Task LoadBookAsync(string filePath)
    {
        try
        {
            var stream = _scanner.OpenFileStream(filePath);
            if (stream != null)
                _book = await EpubReader.ReadBookAsync(stream);
            else
                _book = await EpubReader.ReadBookAsync(filePath);

            _chapters = _book.ReadingOrder
                .OfType<EpubLocalTextContentFile>()
                .Where(c => !string.IsNullOrWhiteSpace(c.Content))
                .ToList();

            if (_chapters.Count == 0)
            {
                StatusText.Text = "No readable content found in this ePub.";
                LoadingOverlay.IsVisible = false;
                return;
            }

            BuildToc();

            // Set up WebView message handling
            ContentWebView.Navigated += OnWebViewNavigated;
            ContentWebView.Navigating += OnWebViewNavigating;

            var saved = LibraryData.GetPosition(filePath);
            if (saved != null && saved.Chapter < _chapters.Count)
            {
                _currentChapter = saved.Chapter;
                _savedPage = saved.Page;
                await ShowChapterAsync(saved.Chapter, goToLastPage: false, overridePage: saved.Page);
            }
            else
            {
                await ShowChapterAsync(0);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading epub: {ex}");
            StatusText.Text = $"Failed to open book: {ex.Message}";
            LoadingOverlay.IsVisible = false;
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
            {
                WalkNavItems(_book.Navigation, keyToIndex, 0);
            }

            if (_tocEntries.Count == 0)
            {
                for (int i = 0; i < _chapters.Count; i++)
                {
                    var title = Path.GetFileNameWithoutExtension(_chapters[i].Key);
                    _tocEntries.Add(new TocEntry { Title = title, ChapterIndex = i, Depth = 0 });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"BuildToc error: {ex.Message}");
        }

        TocListView.ItemsSource = _tocEntries;
    }

    private void WalkNavItems(
        IEnumerable<EpubNavigationItem> items,
        Dictionary<string, int> keyToIndex,
        int depth)
    {
        foreach (var item in items)
        {
            var title = item.Title ?? "(untitled)";
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
                    {
                        chapterIndex = kv.Value;
                        break;
                    }
                }
            }

            _tocEntries.Add(new TocEntry
            {
                Title = title,
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

        var chapter = _chapters[chapterIndex];
        var html = BuildPagedHtml(chapter.Content ?? "", LibraryData.Theme == "Dark");

        ContentWebView.Source = new HtmlWebViewSource { Html = html };
    }

    private async void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result != WebNavigationResult.Success) return;
        await RecalculatePagesAsync();
    }

    private async void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (e.Url.StartsWith("reader://nav/"))
        {
            e.Cancel = true;
            var command = e.Url.Replace("reader://nav/", "");
            if (command == "next")
                await NavigateNextAsync();
            else if (command == "prev")
                await NavigatePrevAsync();
        }
    }

    private async Task RecalculatePagesAsync()
    {
        try
        {
            // Small delay to let the WebView finish rendering
            await Task.Delay(200);

            var totalStr = await ContentWebView.EvaluateJavaScriptAsync("getTotalPages()");
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

            LoadingOverlay.IsVisible = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RecalculatePages error: {ex.Message}");
            LoadingOverlay.IsVisible = false;
        }
    }

    private async Task GoToPageAsync(int page)
    {
        page = Math.Clamp(page, 0, Math.Max(0, _totalPages - 1));
        _currentPage = page;

        await ContentWebView.EvaluateJavaScriptAsync($"goToPage({page})");
        UpdateNavigation();
        SavePosition();
    }

    private void SavePosition()
    {
        if (!string.IsNullOrEmpty(_filePath))
            LibraryData.SetPosition(_filePath, _currentChapter, _currentPage);
    }

    private void UpdateNavigation()
    {
        var chapterLabel = _chapters.Count > 1
            ? $"Ch. {_currentChapter + 1}/{_chapters.Count}  ·  "
            : "";

        PageIndexText.Text = _totalPages > 0
            ? $"{chapterLabel}Page {_currentPage + 1} of {_totalPages}"
            : "";

        PrevPageButton.IsEnabled = _currentPage > 0 || _currentChapter > 0;
        NextPageButton.IsEnabled = _currentPage < _totalPages - 1 || _currentChapter < _chapters.Count - 1;

        StatusText.Text = _chapters.Count > 0 ? _chapters[_currentChapter].Key : "";
    }

    // ── HTML builder ──────────────────────────────────────────────────────────

    private static string BuildPagedHtml(string rawHtml, bool isDark)
    {
        var bg = isDark ? "#0f0f0f" : "#f5f5f5";
        var fg = isDark ? "#DCDCDC" : "#1a1a1a";
        var headingColor = isDark ? "#FFFFFF" : "#000000";
        var linkColor = "#E50914";
        var hrColor = isDark ? "#2A2A2A" : "#DDDDDD";
        var blockquoteColor = isDark ? "#AAAAAA" : "#555555";

        var body = rawHtml;
        var bodyStart = rawHtml.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        var bodyEnd = rawHtml.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyStart >= 0 && bodyEnd > bodyStart)
        {
            var innerStart = rawHtml.IndexOf('>', bodyStart) + 1;
            body = rawHtml[innerStart..bodyEnd];
        }

        // MAUI WebView doesn't support window.chrome.webview.postMessage
        // Instead we use URL scheme interception for navigation
        return $@"
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset=""utf-8"" />
            <meta name=""viewport"" content=""width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no"" />
            <style>
                * {{ box-sizing: border-box; margin: 0; padding: 0; }}

                html, body {{
                    width: 100%;
                    height: 100%;
                    overflow: hidden;
                    background: {bg};
                }}

                #pager {{
                    column-width: 100vw;
                    column-gap: 0;
                    column-fill: auto;
                    height: 100vh;
                    width: max-content;
                    background: {bg};
                    transition: transform 0.35s cubic-bezier(0.4, 0, 0.2, 1);
                    animation: fadeIn 0.25s ease;
                }}

                #content {{
                    width: 100vw;
                    padding: 48px 120px 56px;
                    font-family: Georgia, 'Times New Roman', serif;
                    font-size: 18px;
                    line-height: 1.85;
                    color: {fg};
                }}

                @keyframes fadeIn {{
                    from {{ opacity: 0; }}
                    to   {{ opacity: 1; }}
                }}

                h1, h2, h3, h4, h5, h6 {{
                    font-family: 'Segoe UI', sans-serif;
                    color: {headingColor};
                    margin: 1.5em 0 0.5em;
                    line-height: 1.3;
                    break-after: avoid;
                }}
                h1 {{ font-size: 1.8em; }}
                h2 {{ font-size: 1.4em; }}

                p {{
                    margin: 0 0 0.85em 0;
                    orphans: 3;
                    widows: 3;
                }}

                em, i {{ color: {fg}; }}
                a {{ color: {linkColor}; text-decoration: none; }}
                hr {{ border: none; border-top: 1px solid {hrColor}; margin: 2em 0; }}

                img {{
                    max-width: 100%;
                    max-height: 80vh;
                    height: auto;
                    border-radius: 4px;
                    break-inside: avoid;
                }}

                blockquote {{
                    border-left: 3px solid #E50914;
                    margin: 1em 0;
                    padding: 0.5em 1em;
                    color: {blockquoteColor};
                    font-style: italic;
                    break-inside: avoid;
                }}

                /* Mobile-friendly adjustments */
                @media (max-width: 600px) {{
                    #content {{
                        padding: 24px 20px 32px;
                        font-size: 16px;
                    }}
                }}
            </style>
            </head>
            <body>
            <div id=""pager""><div id=""content"">{body}</div></div>

            <script>
            (function() {{
                var pager = document.getElementById('pager');
                var currentPage = 0;
                var pageWidth = 0;
                var totalPages = 0;

                function computeLayout() {{
                    pageWidth = window.innerWidth;
                    totalPages = Math.max(1, Math.round(pager.scrollWidth / pageWidth));
                    return totalPages;
                }}

                window.getTotalPages = function() {{
                    return computeLayout();
                }};

                window.goToPage = function(page) {{
                    computeLayout();
                    currentPage = Math.max(0, Math.min(page, totalPages - 1));
                    pager.style.transform = 'translateX(' + (-currentPage * pageWidth) + 'px)';
                }};

                document.addEventListener('click', function(e) {{
                    if (e.target.tagName === 'A') return;
                    if (e.clientX > window.innerWidth / 2)
                        window.location.href = 'reader://nav/next';
                    else
                        window.location.href = 'reader://nav/prev';
                }});
            }})();
            </script>
            </body>
            </html>";
    }

    // ── Theme refresh ─────────────────────────────────────────────────────────

    public async void RefreshTheme()
    {
        if (_chapters.Count == 0) return;
        _savedPage = _currentPage;
        var chapter = _chapters[_currentChapter];
        var html = BuildPagedHtml(chapter.Content ?? "", LibraryData.Theme == "Dark");
        ContentWebView.Source = new HtmlWebViewSource { Html = html };
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private async void PrevPage_Click(object? sender, EventArgs e)
        => await NavigatePrevAsync();

    private async void NextPage_Click(object? sender, EventArgs e)
        => await NavigateNextAsync();

    private async Task NavigateNextAsync()
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

    private async Task NavigatePrevAsync()
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
}