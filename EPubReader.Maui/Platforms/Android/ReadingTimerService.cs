using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace EPubReader.Maui;

[Service(ForegroundServiceType = Android.Content.PM.ForegroundService.TypeDataSync)]
public class ReadingTimerService : Android.App.Service
{
    public const string ChannelId = "reading_timer_channel";
    public const int NotificationId = 42;
    public const string ExtraSeconds = "elapsed_seconds";   // ← new

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        CreateNotificationChannel();

        // Every start/update call carries the current elapsed seconds from ReaderPage.
        var seconds = intent?.GetLongExtra(ExtraSeconds, 0) ?? 0;
        var text = FormatElapsed(seconds);

        StartForeground(NotificationId, BuildNotification(text));

        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatElapsed(long seconds) =>
        seconds >= 3600
            ? $"📖 Reading  ·  {seconds / 3600}h {(seconds % 3600) / 60:D2}m"
            : $"📖 Reading  ·  {seconds / 60}m {seconds % 60:D2}s";

    private Notification BuildNotification(string text)
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.SingleTop);
        var pi = PendingIntent.GetActivity(this, 0, intent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        return new NotificationCompat.Builder(this, ChannelId)
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetContentTitle(text)
            .SetOngoing(true)
            .SetSilent(true)
            .SetContentIntent(pi)
            .Build()!;
    }

    private void CreateNotificationChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var channel = new NotificationChannel(
            ChannelId, "Reading Timer", NotificationImportance.Low)
        {
            Description = "Shows elapsed reading time"
        };
        var nm = (NotificationManager?)GetSystemService(NotificationService);
        nm?.CreateNotificationChannel(channel);
    }
}