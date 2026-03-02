using CommunityToolkit.Maui;

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
            });

#if ANDROID
        builder.Services.AddSingleton<ILibraryScanner, AndroidLibraryScanner>();
#else
        builder.Services.AddSingleton<ILibraryScanner, DesktopLibraryScanner>();
#endif

        return builder.Build();
    }
}
