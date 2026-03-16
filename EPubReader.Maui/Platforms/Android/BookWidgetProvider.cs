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
    [Preserve(AllMembers = true)]
    public class BookWidgetProvider : AppWidgetProvider
    {
        public const string ActionBookChanged = "com.companyname.epubreader.maui.BOOK_CHANGED";

        // SharedPreferences keys — written by LibraryData, read by widget
        public const string PrefFile = "epubreader_widget";
        public const string PrefTitle = "last_title";
        public const string PrefAuthor = "last_author"; 
        public const string PrefFilePath = "last_file_path";
        public const string PrefCoverPath = "last_cover_path";


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

        internal static void UpdateWidget(Context context, AppWidgetManager manager, int widgetId)
        {
            try  
            {
                var views = new RemoteViews(context.PackageName!, Resource.Layout.book_widget);
                // Read from SharedPreferences — written by the MAUI app via LibraryData
                var prefs = context.GetSharedPreferences(PrefFile, FileCreationMode.Private)!;
                var title = prefs.GetString(PrefTitle, null);
                var author = prefs.GetString(PrefAuthor, null);
                var filePath = prefs.GetString(PrefFilePath, null);
                var coverPath = prefs.GetString(PrefCoverPath, null);

                // Replace the cover check:
                if (!string.IsNullOrEmpty(coverPath)
                    && !coverPath.StartsWith("content://")   // widget can't read content URIs
                    && !coverPath.StartsWith("gdrive://")    // same for gdrive
                    && System.IO.File.Exists(coverPath))
                {
                    try
                    {
                        var bitmap = Android.Graphics.BitmapFactory.DecodeFile(coverPath);
                        if (bitmap != null)
                        {
                            views.SetImageViewBitmap(Resource.Id.widget_cover, bitmap);
                            views.SetViewVisibility(Resource.Id.widget_cover, Android.Views.ViewStates.Visible);
                            views.SetViewVisibility(Resource.Id.widget_scrim, Android.Views.ViewStates.Visible);
                            views.SetViewVisibility(Resource.Id.widget_bg_solid, Android.Views.ViewStates.Gone);
                        }
                    }
                    catch (Exception coverEx)
                    {
                        Android.Util.Log.Warn("BookWidget", $"Cover load failed: {coverEx.Message}");
                    }
                }



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
                if (!string.IsNullOrEmpty(filePath))
                    launch.PutExtra("open_book_file_path", filePath);
                var pending = PendingIntent.GetActivity(
                    context, 0, launch,
                    PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
                views.SetOnClickPendingIntent(Resource.Id.widget_root, pending);

                manager.UpdateAppWidget(widgetId, views);
            }
            catch (Exception ex)
            {
                Android.Util.Log.Error("BookWidget", $"UpdateWidget failed: {ex.Message}");
            }
        }
    }
}
