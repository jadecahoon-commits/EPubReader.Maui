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