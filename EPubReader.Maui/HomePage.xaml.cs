using System.Diagnostics;

namespace EPubReader.Maui;

public partial class HomePage : ContentPage
{
    private List<string> _fandoms = new();
    private readonly ILibraryScanner _scanner;
    private bool _hasAutoDownloaded = false; 


    public HomePage()
    {
        InitializeComponent();
        _scanner = IPlatformApplication.Current!.Services.GetRequiredService<ILibraryScanner>();
    }

    public HomePage(ILibraryScanner scanner)
    {
        InitializeComponent();
        _scanner = scanner;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override async void OnAppearing()
    {
        base.OnAppearing();

#if ANDROID
        try
        {
            var activity = (Android.App.Activity)Microsoft.Maui.ApplicationModel.Platform.CurrentActivity!;
            var filePath = activity.Intent?.GetStringExtra("open_book_file_path");
            if (!string.IsNullOrEmpty(filePath))
            {
                // Clear the extra so it doesn't fire again on back-navigation
                activity.Intent?.RemoveExtra("open_book_file_path");

                var last = LibraryData.LastReadBook;
                if (last != null)
                {
                    var book = new BookItem
                    {
                        CalibreKey = last.CalibreKey,
                        Title = last.Title,
                        Author = last.Author,
                        CoverImagePath = last.CoverImagePath,
                        FilePath = filePath,
                        FileType = System.IO.Path.GetExtension(last.CalibreKey).TrimStart('.')
                    };
                    await Navigation.PushAsync(new ReaderPage(book, _scanner));
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Widget open-book failed: {ex}");
        }
#endif

#if ANDROID
        ApplyAndroidLayout();

        var status = await Permissions.RequestAsync<Permissions.StorageRead>();
        if (status != PermissionStatus.Granted)
        {
            await DisplayAlert("Permission needed",
                "Storage access is required to read your library.", "OK");
        }

        await GoogleAuthService.Instance.InitAsync();

        // Auto-download library-data.json from Google Drive on app open
        if (!_hasAutoDownloaded)
        {
            _hasAutoDownloaded = true;
            await TryAutoDownloadLibraryDataAsync();
        }
#endif

        LibraryData.Load();
        LoadFandoms();
        LoadLastReadBook();
        LoadReadingStats();
    }

    // ── Auto-download library data from Drive ─────────────────────────────────

    private static async Task TryAutoDownloadLibraryDataAsync()
    {
        try
        {
            if (!GoogleAuthService.Instance.IsSignedIn) return;

            var localFile = ResolveLocalDataFile();
            if (localFile == null) return;

            var ok = await GoogleAuthService.Instance.DownloadLibraryDataAsync(localFile);
            Debug.WriteLine(ok
                ? "HomePage: auto-downloaded library-data from Drive"
                : "HomePage: Drive auto-download skipped (no file on Drive or not signed in)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HomePage.TryAutoDownloadLibraryDataAsync: {ex.Message}");
        }
    }

    private static string? ResolveLocalDataFile()
    {
        var saveDataPath = LibraryData.SaveDataPath;

        if (!string.IsNullOrEmpty(saveDataPath) && !saveDataPath.StartsWith("content://"))
            return System.IO.Path.Combine(saveDataPath, "library-data.json");

        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EPubReader",
            "library-data.json");
    }

    // ── Android layout ────────────────────────────────────────────────────────

    private void ApplyAndroidLayout()
    {
        SidebarColumn.Width = new GridLength(0);
        SidebarPanel.IsVisible = false;
        SidebarBorder.IsVisible = false;

        BottomBarRow.Height = new GridLength(64);
        AndroidBottomBar.IsVisible = true;
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    private void LoadFandoms()
    {
        try
        {
            _fandoms = LibraryData.GetAllFandoms();

            FandomList.ItemsSource = null;
            FandomList.ItemsSource = _fandoms;

            FandomListAndroid.ItemsSource = null;
            FandomListAndroid.ItemsSource = _fandoms;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HomePage.LoadFandoms: {ex}");
        }
    }

    private void LoadLastReadBook()
    {
        try
        {
            var last = LibraryData.LastReadBook;
            if (last == null || string.IsNullOrEmpty(last.Title))
            {
                ContinueReadingCard.IsVisible = false;
                return;
            }

            LastBookTitle.Text = last.Title;
            LastBookAuthor.Text = last.Author;

            if (!string.IsNullOrEmpty(last.CoverImagePath))
            {
                LastBookCover.Source = ImageSource.FromFile(last.CoverImagePath);
                LastBookCover.IsVisible = true;
            }
            else
            {
                LastBookCover.IsVisible = false;
            }

            ContinueReadingCard.IsVisible = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HomePage.LoadLastReadBook: {ex}");
            ContinueReadingCard.IsVisible = false;
        }
    }

    private void LoadReadingStats()
    {
        try
        {
            var totalSeconds = LibraryData.GetTotalReadingSeconds();

            var thisYear = DateTime.UtcNow.Year;
            var booksReadThisYear = LibraryData.GetStats().ReadHistory
                .Values
                .SelectMany(list => list)
                .Count(dt => dt.Year == thisYear);

            string timeMain, timeSub;
            if (totalSeconds <= 0 && LibraryData.GetBooksReadCount() == 0)
            {
                StatsCard.IsVisible = false;
                return;
            }
            else if (totalSeconds < 3600)
            {
                var mins = totalSeconds / 60;
                timeMain = $"{mins}m";
                timeSub = mins == 1 ? "minute read" : "minutes read";
            }
            else
            {
                var hours = totalSeconds / 3600;
                timeMain = $"{hours}h";
                timeSub = hours == 1 ? "hour read" : "hours read";
            }

            StatsTimeLabel.Text = timeMain;
            StatsTimeSubLabel.Text = timeSub;

            var timePerFandom = LibraryData.GetTimePerFandom()
    .Where(kvp => !string.IsNullOrEmpty(kvp.Key) && kvp.Key != "(No Fandom)")
    .OrderByDescending(kvp => kvp.Value)
    .FirstOrDefault();

            if (timePerFandom.Key != null)
            {
                var topSeconds = timePerFandom.Value;
                string timeSuffix = topSeconds >= 3600
                    ? $"{topSeconds / 3600}h spent"
                    : $"{topSeconds / 60}m spent";
                StatsTopFandomLabel.Text = timePerFandom.Key;
                StatsTopFandomSubLabel.Text = timeSuffix;
            }
            else
            {
                StatsTopFandomLabel.Text = "—";
                StatsTopFandomSubLabel.Text = "";
            }

            // Total books read (all time)
            var totalBooks = LibraryData.GetBooksReadCount();
            StatsTotalBooksLabel.Text = totalBooks.ToString();
            StatsTotalBooksSubLabel.Text = totalBooks == 1 ? "book read" : "books read";

            // Time read today
            var todaySeconds = LibraryData.GetReadingSecondsForDate(); 
            if (todaySeconds <= 0)
            {
                StatsTodayTimeLabel.Text = "—";
                StatsTodayTimeSubLabel.Text = "no reading yet";
            }
            else if (todaySeconds < 3600)
            {
                var mins = todaySeconds / 60;
                StatsTodayTimeLabel.Text = $"{mins}m";
                StatsTodayTimeSubLabel.Text = mins == 1 ? "minute today" : "minutes today";
            }
            else
            {
                var hrs = todaySeconds / 3600;
                StatsTodayTimeLabel.Text = $"{hrs}h";
                StatsTodayTimeSubLabel.Text = hrs == 1 ? "hour today" : "hours today";
            }

            StatsCard.IsVisible = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HomePage.LoadReadingStats: {ex}");
        }
    }

    // ── Continue reading ──────────────────────────────────────────────────────

    private async void ContinueReading_Tapped(object? sender, TappedEventArgs e)
    {
        var last = LibraryData.LastReadBook;
        if (last == null || string.IsNullOrEmpty(last.CalibreKey)) return;

        try
        {
            BookItem? book = null;

#if ANDROID
            // Prefer the saved FilePath (content:// URI) — works for both local SAF
            // and Google Drive SAF where doc IDs are opaque and can't be reconstructed.
            var savedFilePath = last.FilePath;
            if (!string.IsNullOrEmpty(savedFilePath) && 
                (savedFilePath.StartsWith("content://") || savedFilePath.StartsWith("gdrive://")))
            {
                book = new BookItem
                {
                    CalibreKey = last.CalibreKey,
                    Title = last.Title,
                    Author = last.Author,
                    CoverImagePath = last.CoverImagePath,
                    FilePath = savedFilePath,
                    FileType = System.IO.Path.GetExtension(last.CalibreKey).TrimStart('.')
                };
            }
            else
            {
                // Fallback: try to reconstruct the URI from the CalibreKey (local SAF only)
                var resolvedUri = _scanner.ResolveFileUriFromCalibreKey(last.CalibreKey);
                if (!string.IsNullOrEmpty(resolvedUri))
                {
                    book = new BookItem
                    {
                        CalibreKey = last.CalibreKey,
                        Title = last.Title,
                        Author = last.Author,
                        CoverImagePath = last.CoverImagePath,
                        FilePath = resolvedUri,
                        FileType = System.IO.Path.GetExtension(last.CalibreKey).TrimStart('.')
                    };
                }
            }
#endif
            // Fallback: full library scan (Windows / desktop, or if fast path failed)
            if (book == null)
            {
                var books = await Task.Run(() => _scanner.ScanLibrary(LibraryData.LibraryPath));
                book = books.FirstOrDefault(b => b.CalibreKey == last.CalibreKey);
            }

            if (book == null)
            {
                await DisplayAlert("Not Found",
                    "The last-read book could not be found in the current library.", "OK");
                return;
            }

            await Navigation.PushAsync(new ReaderPage(book, _scanner));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HomePage.ContinueReading_Tapped: {ex}");
            await DisplayAlert("Error", $"Could not open book: {ex.Message}", "OK");
        }
    }

    // ── Sidebar fandom selection ──────────────────────────────────────────────

    private void FandomList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FandomList.SelectedItem is string fandom)
            _ = NavigateToFandomAsync(fandom);
    }

    private async Task NavigateToFandomAsync(string fandom)
    {
        await Navigation.PushAsync(new MainPage(_scanner, fandom));
    }

    // ── Add fandom (sidebar) ──────────────────────────────────────────────────

    private void AddFandom_Click(object? sender, EventArgs e)
        => CommitNewFandom(NewFandomInput.Text ?? "");

    private void NewFandomInput_Completed(object? sender, EventArgs e)
        => CommitNewFandom(NewFandomInput.Text ?? "");

    private void CommitNewFandom(string fandom)
    {
        fandom = fandom.Trim();
        if (string.IsNullOrWhiteSpace(fandom)) return;

        try
        {
            LibraryData.AddStandaloneFandom(fandom);
            NewFandomInput.Text = "";
            LoadFandoms();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HomePage.CommitNewFandom: {ex}");
        }
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    private async void Settings_Click(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new SettingsPage());
    }

    // ── Bottom bar ────────────────────────────────────────────────────────────

    private async void BottomBarFandoms_Click(object? sender, EventArgs e)
    {
        FandomOverlay.IsVisible = true;
        FandomSheet.TranslationY = 500;
        await FandomSheet.TranslateTo(0, 0, 250, Easing.CubicOut);
    }

    // Navigates back to HomePage (this page is already the root, so just reload)
    private void BottomBarHome_Click(object? sender, EventArgs e)
    {
        // HomePage IS the home — pop back to root if we're somehow in a nav stack,
        // otherwise just refresh the data.
        if (Navigation.NavigationStack.Count > 1)
            _ = Navigation.PopToRootAsync();
        else
        {
            LibraryData.Load();
            LoadFandoms();
            LoadLastReadBook();
            LoadReadingStats();
        }
    }

    // ── Fandom overlay ────────────────────────────────────────────────────────

    private async void FandomOverlay_Tapped(object? sender, TappedEventArgs e)
        => await CloseFandomSheetAsync();

    private async void CloseFandomSheet_Click(object? sender, EventArgs e)
        => await CloseFandomSheetAsync();

    private async Task CloseFandomSheetAsync()
    {
        await FandomSheet.TranslateTo(0, 500, 220, Easing.CubicIn);
        FandomOverlay.IsVisible = false;
        FandomSheet.TranslationY = 0;
    }

    private async void FandomListAndroid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FandomListAndroid.SelectedItem is string fandom)
        {
            BottomBarFandomsButton.Text = $"📚  {fandom}";
            await CloseFandomSheetAsync();
            await NavigateToFandomAsync(fandom);
        }
    }

    // ── Add fandom (Android sheet) ────────────────────────────────────────────

    private void AddFandomAndroid_Click(object? sender, EventArgs e)
        => CommitNewFandomAndroid(NewFandomInputAndroid.Text ?? "");

    private void NewFandomInputAndroid_Completed(object? sender, EventArgs e)
        => CommitNewFandomAndroid(NewFandomInputAndroid.Text ?? "");

    private void CommitNewFandomAndroid(string fandom)
    {
        fandom = fandom.Trim();
        if (string.IsNullOrWhiteSpace(fandom)) return;

        try
        {
            LibraryData.AddStandaloneFandom(fandom);
            NewFandomInputAndroid.Text = "";
            LoadFandoms();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HomePage.CommitNewFandomAndroid: {ex}");
        }
    }
}