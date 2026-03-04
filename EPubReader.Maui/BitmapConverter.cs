using System.Globalization;

namespace EPubReader.Maui;

public class BitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
            return null;

#if ANDROID
        if (path.StartsWith("content://"))
        {
            return ImageSource.FromStream(() => OpenAndroidStream(path));
        }
#endif
        return ImageSource.FromFile(path);
    }

#if ANDROID
    private static Stream? OpenAndroidStream(string path)
    {
        try
        {
            var resolver = Android.App.Application.Context.ContentResolver;
            if (resolver == null) return null;

            var uri = Android.Net.Uri.Parse(path);
            if (uri == null) return null;

            var inputStream = resolver.OpenInputStream(uri);
            if (inputStream == null) return null;

            var mem = new MemoryStream();
            inputStream.CopyTo(mem);
            inputStream.Dispose();
            mem.Position = 0;
            return mem;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BitmapConverter stream error for {path}: {ex.Message}");
            return null;
        }
    }
#endif

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}