using Android.App;
using Android.Content;
using Android.Content.PM;

namespace EPubReader.Maui;

[Activity(
    NoHistory = true,
    LaunchMode = LaunchMode.SingleTop,
    Exported = true)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "com.companyname.epubreader.maui",
    DataHost = "oauth2redirect")]
public class WebAuthCallbackActivity : WebAuthenticatorCallbackActivity
{
}