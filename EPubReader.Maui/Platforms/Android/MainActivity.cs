using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace EPubReader.Maui
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                               ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        public static Action<int, string>? OnFolderPicked;

        // ── Add these two ──────────────────────────────────────────────────────
        public static event Action? AppPaused;
        public static event Action? AppResumed;

        protected override void OnPause()
        {
            base.OnPause();
            AppPaused?.Invoke();
        }

        protected override void OnResume()
        {
            base.OnResume();
            AppResumed?.Invoke();
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            Intent = intent; // update the activity's current intent

            // If a widget tap arrived while the app is already running, handle it immediately
            var filePath = intent?.GetStringExtra("open_book_file_path");
            if (!string.IsNullOrEmpty(filePath))
                OpenBookFromWidget(filePath);
        }

        private void OpenBookFromWidget(string filePath)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    var last = LibraryData.LastReadBook;
                    if (last == null) return;

                    // Find the current NavigationPage and push ReaderPage
                    if (Microsoft.Maui.Controls.Application.Current?.MainPage is NavigationPage navPage)
                    {
                        // Pop back to root (HomePage) first if deep in stack
                        await navPage.PopToRootAsync(false);
                        var book = new BookItem
                        {
                            CalibreKey = last.CalibreKey,
                            Title = last.Title,
                            Author = last.Author,
                            CoverImagePath = last.CoverImagePath,
                            FilePath = filePath,
                            FileType = System.IO.Path.GetExtension(last.CalibreKey).TrimStart('.')
                        };
                        var scanner = IPlatformApplication.Current!.Services
                            .GetRequiredService<ILibraryScanner>();
                        await navPage.PushAsync(new ReaderPage(book, scanner));

                        // Clear the extra so OnAppearing doesn't double-fire
                        Intent?.RemoveExtra("open_book_file_path");
                    }
                }
                catch (Exception ex)
                {
                    Android.Util.Log.Error("MainActivity", $"OpenBookFromWidget failed: {ex}");
                }
            });
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent? data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            if (resultCode != Result.Ok || data?.Data == null) return;

            var uri = data.Data;
            ContentResolver?.TakePersistableUriPermission(uri,
                Android.Content.ActivityFlags.GrantReadUriPermission |
                Android.Content.ActivityFlags.GrantWriteUriPermission |
                (Android.Content.ActivityFlags)0x00000200);

            OnFolderPicked?.Invoke(requestCode, uri.ToString()!);
        }
    }
}