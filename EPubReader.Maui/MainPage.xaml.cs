using System.Diagnostics;

namespace EPubReader.Maui;

public partial class MainPage : ContentPage
{
    private List<BookItem> _books = new();
    private string _selectedFandom = "";
    private BookItem? _selectedBook = null;

    private const string Unsorted = "Unsorted";
    private const string Uncategorized = "Uncategorized";
    private readonly ILibraryScanner _scanner;


    public MainPage()
    {
        InitializeComponent();
        try
        {
            LibraryData.Load();
            LoadBooks();
            LoadFandoms();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during initialization: {ex}");
            ShowError("Failed to load library", ex.Message);
        }
    }

    public MainPage(ILibraryScanner scanner)
    {
        InitializeComponent();
        _scanner = scanner;
        try
        {
            LibraryData.Load();
            LoadBooks();
            LoadFandoms();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during initialization: {ex}");
            ShowError("Failed to load library", ex.Message);
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

#if ANDROID
        var status = await Permissions.RequestAsync<Permissions.StorageRead>();
        if (status != PermissionStatus.Granted)
        {
            await DisplayAlert("Permission needed", "Storage access is required to read your library.", "OK");
        }
#endif

        LoadBooks();
        LoadFandoms();
    }

    private void LoadBooks()
    {
        try
        {
            var libraryPath = LibraryData.LibraryPath;
            if (string.IsNullOrEmpty(libraryPath))
            {
                _books = new List<BookItem>();
                return;
            }

            var exists = Directory.Exists(libraryPath);
            var dirs = exists ? Directory.GetDirectories(libraryPath) : Array.Empty<string>();

            _books = _scanner.ScanLibrary(libraryPath);

            foreach (var book in _books)
                book.Category = LibraryData.GetCategory(book.FilePath);

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                var details = "";
                foreach (var dir in dirs)
                {
                    var bookDirs = Directory.GetDirectories(dir);
                    var files = Directory.GetFiles(dir);
                    details += $"\n{Path.GetFileName(dir)}/\n";
                    details += $"  subdirs: {string.Join(", ", bookDirs.Select(Path.GetFileName))}\n";
                    details += $"  files: {string.Join(", ", files.Select(Path.GetFileName))}\n";

                    foreach (var bookDir in bookDirs)
                    {
                        var bookFiles = Directory.GetFiles(bookDir);
                        details += $"  {Path.GetFileName(bookDir)}/\n";
                        details += $"    files: {string.Join(", ", bookFiles.Select(Path.GetFileName))}\n";
                    }
                }

                await DisplayAlert("Debug",
                    $"Path: {libraryPath}\nBooks found: {_books.Count}\n{details}",
                    "OK");
            });
        }
        catch (Exception ex)
        {
            _books = new List<BookItem>();
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await DisplayAlert("Error",
                    $"Path: {LibraryData.LibraryPath}\n" +
                    $"Exception: {ex.GetType().Name}: {ex.Message}",
                    "OK");
            });
        }
    }

    private void LoadFandoms()
    {
        try
        {
            var bookFandoms = _books
                .Select(b => string.IsNullOrWhiteSpace(b.Fandom) ? Unsorted : b.Fandom);

            var allKnown = LibraryData.GetAllFandoms()
                .Where(f => !f.Equals(Unsorted, StringComparison.OrdinalIgnoreCase));

            var fandoms = bookFandoms
                .Concat(allKnown)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f == Unsorted ? 1 : 0)
                .ThenBy(f => f)
                .ToList();

            if (!fandoms.Contains(Unsorted))
                fandoms.Add(Unsorted);

            var previousSelection = _selectedFandom;
            FandomList.ItemsSource = fandoms;

            if (!string.IsNullOrEmpty(previousSelection) && fandoms.Contains(previousSelection))
                FandomList.SelectedItem = previousSelection;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading fandoms: {ex}");
        }
    }

    // ── Category sections ─────────────────────────────────────────────────────

    private void RebuildCategorySections(string fandom)
    {
        _selectedBook = null;
        DescriptionPanel.IsVisible = false;

        var fandomBooks = _books.Where(b =>
            fandom == Unsorted
                ? string.IsNullOrWhiteSpace(b.Fandom)
                : b.Fandom == fandom
        ).ToList();

        var sections = fandomBooks
            .GroupBy(b => string.IsNullOrWhiteSpace(b.Category) ? Uncategorized : b.Category,
                     StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key == Uncategorized ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CategorySection
            {
                Name = g.Key,
                Books = g.OrderBy(b => b.SeriesIndex)
                         .ThenBy(b => b.Title)
                         .ToList()
            })
            .ToList();

        BindableLayout.SetItemsSource(CategorySections, null);
        BindableLayout.SetItemTemplate(CategorySections, CreateCategorySectionTemplate());
        BindableLayout.SetItemsSource(CategorySections, sections);

        var total = fandomBooks.Count;
        BookCountText.Text = $"{total} title{(total != 1 ? "s" : "")}  ·  {sections.Count} categor{(sections.Count != 1 ? "ies" : "y")}";
    }

    private DataTemplate CreateCategorySectionTemplate()
    {
        return new DataTemplate(() =>
        {
            var section = new VerticalStackLayout { Margin = new Thickness(0, 0, 0, 32) };

            // Category header row
            var headerStack = new HorizontalStackLayout { Spacing = 12, Margin = new Thickness(0, 0, 0, 12) };

            var accentBar = new BoxView
            {
                WidthRequest = 4,
                HeightRequest = 20,
                CornerRadius = 2,
                Color = Color.FromArgb("#E50914")
            };
            headerStack.Children.Add(accentBar);

            var nameLabel = new Label
            {
                FontSize = 18,
                FontAttributes = FontAttributes.Bold
            };
            nameLabel.SetBinding(Label.TextProperty, "Name");
            nameLabel.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#000000"), Color.FromArgb("#FFFFFF"));
            headerStack.Children.Add(nameLabel);

            var countLabel = new Label
            {
                FontSize = 12,
                VerticalOptions = LayoutOptions.Center
            };
            countLabel.SetBinding(Label.TextProperty, "CountLabel");
            countLabel.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#999999"), Color.FromArgb("#888888"));
            headerStack.Children.Add(countLabel);

            section.Children.Add(headerStack);

            // Horizontal book strip
            var bookScroll = new ScrollView
            {
                Orientation = ScrollOrientation.Horizontal
            };

            var bookCollection = new CollectionView
            {
                SelectionMode = SelectionMode.Single,
                ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Horizontal) { ItemSpacing = 12 },
                HeightRequest = 290,
                ItemTemplate = CreateBookCardTemplate()
            };
            bookCollection.SetBinding(ItemsView.ItemsSourceProperty, "Books");
            bookCollection.SelectionChanged += BookCollection_SelectionChanged;

            bookScroll.Content = bookCollection;
            section.Children.Add(bookScroll);

            return section;
        });
    }

    private DataTemplate CreateBookCardTemplate()
    {
        return new DataTemplate(() =>
        {
            var card = new Border
            {
                WidthRequest = 180,
                HeightRequest = 270,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                StrokeThickness = 1
            };
            card.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#e0e0e0"), Color.FromArgb("#3a3a3a"));

            var grid = new Grid();

            // Cover image
            var coverImage = new Image
            {
                Aspect = Aspect.AspectFill,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            coverImage.SetBinding(Image.SourceProperty, new Binding("CoverImagePath", converter: new BitmapConverter()));
            coverImage.SetBinding(IsVisibleProperty, "HasCover");
            grid.Children.Add(coverImage);

            // No-cover fallback
            var fallback = new Grid { Padding = 16 };
            fallback.SetAppThemeColor(Grid.BackgroundColorProperty, Color.FromArgb("#ffffff"), Color.FromArgb("#2a2a2a"));
            fallback.SetBinding(IsVisibleProperty, new Binding("HasCover", converter: new InvertBoolConverter()));
            fallback.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            fallback.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var titleLabel = new Label
            {
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 4,
                VerticalOptions = LayoutOptions.Center
            };
            titleLabel.SetBinding(Label.TextProperty, "Title");
            titleLabel.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#000000"), Color.FromArgb("#FFFFFF"));
            fallback.Children.Add(titleLabel);

            var infoStack = new VerticalStackLayout();
            Grid.SetRow(infoStack, 1);
            var authorLabel = new Label { FontSize = 11, LineBreakMode = LineBreakMode.TailTruncation };
            authorLabel.SetBinding(Label.TextProperty, "Author");
            authorLabel.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#555555"), Color.FromArgb("#bbbbbb"));
            infoStack.Children.Add(authorLabel);

            var typeLabel = new Label { FontSize = 10, CharacterSpacing = 3 };
            typeLabel.SetBinding(Label.TextProperty, "FileType");
            typeLabel.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#555555"), Color.FromArgb("#bbbbbb"));
            infoStack.Children.Add(typeLabel);
            fallback.Children.Add(infoStack);

            grid.Children.Add(fallback);

            // Overlay for cover images (title/author at bottom)
            var overlay = new Grid
            {
                VerticalOptions = LayoutOptions.End,
                HeightRequest = 80,
                Padding = new Thickness(10, 0, 10, 8)
            };
            overlay.SetBinding(IsVisibleProperty, "HasCover");

            // Gradient-like dark overlay
            overlay.BackgroundColor = new Color(0, 0, 0, 0.6f);

            var overlayStack = new VerticalStackLayout { VerticalOptions = LayoutOptions.End };
            var overlayTitle = new Label
            {
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 2
            };
            overlayTitle.SetBinding(Label.TextProperty, "Title");
            overlayStack.Children.Add(overlayTitle);

            var overlayAuthor = new Label
            {
                FontSize = 10,
                TextColor = Color.FromArgb("#CCCCCC"),
                LineBreakMode = LineBreakMode.TailTruncation
            };
            overlayAuthor.SetBinding(Label.TextProperty, "Author");
            overlayStack.Children.Add(overlayAuthor);
            overlay.Children.Add(overlayStack);
            grid.Children.Add(overlay);

            card.Content = grid;

            // Double-tap to open
            var tapGesture = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
            tapGesture.Tapped += BookCard_DoubleTapped;
            card.GestureRecognizers.Add(tapGesture);

            // Drag to assign fandom
            var dragGesture = new DragGestureRecognizer();
            dragGesture.DragStarting += (s, args) =>
            {
                if (card.BindingContext is BookItem book)
                {
                    args.Data.Properties["BookItem"] = book;
                    args.Data.Text = book.Title;
                }
            };
            card.GestureRecognizers.Add(dragGesture);

            return card;

            return card;
        });
    }

    // ── Fandom list ───────────────────────────────────────────────────────────

    private void FandomList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (FandomList.SelectedItem is string fandom)
            {
                _selectedFandom = fandom;
                SelectedFandomHeader.Text = fandom;
                RebuildCategorySections(fandom);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error selecting fandom: {ex}");
            ShowError("Error loading fandom", ex.Message);
        }
    }

    // ── Add fandom ────────────────────────────────────────────────────────────

    private void AddFandom_Click(object? sender, EventArgs e)
        => CommitNewFandom(NewFandomInput.Text);

    private void NewFandomInput_Completed(object? sender, EventArgs e)
        => CommitNewFandom(NewFandomInput.Text ?? "");

    private void CommitNewFandom(string fandom)
    {
        fandom = fandom.Trim();
        if (string.IsNullOrWhiteSpace(fandom)) return;

        try
        {
            LibraryData.AddStandaloneFandom(fandom);
            NewFandomInput.Text = "";
            LoadFandoms();
            FandomList.SelectedItem = fandom;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error adding standalone fandom: {ex}");
            ShowError("Failed to add fandom", ex.Message);
        }
    }

    // ── Book selection ────────────────────────────────────────────────────────

    private void BookCollection_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.CurrentSelection.FirstOrDefault() is not BookItem book) return;

            _selectedBook = book;

            SelectedBookTitle.Text = book.Title;
            SelectedBookAuthor.Text = $"by {book.Author}";
            SelectedBookType.Text = book.FileType.ToUpperInvariant();
            CategoryInput.Text = book.Category;

            SelectedBookDescription.Text = !string.IsNullOrWhiteSpace(book.Description)
                ? book.Description
                : "No description available.";

            DescriptionPanel.IsVisible = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error selecting book: {ex}");
        }
    }

    // ── Open book ─────────────────────────────────────────────────────────────

    private async void BookCard_DoubleTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (_selectedBook == null) return;

            var ext = Path.GetExtension(_selectedBook.FilePath).ToLowerInvariant();
            if (ext == ".epub")
            {
                // TODO: Navigate to ReaderPage
                await Navigation.PushAsync(new ReaderPage(_selectedBook, _scanner));
            }
            else
            {
                // Open with system default app
                await Launcher.OpenAsync(new OpenFileRequest
                {
                    File = new ReadOnlyFile(_selectedBook.FilePath)
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error opening book: {ex}");
            ShowError("Failed to open book", ex.Message);
        }
    }

    // ── Category ──────────────────────────────────────────────────────────────

    private void SaveCategory_Click(object? sender, EventArgs e)
    {
        try
        {
            if (_selectedBook == null) return;

            var category = CategoryInput.Text?.Trim() ?? "";
            _selectedBook.Category = category;
            LibraryData.SetCategory(_selectedBook.FilePath, category);

            // Remember the selected book before rebuild clears it
            var savedBook = _selectedBook;

            if (!string.IsNullOrEmpty(_selectedFandom))
                RebuildCategorySections(_selectedFandom);

            // Restore selection state so the description panel stays visible
            _selectedBook = savedBook;
            DescriptionPanel.IsVisible = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving category: {ex}");
            ShowError("Failed to save category", ex.Message);
        }
    }

    // Settings
    private async void Settings_Click(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new SettingsPage());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async void ShowError(string title, string detail)
    {
        try
        {
            await DisplayAlert(title, detail, "OK");
        }
        catch (Exception ex) { Debug.WriteLine($"Error showing error dialog: {ex}"); }
    }

    // ── Drag & drop: assign book to fandom ────────────────────────────────────

    // ── Drag & drop: assign book to fandom ────────────────────────────────────

    private void Fandom_DragOver(object? sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;

        // Highlight the fandom border in red
        if (sender is GestureRecognizer recognizer &&
            recognizer.Parent is Border border)
        {
            border.Stroke = Color.FromArgb("#E50914");
            border.StrokeThickness = 2;
        }
    }

    private void Fandom_DragLeave(object? sender, DragEventArgs e)
    {
        // Revert to default border
        if (sender is GestureRecognizer recognizer &&
            recognizer.Parent is Border border)
        {
            ResetFandomBorder(border);
        }
    }

    private void Fandom_Drop(object? sender, DropEventArgs e)
    {
        try
        {
            // Revert highlight on the drop target
            if (sender is GestureRecognizer recognizer &&
                recognizer.Parent is Border border)
            {
                ResetFandomBorder(border);
            }

            if (e.Data?.Properties == null) return;
            if (!e.Data.Properties.ContainsKey("BookItem")) return;
            if (e.Data.Properties["BookItem"] is not BookItem book) return;

            // Resolve which fandom was dropped on
            string? targetFandom = null;
            if (sender is Element element)
            {
                targetFandom = element.BindingContext as string;
            }

            if (string.IsNullOrEmpty(targetFandom)) return;

            // Assign the fandom
            var fandomValue = targetFandom == Unsorted ? "" : targetFandom;
            book.Fandom = fandomValue;
            LibraryData.SetFandom(book.FilePath, fandomValue);

            // Refresh the UI
            LoadFandoms();
            if (!string.IsNullOrEmpty(_selectedFandom))
                RebuildCategorySections(_selectedFandom);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error handling fandom drop: {ex}");
        }
    }

    private static void ResetFandomBorder(Border border)
    {
        border.StrokeThickness = 1;
        // Restore the theme-appropriate default stroke
        border.SetAppThemeColor(Border.StrokeProperty,
            Color.FromArgb("#e0e0e0"),   // Light
            Color.FromArgb("#3a3a3a"));  // Dark
    }

   
}