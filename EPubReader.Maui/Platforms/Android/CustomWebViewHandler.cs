using Android.Webkit;
using Microsoft.Maui.Handlers;

namespace EPubReader.Maui.Platforms.Android.Handlers;

public class CustomWebViewHandler : WebViewHandler
{
    protected override void ConnectHandler(global::Android.Webkit.WebView platformView)
    {
        base.ConnectHandler(platformView);

        // Transparent background - let HTML fully control colors
        platformView.SetBackgroundColor(global::Android.Graphics.Color.Transparent);

        var settings = platformView.Settings;

        // CRITICAL: Disable ALL dark mode interference
        // Must set BOTH for all Android versions

        // API 33+ (Tiramisu): This is the modern way
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Tiramisu)
        {
            settings.AlgorithmicDarkeningAllowed = false;
        }

        // API 29-32: Legacy ForceDark - ALWAYS set this too, even on API 33+
        // Some WebView implementations still check this
#pragma warning disable CA1422
        settings.ForceDark = ForceDarkMode.Off;
#pragma warning restore CA1422

        // Additional settings that can interfere
        settings.SetSupportZoom(false);
        settings.BuiltInZoomControls = false;
    }

    // Override to reapply settings when WebView is updated
    protected override void DisconnectHandler(global::Android.Webkit.WebView platformView)
    {
        base.DisconnectHandler(platformView);
    }
}