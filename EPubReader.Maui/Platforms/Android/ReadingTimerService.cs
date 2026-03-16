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

    private System.Timers.Timer? _ticker;
    private DateTime _startTime;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        _startTime = DateTime.UtcNow;

        CreateNotificationChannel();
        StartForeground(NotificationId, BuildNotification("📖 Reading  ·  0:00"));

        _ticker = new System.Timers.Timer(10_000); // update every 10 seconds
        _ticker.Elapsed += (_, _) => UpdateNotification();
        _ticker.AutoReset = true;
        _ticker.Start();

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        _ticker?.Stop();
        _ticker?.Dispose();
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }

    private void UpdateNotification()
    {
        var elapsed = DateTime.UtcNow - _startTime;
        var text = elapsed.TotalHours >= 1
            ? $"📖 Reading  ·  {(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m"
            : $"📖 Reading  ·  {(int)elapsed.TotalMinutes}m {elapsed.Seconds:D2}s";
        var nm = (NotificationManager?)GetSystemService(NotificationService);
        nm?.Notify(NotificationId, BuildNotification(text));
    }

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
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var channel = new NotificationChannel(
                ChannelId, "Reading Timer",
                NotificationImportance.Low)   // Low = no sound, no heads-up
            {
                Description = "Shows elapsed reading time"
            };
            var nm = (NotificationManager?)GetSystemService(NotificationService);
            nm?.CreateNotificationChannel(channel);
        }
    }
}