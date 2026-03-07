using System.Diagnostics;

namespace EPubReader.Maui;

public partial class HomePage : ContentPage
{
    private List<string> _fandoms = new();
    private readonly ILibraryScanner _scanner;

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
        ApplyAndroidLayout();

        var status = await Permissions.RequestAsync<Permissions.StorageRead>();
        if (status != PermissionStatus.Granted)
        {
            await DisplayAlert("Permission needed",
                "Storage access is required to read your library.", "OK");
        }

        await GoogleAuthService.Instance.InitAsync();

        // Auto-download library-data.json from Google Drive on app open
        await TryAutoDownloadLibraryDataAsync();
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
            if (totalSeconds <= 0)
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

            var topFandom = LibraryData.GetStats().ReadHistory
                .GroupBy(kvp => LibraryData.GetFandom(kvp.Key))
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .OrderByDescending(g => g.Sum(kvp => kvp.Value.Count))
                .FirstOrDefault();

            if (topFandom != null)
            {
                StatsTopFandomLabel.Text = topFandom.Key;
                StatsTopFandomSubLabel.Text = $"{topFandom.Sum(kvp => kvp.Value.Count)} sessions";
            }
            else
            {
                StatsTopFandomLabel.Text = "—";
                StatsTopFandomSubLabel.Text = "";
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
        try
        {
            var last = LibraryData.LastReadBook;
            if (last == null) return;

            var books = await Task.Run(() => _scanner.ScanLibrary(LibraryData.LibraryPath));
            var book = books.FirstOrDefault(b => b.CalibreKey == last.CalibreKey);

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