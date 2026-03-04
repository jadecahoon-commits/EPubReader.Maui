using CommunityToolkit.Maui.Storage;
using System.Diagnostics;

namespace EPubReader.Maui;

public partial class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        InitializeComponent();
        DarkModeToggle.IsToggled = LibraryData.Theme == "Dark";
        UpdatePathLabels();
        // Restore Google Drive sign-in state from SecureStorage
#if ANDROID
        _ = InitGoogleDriveAsync();
#endif
    }

    // ── Path labels ───────────────────────────────────────────────────────────

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
            var result = await FolderPicker.Default.PickAsync(default);
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
        intent.PutExtra("android.content.extra.SHOW_ADVANCED", true);
        intent.PutExtra("android.content.extra.FANCY", true);
        intent.AddFlags(
            Android.Content.ActivityFlags.GrantReadUriPermission |
            Android.Content.ActivityFlags.GrantWriteUriPermission |
            Android.Content.ActivityFlags.GrantPersistableUriPermission |
            (Android.Content.ActivityFlags)0x00000200
        );
        Platform.CurrentActivity?.StartActivityForResult(intent, requestCode);
    }
#endif

    // ── Google Drive ──────────────────────────────────────────────────────────


    private void UpdateGoogleDriveUI()
    {
        var isSignedIn = GoogleAuthService.Instance.IsSignedIn;

        GoogleDriveStatusLabel.Text = isSignedIn
            ? $"Signed in as {GoogleAuthService.Instance.UserEmail}"
            : "Not signed in";

        GoogleDriveButton.Text = isSignedIn ? "Sign Out" : "Sign In";
        GoogleDriveButton.BackgroundColor = isSignedIn
            ? Color.FromArgb("#888888")
            : Color.FromArgb("#4285F4");

        GoogleDriveActionsGrid.IsVisible = isSignedIn;
        GoogleDriveActionStatus.IsVisible = false;

        GoogleDriveLibraryLabel.Text = GoogleAuthService.Instance.LibraryFolderName is { } name
        ? $"Library: {name}"
        : "No library folder selected";


    }


    private async void GoogleDrivePickFolder_Click(object sender, EventArgs e)
    {
        var picker = new DriveFolderPickerPage();
        picker.Disappearing += (_, _) => UpdateGoogleDriveUI(); // refresh label after picking
        await Navigation.PushModalAsync(picker);
    }
    private async Task InitGoogleDriveAsync()
    {
        await GoogleAuthService.Instance.InitAsync();
        UpdateGoogleDriveUI();
    }

    private async void GoogleDrive_Click(object? sender, EventArgs e)
    {
        if (GoogleAuthService.Instance.IsSignedIn)
        {
            // Sign out
            var confirm = await DisplayAlert(
                "Sign Out",
                "Sign out of Google Drive? Your local data won't be affected.",
                "Sign Out", "Cancel");

            if (!confirm) return;

            GoogleAuthService.Instance.SignOut();
            UpdateGoogleDriveUI();
        }
        else
        {
            GoogleDriveButton.IsEnabled = false;
            GoogleDriveButton.Text = "Signing in…";

            try
            {
                var success = await GoogleAuthService.Instance.SignInAsync();

                if (success)
                {
                    UpdateGoogleDriveUI();
                }
                else
                {
                    await DisplayAlert("Sign In Failed",
                        "SignInAsync returned false — check debug output.", "OK");
                    GoogleDriveButton.IsEnabled = true;
                    GoogleDriveButton.Text = "Sign In";
                }
            }
            catch (Exception ex)
            {
                // Show the REAL exception instead of swallowing it
                await DisplayAlert("Exception",
                    $"{ex.GetType().Name}\n\n{ex.Message}\n\n{ex.StackTrace}", "OK");
                GoogleDriveButton.IsEnabled = true;
                GoogleDriveButton.Text = "Sign In";
            }
        }
    }

    private async void GoogleDriveUpload_Click(object? sender, EventArgs e)
    {
        await RunDriveActionAsync(async () =>
        {
            // Resolve the local data file path the same way LibraryData does
            var localFile = ResolveLocalDataFile();
            if (localFile == null || !File.Exists(localFile))
            {
                await DisplayAlert("Nothing to Upload",
                    "No local library-data.json found. Save some data first.", "OK");
                return;
            }

            var ok = await GoogleAuthService.Instance.UploadLibraryDataAsync(localFile);
            ShowDriveStatus(ok
                ? "✓ Uploaded to Google Drive"
                : "✗ Upload failed — check your connection");
        });
    }

    private async void GoogleDriveDownload_Click(object? sender, EventArgs e)
    {
        await RunDriveActionAsync(async () =>
        {
            var localFile = ResolveLocalDataFile();
            if (localFile == null)
            {
                await DisplayAlert("Error", "No local save path is configured.", "OK");
                return;
            }

            bool overwrite = true;
            if (File.Exists(localFile))
            {
                overwrite = await DisplayAlert(
                    "Overwrite Local Data?",
                    "This will replace your local library-data.json with the version from Google Drive. Continue?",
                    "Yes, overwrite", "Cancel");
            }

            if (!overwrite) return;

            var ok = await GoogleAuthService.Instance.DownloadLibraryDataAsync(localFile);
            if (ok)
            {
                LibraryData.Load();
                ShowDriveStatus("✓ Downloaded from Google Drive — library data reloaded");
            }
            else
            {
                ShowDriveStatus("✗ Download failed — no file on Drive yet, or check your connection");
            }
        });
    }

    private async Task RunDriveActionAsync(Func<Task> action)
    {
        GoogleDriveActionsGrid.IsEnabled = false;
        GoogleDriveActionStatus.IsVisible = true;
        GoogleDriveActionStatus.Text = "Working…";

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Drive action error: {ex}");
            ShowDriveStatus($"✗ Error: {ex.Message}");
        }
        finally
        {
            GoogleDriveActionsGrid.IsEnabled = true;
        }
    }

    private void ShowDriveStatus(string message)
    {
        GoogleDriveActionStatus.IsVisible = true;
        GoogleDriveActionStatus.Text = message;
    }


    /// <summary>
    /// Returns the path to library-data.json, mirroring LibraryData's internal logic.
    /// We duplicate the path resolution here so the Settings page can locate the file
    /// without exposing LibraryData internals.
    /// </summary>
    private static string? ResolveLocalDataFile()
    {
        var saveDataPath = LibraryData.SaveDataPath;

        if (!string.IsNullOrEmpty(saveDataPath) && !saveDataPath.StartsWith("content://"))
            return System.IO.Path.Combine(saveDataPath, "library-data.json");

        // Fallback to local app data
        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EPubReader",
            "library-data.json");
    }


    // In SettingsPage.xaml.cs, replace the GoogleDriveSyncLibrary_Click method with this:

    private async void GoogleDriveSyncLibrary_Click(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(GoogleAuthService.Instance.LibraryFolderId))
        {
            await DisplayAlert("No Library Selected",
                "Please use '📁 Set Drive Library Location' to pick your Calibre library folder first.",
                "OK");
            return;
        }

        GoogleDriveSyncButton.IsEnabled = false;
        ShowDriveStatus("Starting Drive library scan…");

        var progress = new Progress<(string Message, int Percent)>(report =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
                ShowDriveStatus($"🔄 {report.Message} ({report.Percent}%)"));
        });

        try
        {
            var manifest = await GoogleAuthService.Instance.ScanLibraryFolderAsync(progress);

            if (manifest == null)
            {
                ShowDriveStatus("✗ Scan failed — check sign-in and library folder.");
                return;
            }

            manifest.Save();

#if ANDROID
            try
            {
                var debugPath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(
                        ResolveLocalDataFile() ?? "") ?? "",
                    "drive-manifest-debug.json");

                var json = System.Text.Json.JsonSerializer.Serialize(
                    manifest,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(debugPath, json);
                Debug.WriteLine($"Debug manifest written to {debugPath}");
            }
            catch (Exception dbgEx)
            {
                Debug.WriteLine($"Failed to write debug manifest: {dbgEx.Message}");
            }
#endif

            var bookCount = manifest.Authors.Sum(a => a.Books.Count);
            ShowDriveStatus(
                $"✓ Synced {bookCount} books across {manifest.Authors.Count} authors. " +
                $"Tap back to browse your library.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GoogleDriveSyncLibrary: {ex}");
            ShowDriveStatus($"✗ Error: {ex.Message}");
        }
        finally
        {
            GoogleDriveSyncButton.IsEnabled = true;
        }
    }
}