namespace EPubReader.Maui;

public partial class ReaderSettingsPage : ContentPage
{
    private string _selectedColor;
    private readonly Action _onSettingsChanged;

    private const string PresetDefault = "#DCDCDC";
    private const string PresetWhite   = "#FFFFFF";
    private const string PresetSepia   = "#C8B89A";
    private const string PresetGreen   = "#B5C9A8";

    public ReaderSettingsPage(Action onSettingsChanged)
    {
        InitializeComponent();
        _onSettingsChanged = onSettingsChanged;

        // Load current values from LibraryData
        _selectedColor = LibraryData.ReaderTextColor;
        FontSizeSlider.Value = LibraryData.ReaderFontSize;
        FontSizeValueLabel.Text = LibraryData.ReaderFontSize.ToString();
        CustomColorEntry.Text = _selectedColor;
        RefreshPresetBorders(_selectedColor);
    }

    // ── Close — save and trigger re-render ────────────────────────────────────

    private async void Close_Click(object? sender, EventArgs e)
    {
        LibraryData.ReaderFontSize = (int)Math.Round(FontSizeSlider.Value);

        var entryHex = CustomColorEntry.Text?.Trim() ?? "";
        if (entryHex.Length == 7 && entryHex.StartsWith('#'))
            _selectedColor = entryHex;
        LibraryData.ReaderTextColor = _selectedColor;

        _onSettingsChanged();

        await Navigation.PopAsync();
    }

    // ── Font size ─────────────────────────────────────────────────────────────

    private void FontSizeSlider_ValueChanged(object? sender, ValueChangedEventArgs e)
    {
        FontSizeValueLabel.Text = ((int)Math.Round(e.NewValue)).ToString();
    }

    // ── Text color ────────────────────────────────────────────────────────────

    private void ColorPreset_Tapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not string hex) return;
        SelectColor(hex);
        CustomColorEntry.Text = hex;
    }

    private void ApplyCustomColor_Click(object? sender, EventArgs e)
    {
        var hex = CustomColorEntry.Text?.Trim() ?? "";
        if (hex.Length == 7 && hex.StartsWith('#'))
            SelectColor(hex);
    }

    private void SelectColor(string hex)
    {
        _selectedColor = hex;
        RefreshPresetBorders(hex);
    }

    private void RefreshPresetBorders(string activeHex)
    {
        var inactive = Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#444444")
            : Color.FromArgb("#cccccc");
        var active = Color.FromArgb("#E50914");

        ColorPreset1.Stroke = activeHex.Equals(PresetDefault, StringComparison.OrdinalIgnoreCase) ? active : inactive;
        ColorPreset2.Stroke = activeHex.Equals(PresetWhite,   StringComparison.OrdinalIgnoreCase) ? active : inactive;
        ColorPreset3.Stroke = activeHex.Equals(PresetSepia,   StringComparison.OrdinalIgnoreCase) ? active : inactive;
        ColorPreset4.Stroke = activeHex.Equals(PresetGreen,   StringComparison.OrdinalIgnoreCase) ? active : inactive;
    }
}
