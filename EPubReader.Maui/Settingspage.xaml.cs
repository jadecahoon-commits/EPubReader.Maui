using CommunityToolkit.Maui.Storage;

namespace EPubReader.Maui;

public partial class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        InitializeComponent();
        DarkModeToggle.IsToggled = LibraryData.Theme == "Dark";
        UpdateLibraryPathLabel();

    }

    private void UpdateLibraryPathLabel()
    {
        LibraryPathLabel.Text = string.IsNullOrEmpty(LibraryData.LibraryPath)
            ? "No folder selected"
            : LibraryData.LibraryPath;
    }

    private async void BrowseLibrary_Click(object? sender, EventArgs e)
    {
        try
        {
            var result = await FolderPicker.Default.PickAsync(default);
            if (result.IsSuccessful)
            {
                var path = result.Folder.Path;

#if ANDROID
                // Persist read permission across reboots
                try
                {
                    var uri = Android.Net.Uri.Parse(path);
                    if (uri != null)
                    {
                        var flags = Android.Content.ActivityFlags.GrantReadUriPermission;
                        Platform.CurrentActivity?.ContentResolver?
                            .TakePersistableUriPermission(uri, flags);
                    }
                }
                catch (Exception ex)
                {
                    //Debug.WriteLine($"Error persisting URI permission: {ex.Message}");
                }
#endif

                LibraryData.LibraryPath = path;
                UpdateLibraryPathLabel();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not pick folder: {ex.Message}", "OK");
        }
    }

    private void DarkModeToggle_Toggled(object? sender, ToggledEventArgs e)
    {
        var isDark = DarkModeToggle.IsToggled;
        LibraryData.Theme = isDark ? "Dark" : "Light";

        if (Application.Current != null)
        {
            Application.Current.UserAppTheme = isDark ? AppTheme.Dark : AppTheme.Light;
        }
    }
}