using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Runtime;
using Android.Widget;

namespace EPubReader.Maui
{
    [BroadcastReceiver(Label = "Continue Reading", Exported = true)]
    [IntentFilter(new[] { "android.appwidget.action.APPWIDGET_UPDATE" })]
    [MetaData("android.appwidget.provider", Resource = "@xml/book_widget_info")]
    [Preserve(AllMembers = true)]  // ← prevents the linker stripping this class

    public class BookWidgetProvider : AppWidgetProvider
    {
        public const string ActionBookChanged = "com.companyname.epubreader.maui.BOOK_CHANGED";

        // SharedPreferences keys — written by LibraryData, read by widget
        public const string PrefFile = "epubreader_widget";
        public const string PrefTitle = "last_title";
        public const string PrefAuthor = "last_author";

        public override void OnUpdate(Context context, AppWidgetManager appWidgetManager, int[] appWidgetIds)
        {
            foreach (var id in appWidgetIds)
                UpdateWidget(context, appWidgetManager, id);
        }

        public override void OnReceive(Context context, Intent intent)
        {
            base.OnReceive(context, intent);

            if (intent.Action == ActionBookChanged)
            {
                var manager = AppWidgetManager.GetInstance(context)!;
                var ids = manager.GetAppWidgetIds(
                    new ComponentName(context, Java.Lang.Class.FromType(typeof(BookWidgetProvider))));
                foreach (var id in ids)
                    UpdateWidget(context, manager, id);
            }
        }

        private static void UpdateWidget(Context context, AppWidgetManager manager, int widgetId)
        {
            var views = new RemoteViews(context.PackageName!, Resource.Layout.book_widget);

            // Read from SharedPreferences — written by the MAUI app via LibraryData
            var prefs = context.GetSharedPreferences(PrefFile, FileCreationMode.Private)!;
            var title = prefs.GetString(PrefTitle, null);
            var author = prefs.GetString(PrefAuthor, null);

            if (string.IsNullOrEmpty(title))
            {
                views.SetTextViewText(Resource.Id.widget_title, "No book yet");
                views.SetTextViewText(Resource.Id.widget_author, "Open the app to start reading");
            }
            else
            {
                views.SetTextViewText(Resource.Id.widget_title, title);
                views.SetTextViewText(Resource.Id.widget_author, author ?? "");
            }

            // Tap → open the app
            var launch = new Intent(context, typeof(MainActivity));
            launch.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            var pending = PendingIntent.GetActivity(
                context, 0, launch,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            views.SetOnClickPendingIntent(Resource.Id.widget_root, pending);

            manager.UpdateAppWidget(widgetId, views);
        }
    }
}
