// Create this file: Platforms/Android/Handlers/CustomWebViewHandler.cs

using Android.Webkit;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace EPubReader.Maui.Platforms.Android.Handlers;

public class CustomWebViewHandler : WebViewHandler
{
    protected override void ConnectHandler(global::Android.Webkit.WebView platformView)
    {
        base.ConnectHandler(platformView);

        // Disable Android WebView's automatic dark mode color inversion
        // This prevents the WebView from inverting our carefully chosen colors
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Q)
        {
            platformView.Settings.ForceDark = ForceDarkMode.Off;
        }
    }
}
