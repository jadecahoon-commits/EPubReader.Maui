using System.Diagnostics;

namespace EPubReader.Maui;

public partial class HighlightsPage : ContentPage
{
    private string _filter = "all"; // "all", "favourites", "needs_corrections"

    public HighlightsPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        HighlightData.Load();
        BuildHighlightsList();
    }

    // ── Filters ───────────────────────────────────────────────────────────────

    private void FilterAll_Click(object? sender, EventArgs e) => SetFilter("all");
    private void FilterFavourites_Click(object? sender, EventArgs e) => SetFilter("favourites");
    private void FilterCorrections_Click(object? sender, EventArgs e) => SetFilter("needs_corrections");

    private void SetFilter(string filter)
    {
        _filter = filter;
        UpdateFilterButtons();
        BuildHighlightsList();
    }

    private void UpdateFilterButtons()
    {
        var active = Color.FromArgb("#E50914");
        var inactive = IsAppDark() ? Color.FromArgb("#2a2a2a") : Color.FromArgb("#e0e0e0");
        var activeText = Colors.White;
        var inactiveText = IsAppDark() ? Color.FromArgb("#bbbbbb") : Color.FromArgb("#555555");

        FilterAllButton.BackgroundColor = _filter == "all" ? active : inactive;
        FilterAllButton.TextColor = _filter == "all" ? activeText : inactiveText;

        FilterFavButton.BackgroundColor = _filter == "favourites" ? active : inactive;
        FilterFavButton.TextColor = _filter == "favourites" ? activeText : inactiveText;

        FilterCorrButton.BackgroundColor = _filter == "needs_corrections" ? active : inactive;
        FilterCorrButton.TextColor = _filter == "needs_corrections" ? activeText : inactiveText;
    }

    // ── Build list ────────────────────────────────────────────────────────────

    private void BuildHighlightsList()
    {
        HighlightsStack.Children.Clear();

        try
        {
            var all = HighlightData.GetAllHighlights();
            if (_filter != "all")
                all = all.Where(h => h.Category == _filter).ToList();

            if (all.Count == 0)
            {
                EmptyLabel.IsVisible = true;
                return;
            }

            EmptyLabel.IsVisible = false;

            // Group by book (CalibreKey)
            var grouped = all
                .OrderByDescending(h => h.CreatedUtc)
                .GroupBy(h => h.CalibreKey);

            foreach (var group in grouped)
            {
                var calibreKey = group.Key;
                var parts = calibreKey.Split('/');
                var author = parts.Length >= 1 ? parts[0] : "";
                var title = parts.Length >= 2
                    ? System.Text.RegularExpressions.Regex.Replace(parts[1], @"\s*\(\d+\)$", "")
                    : calibreKey;

                // Book header
                HighlightsStack.Children.Add(new Label
                {
                    Text = title,
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = IsAppDark() ? Colors.White : Colors.Black,
                    Margin = new Thickness(0, 8, 0, 0)
                });

                if (!string.IsNullOrWhiteSpace(author))
                {
                    HighlightsStack.Children.Add(new Label
                    {
                        Text = $"by {author}",
                        FontSize = 12,
                        TextColor = Color.FromArgb("#888888"),
                        Margin = new Thickness(0, 0, 0, 4)
                    });
                }

                // Each highlight in this book
                foreach (var h in group)
                {
                    var isFav = h.Category == "favourites";

                    var card = new Border
                    {
                        Padding = new Thickness(14, 12),
                        BackgroundColor = IsAppDark() ? Color.FromArgb("#1a1a1a") : Color.FromArgb("#ffffff"),
                        Stroke = IsAppDark() ? Color.FromArgb("#2a2a2a") : Color.FromArgb("#e0e0e0"),
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                        StrokeThickness = 1
                    };

                    var cardGrid = new Grid
                    {
                        ColumnDefinitions =
                        {
                            new ColumnDefinition(new GridLength(4)),
                            new ColumnDefinition(GridLength.Star),
                            new ColumnDefinition(GridLength.Auto)
                        },
                        ColumnSpacing = 12
                    };

                    // Left color bar
                    var colorBar = new BoxView
                    {
                        BackgroundColor = isFav ? Color.FromArgb("#3b82f6") : Color.FromArgb("#ef4444"),
                        CornerRadius = 2,
                        VerticalOptions = LayoutOptions.Fill
                    };
                    cardGrid.Add(colorBar, 0, 0);

                    // Text + meta
                    var textStack = new VerticalStackLayout { Spacing = 4 };

                    textStack.Children.Add(new Label
                    {
                        Text = $"\"{TruncateText(h.Text, 200)}\"",
                        FontSize = 14,
                        FontAttributes = FontAttributes.Italic,
                        TextColor = IsAppDark() ? Color.FromArgb("#dddddd") : Color.FromArgb("#333333"),
                        LineBreakMode = LineBreakMode.WordWrap
                    });

                    var metaText = $"Ch. {h.Chapter + 1}  ·  {h.CreatedUtc.ToLocalTime():MMM d, yyyy}";
                    textStack.Children.Add(new Label
                    {
                        Text = metaText,
                        FontSize = 11,
                        TextColor = Color.FromArgb("#888888")
                    });

                    cardGrid.Add(textStack, 1, 0);

                    // Category badge
                    var badge = new Border
                    {
                        Padding = new Thickness(8, 4),
                        BackgroundColor = isFav ? Color.FromArgb("#1e3a5f") : Color.FromArgb("#5f1e1e"),
                        StrokeThickness = 0,
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                        VerticalOptions = LayoutOptions.Start,
                        Content = new Label
                        {
                            Text = isFav ? "★" : "✎",
                            FontSize = 12,
                            TextColor = isFav ? Color.FromArgb("#3b82f6") : Color.FromArgb("#ef4444"),
                            HorizontalOptions = LayoutOptions.Center
                        }
                    };
                    cardGrid.Add(badge, 2, 0);

                    card.Content = cardGrid;
                    HighlightsStack.Children.Add(card);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HighlightsPage.BuildHighlightsList: {ex}");
        }
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private async void Back_Click(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static bool IsAppDark() =>
        Application.Current?.RequestedTheme == AppTheme.Dark;

    private static string TruncateText(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLen) return text;
        return text[..maxLen] + "…";
    }
}
