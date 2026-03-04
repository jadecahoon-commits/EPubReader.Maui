namespace EPubReader.Maui;

public partial class DriveFolderPickerPage : ContentPage
{
    private record BreadcrumbEntry(string Id, string Name);

    private readonly Stack<BreadcrumbEntry> _stack = new();
    private string _currentFolderId = "root";

    public DriveFolderPickerPage()
    {
        InitializeComponent();
        _ = LoadFoldersAsync("root", "My Drive");
    }

    private async Task LoadFoldersAsync(string folderId, string folderName)
    {
        LoadingIndicator.IsVisible = true;
        LoadingIndicator.IsRunning = true;
        FolderList.ItemsSource = null;

        _currentFolderId = folderId;
        BackButton.IsEnabled = _stack.Count > 0;

        // Build breadcrumb text
        var crumbs = _stack.Reverse().Select(e => e.Name).ToList();
        crumbs.Add(folderName);
        BreadcrumbLabel.Text = string.Join(" › ", crumbs);

        try
        {
            var folders = await GoogleAuthService.Instance.ListFoldersAsync(folderId);
            FolderList.ItemsSource = folders;
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not load folders: {ex.Message}", "OK");
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        }
    }

    private async void OnFolderTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not GoogleAuthService.DriveFolderItem folder) return;
        _stack.Push(new BreadcrumbEntry(_currentFolderId, BreadcrumbLabel.Text.Split(" › ").Last()));
        await LoadFoldersAsync(folder.Id, folder.Name);
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        if (!_stack.TryPop(out var parent)) return;
        await LoadFoldersAsync(parent.Id, parent.Name);
    }

    private async void OnSelectClicked(object sender, EventArgs e)
    {
        var folderName = BreadcrumbLabel.Text.Split(" › ").Last();

        // Persist the Drive folder as the selected library
        await GoogleAuthService.Instance.SetLibraryFolderAsync(_currentFolderId, folderName);

        // Store a gdrive:// URI as the LibraryPath so MainPage knows to use Drive scanning
        LibraryData.LibraryPath = $"{GoogleAuthService.DriveLibraryPrefix}{_currentFolderId}";

        await Navigation.PopModalAsync();
        // MainPage.OnAppearing fires after modal closes and calls LoadBooks(),
        // which detects the gdrive:// prefix and runs the Drive scanner.
    }
}