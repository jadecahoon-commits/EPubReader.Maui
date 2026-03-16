using System.Diagnostics;

namespace EPubReader.Maui;

public partial class StatsPage : ContentPage
{
    public StatsPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadStats();
    }

    private void LoadStats()
    {
        try
        {
            var stats = LibraryData.GetStats();

            // ── Hero: total time ──────────────────────────────────────────────
            var total = LibraryData.GetTotalReadingSeconds();

            if (total >= 3600)
            {
                var hrs = total / 3600.0;
                TotalTimeLabel.Text = $"{hrs:F1}h";
                TotalTimeSubLabel.Text = "total hours read";
            }
            else
            {
                var mins = total / 60;
                TotalTimeLabel.Text = $"{mins}m";
                TotalTimeSubLabel.Text = "total minutes read";
            }

            // ── Books read ────────────────────────────────────────────────────
            var booksRead = LibraryData.GetBooksReadCount();
            BooksReadLabel.Text = booksRead.ToString();
            BooksReadSubLabel.Text = booksRead == 1 ? "book finished" : "books finished";

            // ── Today ─────────────────────────────────────────────────────────
            var todaySeconds = LibraryData.GetReadingSecondsForDate();
            if (todaySeconds <= 0)
            {
                TodayLabel.Text = "—";
                TodaySubLabel.Text = "no reading today";
            }
            else if (todaySeconds < 3600)
            {
                var mins = todaySeconds / 60;
                TodayLabel.Text = $"{mins}m";
                TodaySubLabel.Text = mins == 1 ? "min today" : "mins today";
            }
            else
            {
                var hrs = todaySeconds / 3600.0;
                TodayLabel.Text = $"{hrs:F1}h";
                TodaySubLabel.Text = "hours today";
            }

            // ── Average per active day ────────────────────────────────────────
            var perDate = stats.SecondsPerDate;
            if (perDate.Count > 0)
            {
                var avgSeconds = perDate.Values.Sum() / (double)perDate.Count;
                if (avgSeconds >= 3600)
                {
                    AvgPerDayLabel.Text = $"{avgSeconds / 3600:F1}h";
                    AvgPerDaySubLabel.Text = "avg per session day";
                }
                else
                {
                    var mins = (long)(avgSeconds / 60);
                    AvgPerDayLabel.Text = $"{mins}m";
                    AvgPerDaySubLabel.Text = "avg per session day";
                }
            }
            else
            {
                AvgPerDayLabel.Text = "—";
                AvgPerDaySubLabel.Text = "avg per session day";
            }

            // ── Top fandom ────────────────────────────────────────────────────
            var topFandom = LibraryData.GetTimePerFandom()
                .Where(kv => !string.IsNullOrEmpty(kv.Key) && kv.Key != "(No Fandom)")
                .OrderByDescending(kv => kv.Value)
                .FirstOrDefault();

            if (topFandom.Key != null)
            {
                TopFandomLabel.Text = topFandom.Key;
                var topSec = topFandom.Value;
                TopFandomSubLabel.Text = topSec >= 3600
                    ? $"{topSec / 3600.0:F1}h spent"
                    : $"{topSec / 60}m spent";
            }
            else
            {
                TopFandomLabel.Text = "—";
                TopFandomSubLabel.Text = "top fandom";
            }

            // ── Fandom bars ───────────────────────────────────────────────────
            BuildFandomBars(stats);

            // ── Daily bars ────────────────────────────────────────────────────
            BuildDailyBars(stats);

            // ── Recently read ─────────────────────────────────────────────────
            BuildRecentBooks(stats);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"StatsPage.LoadStats: {ex}");
        }
    }

    // ── Fandom bar chart ──────────────────────────────────────────────────────

    private void BuildFandomBars(LibraryData.ReadingStats stats)
    {
        FandomBarsStack.Children.Clear();

        var fandoms = stats.SecondsPerFandom
            .Where(kv => !string.IsNullOrEmpty(kv.Key) && kv.Key != "(No Fandom)")
            .OrderByDescending(kv => kv.Value)
            .ToList();

        if (fandoms.Count == 0)
        {
            FandomBarsStack.Children.Add(new Label
            {
                Text = "No fandom data yet.",
                FontSize = 13,
                TextColor = Color.FromArgb("#888888")
            });
            return;
        }

        var maxSeconds = fandoms[0].Value;
        var totalSeconds = fandoms.Sum(f => f.Value);

        foreach (var (fandom, seconds) in fandoms)
        {
            var fraction = maxSeconds > 0 ? (double)seconds / maxSeconds : 0;
            var pct = totalSeconds > 0 ? (double)seconds / totalSeconds * 100 : 0;
            var timeStr = FormatSeconds(seconds);

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(140)),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(60))
                },
                ColumnSpacing = 10
            };

            // Label
            row.Add(new Label
            {
                Text = fandom,
                FontSize = 13,
                TextColor = IsAppDark() ? Color.FromArgb("#dddddd") : Color.FromArgb("#333333"),
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.TailTruncation
            }, 0, 0);

            // Bar background + fill
            var barTrack = new Grid
            {
                BackgroundColor = IsAppDark() ? Color.FromArgb("#2a2a2a") : Color.FromArgb("#eeeeee"),
                HeightRequest = 10,
                VerticalOptions = LayoutOptions.Center
            };
            barTrack.WidthRequest = -1; // stretch

            var barFill = new BoxView
            {
                Color = Color.FromArgb("#E50914"),
                HeightRequest = 10,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Center
            };

            // Animate bar after layout
            barTrack.SizeChanged += (s, e) =>
            {
                barFill.WidthRequest = barTrack.Width * fraction;
            };
            barTrack.Children.Add(barFill);
            row.Add(barTrack, 1, 0);

            // Time label
            row.Add(new Label
            {
                Text = timeStr,
                FontSize = 12,
                TextColor = Color.FromArgb("#888888"),
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center
            }, 2, 0);

            FandomBarsStack.Children.Add(row);
        }
    }

    // ── Daily bar chart ───────────────────────────────────────────────────────

    private void BuildDailyBars(LibraryData.ReadingStats stats)
    {
        DailyBarsStack.Children.Clear();

        var today = DateTime.Now.Date;
        var days = Enumerable.Range(0, 14)
            .Select(i => today.AddDays(-13 + i))
            .ToList();

        var maxSeconds = days
            .Select(d => stats.SecondsPerDate.TryGetValue(d.ToString("yyyy-MM-dd"), out var s) ? s : 0)
            .DefaultIfEmpty(1)
            .Max();

        if (maxSeconds == 0) maxSeconds = 1;

        // Column chart using a horizontal stack of vertical bars
        var barGrid = new HorizontalStackLayout
        {
            Spacing = 4,
            HorizontalOptions = LayoutOptions.Fill
        };

        foreach (var day in days)
        {
            var key = day.ToString("yyyy-MM-dd");
            var seconds = stats.SecondsPerDate.TryGetValue(key, out var s) ? s : 0;
            var fraction = (double)seconds / maxSeconds;
            var isToday = day == today;

            var col = new VerticalStackLayout
            {
                Spacing = 4,
                HorizontalOptions = LayoutOptions.Start,
                WidthRequest = 18
            };

            // Bar
            var barOuter = new Grid
            {
                HeightRequest = 60,
                VerticalOptions = LayoutOptions.End,
                HorizontalOptions = LayoutOptions.Center
            };

            var barBg = new BoxView
            {
                Color = IsAppDark() ? Color.FromArgb("#2a2a2a") : Color.FromArgb("#eeeeee"),
                HeightRequest = 60,
                WidthRequest = 14,
                VerticalOptions = LayoutOptions.End
            };

            var barFill = new BoxView
            {
                Color = isToday ? Color.FromArgb("#FF6B6B") : Color.FromArgb("#E50914"),
                HeightRequest = Math.Max(2, 60 * fraction),
                WidthRequest = 14,
                VerticalOptions = LayoutOptions.End
            };

            barOuter.Children.Add(barBg);
            barOuter.Children.Add(barFill);
            col.Children.Add(barOuter);

            // Day label
            col.Children.Add(new Label
            {
                Text = day.ToString("d").TrimStart('0'),   // e.g. "8", "15"
                FontSize = 9,
                TextColor = isToday
                    ? Color.FromArgb("#E50914")
                    : Color.FromArgb("#888888"),
                HorizontalOptions = LayoutOptions.Center
            });

            barGrid.Children.Add(col);
        }

        DailyBarsStack.Children.Add(barGrid);

        // Max label
        DailyBarsStack.Children.Add(new Label
        {
            Text = $"Peak: {FormatSeconds(maxSeconds)} in one day",
            FontSize = 11,
            TextColor = Color.FromArgb("#666666"),
            HorizontalOptions = LayoutOptions.End
        });
    }

    // ── Recently read ─────────────────────────────────────────────────────────

    private void BuildRecentBooks(LibraryData.ReadingStats stats)
    {
        RecentBooksStack.Children.Clear();

        var recent = stats.ReadHistory
            .SelectMany(kv => kv.Value.Select(dt => (Key: kv.Key, Date: dt)))
            .OrderByDescending(x => x.Date)
            .Take(10)
            .ToList();

        if (recent.Count == 0)
        {
            RecentBooksStack.Children.Add(new Label
            {
                Text = "No books finished yet.",
                FontSize = 13,
                TextColor = Color.FromArgb("#888888")
            });
            return;
        }

        foreach (var (key, date) in recent)
        {
            // CalibreKey format: Author/BookFolder/file.epub
            // Parse out a friendly title & author
            var parts = key.Split('/');
            var author = parts.Length >= 1 ? parts[0] : "";
            var title = parts.Length >= 2
                ? System.Text.RegularExpressions.Regex.Replace(
                    parts[1], @"\s*\(\d+\)$", "")  // strip "(123)" word count
                : key;

            var fandom = LibraryData.GetFandom(key);

            var card = new Border
            {
                Padding = new Thickness(14, 10),
                BackgroundColor = IsAppDark() ? Color.FromArgb("#1a1a1a") : Color.FromArgb("#ffffff"),
                Stroke = IsAppDark() ? Color.FromArgb("#2a2a2a") : Color.FromArgb("#e0e0e0"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                StrokeThickness = 1
            };

            var inner = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                }
            };

            var textStack = new VerticalStackLayout { Spacing = 2 };
            textStack.Children.Add(new Label
            {
                Text = title,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = IsAppDark() ? Colors.White : Colors.Black,
                LineBreakMode = LineBreakMode.TailTruncation
            });
            textStack.Children.Add(new Label
            {
                Text = string.IsNullOrEmpty(fandom) ? author : $"{author}  ·  {fandom}",
                FontSize = 12,
                TextColor = Color.FromArgb("#888888"),
                LineBreakMode = LineBreakMode.TailTruncation
            });

            inner.Add(textStack, 0, 0);
            inner.Add(new Label
            {
                Text = FormatRelativeDate(date),
                FontSize = 11,
                TextColor = Color.FromArgb("#666666"),
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.End
            }, 1, 0);

            card.Content = inner;
            RecentBooksStack.Children.Add(card);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatSeconds(long seconds)
    {
        if (seconds >= 3600)
            return $"{seconds / 3600.0:F1}h";
        if (seconds >= 60)
            return $"{seconds / 60}m";
        return $"{seconds}s";
    }

    private static string FormatRelativeDate(DateTime utcDate)
    {
        var local = utcDate.ToLocalTime();
        var diff = DateTime.Now - local;
        if (diff.TotalDays < 1) return "today";
        if (diff.TotalDays < 2) return "yesterday";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        return local.ToString("MMM d");
    }

    private static bool IsAppDark()
    {
        return Application.Current?.RequestedTheme == AppTheme.Dark
            || LibraryData.Theme == "Dark";
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private async void Back_Click(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }
}
