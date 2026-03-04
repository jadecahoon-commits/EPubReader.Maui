// File: Platforms/Android/CustomWebViewHandler.cs

using Android.Webkit;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace EPubReader.Maui.Platforms.Android.Handlers;

public class CustomWebViewHandler : WebViewHandler
{
    protected override void ConnectHandler(global::Android.Webkit.WebView platformView)
    {
        base.ConnectHandler(platformView);

        // Force dark background on the WebView itself
        platformView.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#0f0f0f"));

        // For Android 13+ (API 33+), use the new AlgorithmicDarkeningAllowed property
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Tiramisu)
        {
            platformView.Settings.AlgorithmicDarkeningAllowed = false;
        }

        // For Android 10-12 (API 29-32), use the legacy ForceDark setting
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Q)
        {
#pragma warning disable CA1422
            platformView.Settings.ForceDark = ForceDarkMode.Off;
#pragma warning restore CA1422
        }
    }
}