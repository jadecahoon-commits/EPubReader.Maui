using CommunityToolkit.Maui;
#if ANDROID
using EPubReader.Maui.Platforms.Android.Handlers;
#endif

namespace EPubReader.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            })

                .ConfigureMauiHandlers(handlers =>
                 {
#if ANDROID
                    handlers.AddHandler<WebView, CustomWebViewHandler>();
#endif
                 });

#if ANDROID
            builder.Services.AddSingleton<ILibraryScanner, AndroidLibraryScanner>();
#else
        builder.Services.AddSingleton<ILibraryScanner, DesktopLibraryScanner>();
#endif

        return builder.Build();
    }
}
