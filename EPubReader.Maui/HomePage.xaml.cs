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
#endif

        LibraryData.Load();
        LoadFandoms();
        LoadLastReadBook();
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

            LastBookTitle.Text  = last.Title;
            LastBookAuthor.Text = last.Author;

            if (!string.IsNullOrEmpty(last.CoverImagePath))
            {
                LastBookCover.Source    = ImageSource.FromFile(last.CoverImagePath);
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

    // ── Continue Reading ──────────────────────────────────────────────────────

    private async void ContinueReading_Tapped(object? sender, TappedEventArgs e)
    {
        var last = LibraryData.LastReadBook;
        if (last == null || string.IsNullOrEmpty(last.CalibreKey)) return;

        try
        {
            // Load the full book list and find the matching book by CalibreKey.
            // This ensures we always use the correct platform-specific FilePath
            // regardless of which device (Windows/Android/Drive) opened it last.
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

    private async void BottomBarAddFandom_Click(object? sender, EventArgs e)
    {
        FandomOverlay.IsVisible = true;
        FandomSheet.TranslationY = 500;
        await FandomSheet.TranslateTo(0, 0, 250, Easing.CubicOut);
        await Task.Delay(280);
        NewFandomInputAndroid.Focus();
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
