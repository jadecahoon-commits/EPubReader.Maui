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
#if ANDROID
        MainActivity.OnFolderPicked = (code, path) =>
        {
            if (code != 1001) return;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LibraryData.LibraryPath = path;
                UpdatePathLabels();
            });
            MainActivity.OnFolderPicked = null;
        };
        LaunchFolderPicker(1001);
#else
    try
    {
        var result = await CommunityToolkit.Maui.Storage.FolderPicker.Default.PickAsync(default);
        if (!result.IsSuccessful) return;
        LibraryData.LibraryPath = result.Folder.Path;
        UpdatePathLabels();
    }
    catch (Exception ex)
    {
        await DisplayAlert("Error", $"Could not pick folder: {ex.Message}", "OK");
    }
#endif
    }

    // ── Save data folder ──────────────────────────────────────────────────────

    private async void BrowseSaveData_Click(object? sender, EventArgs e)
    {
#if ANDROID
        MainActivity.OnFolderPicked = (code, path) =>
        {
            if (code != 1002) return;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
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
                        LibraryData.SaveDataPath = path;
                        LibraryData.Load();
                    }
                    else
                    {
                        LibraryData.SaveDataPath = path;
                    }
                }
                else
                {
                    LibraryData.SaveDataPath = path;
                }

                UpdatePathLabels();

                await DisplayAlert(
                    "Save Data Folder Set",
                    $"Your data will be saved to:\n{candidateFile}\n\nSet the same folder on your other devices to sync.",
                    "OK");
            });
            MainActivity.OnFolderPicked = null;
        };
        LaunchFolderPicker(1002);
#else
        try
        {
            var result = await FolderPicker.Default.PickAsync(default);
            if (!result.IsSuccessful) return;

            var path = result.Folder.Path;
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
                    LibraryData.SaveDataPath = path;
                    LibraryData.Load();
                }
                else
                {
                    LibraryData.SaveDataPath = path;
                }
            }
            else
            {
                LibraryData.SaveDataPath = path;
            }

            UpdatePathLabels();

            await DisplayAlert(
                "Save Data Folder Set",
                $"Your data will be saved to:\n{candidateFile}\n\nSet the same folder on your other devices to sync.",
                "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not pick folder: {ex.Message}", "OK");
        }
#endif
    }

    // ── Theme ─────────────────────────────────────────────────────────────────

    private void DarkModeToggle_Toggled(object? sender, ToggledEventArgs e)
    {
        var isDark = DarkModeToggle.IsToggled;
        LibraryData.Theme = isDark ? "Dark" : "Light";

        if (Application.Current != null)
            Application.Current.UserAppTheme = isDark ? AppTheme.Dark : AppTheme.Light;
    }

    // ── Android folder picker ─────────────────────────────────────────────────

#if ANDROID
    private static void LaunchFolderPicker(int requestCode)
    {
        var intent = new Android.Content.Intent(Android.Content.Intent.ActionOpenDocumentTree);
        intent.AddFlags(
            Android.Content.ActivityFlags.GrantReadUriPermission |
            Android.Content.ActivityFlags.GrantWriteUriPermission |
            Android.Content.ActivityFlags.GrantPersistableUriPermission);
        Platform.CurrentActivity?.StartActivityForResult(intent, requestCode);
    }
#endif
}