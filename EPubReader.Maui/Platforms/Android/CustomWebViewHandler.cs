using Android.Webkit;
using Microsoft.Maui.Handlers;

namespace EPubReader.Maui.Handlers;


public class CustomWebViewHandler : WebViewHandler
{
    protected override void ConnectHandler(global::Android.Webkit.WebView platformView)
    {
        base.ConnectHandler(platformView);

        platformView.SetBackgroundColor(global::Android.Graphics.Color.Transparent);

        var settings = platformView.Settings;

        // API 33+ (Android 13+)
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Tiramisu)
        {
            settings.AlgorithmicDarkeningAllowed = false;
        }

        // API 29-32 (set this too as some WebView builds still check it)
#pragma warning disable CA1422
        settings.ForceDark = ForceDarkMode.Off;
#pragma warning restore CA1422
    }
}