using CommunityToolkit.Maui.Storage;

namespace EPubReader.Maui;

public partial class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        InitializeComponent();
        DarkModeToggle.IsToggled = LibraryData.Theme == "Dark";
        UpdatePathLabels();
    }

    private void UpdatePathLabels()
    {
        LibraryPathLabel.Text = string.IsNullOrEmpty(LibraryData.LibraryPath)
            ? "No folder selected"
            : LibraryData.LibraryPath;

        SaveDataPathLabel.Text = string.IsNullOrEmpty(LibraryData.SaveDataPath)
            ? "Using local storage (no sync)"
            : LibraryData.SaveDataPath;
    }

    // ── Library folder ────────────────────────────────────────────────────────

    private async void BrowseLibrary_Click(object? sender, EventArgs e)
    {
        try
        {
            var result = await FolderPicker.Default.PickAsync(default);
            if (!result.IsSuccessful) return;

            var path = result.Folder.Path;

#if ANDROID
            TryPersistAndroidUri(path);
#endif

            LibraryData.LibraryPath = path;
            UpdatePathLabels();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not pick folder: {ex.Message}", "OK");
        }
    }

    // ── Save data folder ──────────────────────────────────────────────────────

    private async void BrowseSaveData_Click(object? sender, EventArgs e)
    {
        try
        {
            var result = await FolderPicker.Default.PickAsync(default);
            if (!result.IsSuccessful) return;

            var path = result.Folder.Path;

#if ANDROID
            TryPersistAndroidUri(path);
#endif

            // Check whether there's already a save file at the new location.
            // If so, offer to load it (it may be the synced file from another device).
            var candidateFile = System.IO.Path.Combine(path, "library-data.json");
            if (System.IO.File.Exists(candidateFile) && !string.IsNullOrEmpty(LibraryData.SaveDataPath))
            {
                var load = await DisplayAlert(
                    "Existing save data found",
                    "A library-data.json already exists in this folder. Load it? " +
                    "(Choosing 'No' will overwrite it with your current data.)",
                    "Yes, load it", "No, overwrite");

                if (load)
                {
                    // Point to new path first so Load() reads from there
                    LibraryData.SaveDataPath = path;
                    LibraryData.Load();
                }
                else
                {
                    LibraryData.SaveDataPath = path; // triggers SaveData() to overwrite
                }
            }
            else
            {
                LibraryData.SaveDataPath = path; // triggers SaveData() to write there
            }

            UpdatePathLabels();

            await DisplayAlert(
                "Save Data Folder Set",
                $"Your data will be saved to:\n{candidateFile}\n\n" +
                "Set the same folder on your other devices to sync.",
                "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not pick folder: {ex.Message}", "OK");
        }
    }

    // ── Theme ─────────────────────────────────────────────────────────────────

    private void DarkModeToggle_Toggled(object? sender, ToggledEventArgs e)
    {
        var isDark = DarkModeToggle.IsToggled;
        LibraryData.Theme = isDark ? "Dark" : "Light";

        if (Application.Current != null)
            Application.Current.UserAppTheme = isDark ? AppTheme.Dark : AppTheme.Light;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

#if ANDROID
    private static void TryPersistAndroidUri(string path)
    {
        try
        {
            var uri = Android.Net.Uri.Parse(path);
            if (uri != null)
            {
                var flags = Android.Content.ActivityFlags.GrantReadUriPermission
                          | Android.Content.ActivityFlags.GrantWriteUriPermission;
                Platform.CurrentActivity?.ContentResolver?
                    .TakePersistableUriPermission(uri, flags);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error persisting URI permission: {ex.Message}");
        }
    }
#endif
}