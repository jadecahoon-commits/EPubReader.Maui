using System.Diagnostics;

namespace EPubReader.Maui;

public partial class MainPage : ContentPage
{
    private List<BookItem> _books = new();
    private string _selectedFandom = "";
    private BookItem? _selectedBook = null;
    private Border? _lastSelectedCard = null;
    private List<CategorySection> _currentSections = new();
    private readonly Dictionary<BookItem, Border> _cardViews = new();


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

    public MainPage(ILibraryScanner scanner, string initialFandom)
    {
        InitializeComponent();
        _scanner = scanner;
        _selectedFandom = initialFandom;
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
#if WINDOWS
    HookKeyboard();
#endif


#if ANDROID
        ApplyAndroidLayout();

        var status = await Permissions.RequestAsync<Permissions.StorageRead>();
        if (status != PermissionStatus.Granted)
        {
            await DisplayAlert("Permission needed", "Storage access is required to read your library.", "OK");
        }

        // Ensure GoogleAuthService has loaded its SecureStorage state before
        // LoadBooks checks LibraryFolderId. SettingsPage also calls this, but
        // MainPage can't rely on SettingsPage having been opened first.
        await GoogleAuthService.Instance.InitAsync();
#endif

        LoadBooks();
        LoadFandoms();

        // Auto-select the first fandom so books are visible without a manual tap
        AutoSelectFandom();

        _ = BackgroundDriveSyncAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if WINDOWS
    UnhookKeyboard();
#endif
    }

#if WINDOWS
private Microsoft.UI.Xaml.Window? _winWindow;

private void HookKeyboard()
{
    try
    {
        if (Application.Current?.Windows is { Count: > 0 } wins)
        {
            var nativeWin = wins[0].Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            if (nativeWin != null)
            {
                _winWindow = nativeWin;
                nativeWin.Content.KeyDown += OnWindowKeyDown;
            }
        }
    }
    catch (Exception ex) { Debug.WriteLine($"MainPage.HookKeyboard: {ex.Message}"); }
}

private void UnhookKeyboard()
{
    try
    {
        if (_winWindow != null)
            _winWindow.Content.KeyDown -= OnWindowKeyDown;
    }
    catch { }
}

private void OnWindowKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
{
    if (e.Key == Windows.System.VirtualKey.Left)
    {
        e.Handled = true;
        NavigateBookByKeyboard(-1);
    }
    else if (e.Key == Windows.System.VirtualKey.Right)
    {
        e.Handled = true;
        NavigateBookByKeyboard(+1);
    }
}

private void NavigateBookByKeyboard(int delta)
{
    try
    {
        var allBooks = _currentSections.SelectMany(s => s.Books).ToList();
        if (allBooks.Count == 0) return;

        int currentIndex = _selectedBook != null
            ? allBooks.IndexOf(_selectedBook)
            : -1;

        int nextIndex = currentIndex < 0
            ? (delta > 0 ? 0 : allBooks.Count - 1)
            : Math.Clamp(currentIndex + delta, 0, allBooks.Count - 1);

        if (nextIndex == currentIndex) return;

        var nextBook = allBooks[nextIndex];
        var cv = FindCollectionViewForBook(CategorySections, nextBook);
        if (cv != null)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                // Only call SelectBook directly — setting cv.SelectedItem would
                // trigger SelectionChanged and run the selection logic a second time,
                // leaving a stale red border on the previously-selected card.
                SelectBook(nextBook, cv);
            });
        }
    }
    catch (Exception ex) { Debug.WriteLine($"NavigateBookByKeyboard: {ex.Message}"); }
}

private static CollectionView? FindCollectionViewForBook(VisualElement parent, BookItem target)
{
    if (parent is CollectionView cv &&
        cv.ItemsSource is System.Collections.IEnumerable items &&
        items.Cast<object>().Contains(target))
        return cv;

    if (parent is IVisualTreeElement tree)
    {
        foreach (var child in tree.GetVisualChildren().OfType<VisualElement>())
        {
            var result = FindCollectionViewForBook(child, target);
            if (result != null) return result;
        }
    }
    return null;
}
#endif



    private void LoadBooks()
    {
        try
        {
            // ── Option 1: Drive manifest ──────────────────────────────────────
            // LibraryFolderId is now guaranteed to be loaded (InitAsync called above).
            if (DriveLibraryManifest.Exists() &&
                !string.IsNullOrEmpty(GoogleAuthService.Instance.LibraryFolderId))
            {
                var manifest = DriveLibraryManifest.Load();
                _books = manifest?.ToBookItems() ?? new List<BookItem>();
                Debug.WriteLine($"MainPage: loaded {_books.Count} books from Drive manifest " +
                                $"(last synced: {manifest?.LastSynced:u})");
                return;
            }

            // ── Option 2: Local / SAF library path ───────────────────────────
            var libraryPath = LibraryData.LibraryPath;
            if (string.IsNullOrEmpty(libraryPath))
            {
                _books = new List<BookItem>();
                return;
            }

            _books = _scanner.ScanLibrary(libraryPath);

            foreach (var book in _books)
                book.Category = LibraryData.GetCategory(book.CalibreKey);
        }
        catch (Exception ex)
        {
            _books = new List<BookItem>();
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await DisplayAlert("Error loading library",
                    $"Exception: {ex.GetType().Name}: {ex.Message}", "OK");
            });
        }
    }

    /// <summary>
    /// Runs silently in the background after the page appears.
    /// Re-scans the Drive folder and, if the book list has changed, updates the
    /// local manifest and refreshes the UI.  Never shows errors to the user —
    /// if something fails the stale cache is simply kept.
    /// </summary>
    private async Task BackgroundDriveSyncAsync()
    {
        try
        {
            // Only runs when Drive is the active library source
            if (!DriveLibraryManifest.Exists() ||
                string.IsNullOrEmpty(GoogleAuthService.Instance.LibraryFolderId) ||
                !GoogleAuthService.Instance.IsSignedIn)
                return;

            Debug.WriteLine("BackgroundDriveSync: starting…");

            var freshManifest = await GoogleAuthService.Instance.ScanLibraryFolderAsync();
            if (freshManifest == null)
            {
                Debug.WriteLine("BackgroundDriveSync: scan returned null, keeping cache");
                return;
            }

            // Compare with what we loaded at startup
            var cachedManifest = DriveLibraryManifest.Load();
            var cachedPrint = cachedManifest?.ComputeFingerprint() ?? "";
            var freshPrint = freshManifest.ComputeFingerprint();

            if (cachedPrint == freshPrint)
            {
                Debug.WriteLine("BackgroundDriveSync: no changes detected");
                return;
            }

            Debug.WriteLine(
                $"BackgroundDriveSync: change detected " +
                $"(was {cachedPrint}, now {freshPrint}) — updating cache and UI");

            freshManifest.Save();

            // Refresh the UI on the main thread
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    var newBooks = freshManifest.ToBookItems();
                    _books = newBooks;
                    LoadFandoms();
                    // Re-apply the currently selected fandom so the grid updates
                    if (!string.IsNullOrEmpty(_selectedFandom))
                        RebuildCategorySections(_selectedFandom);
                    else
                        AutoSelectFandom();

                    Debug.WriteLine(
                        $"BackgroundDriveSync: UI refreshed — {_books.Count} books");
                }
                catch (Exception uiEx)
                {
                    Debug.WriteLine($"BackgroundDriveSync UI refresh: {uiEx.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            // Background sync must never crash the app
            Debug.WriteLine($"BackgroundDriveSync: {ex.Message}");
        }
    }

    private void CategoryInput_TextChanged(object? sender, TextChangedEventArgs e)
    {
        try
        {
            var text = e.NewTextValue ?? "";

            // Hide if nothing typed yet
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(_selectedFandom))
            {
                CategorySuggestionsBorder.IsVisible = false;
                return;
            }

            var all = LibraryData.GetCategoriesForFandom(_selectedFandom, Unsorted);
            var matches = all
                .Where(c => c.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0 || (matches.Count == 1 && matches[0].Equals(text, StringComparison.OrdinalIgnoreCase)))
            {
                CategorySuggestionsBorder.IsVisible = false;
                return;
            }

            CategorySuggestions.ItemsSource = matches;
            CategorySuggestionsBorder.IsVisible = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CategoryInput_TextChanged: {ex}");
        }
    }



    private void CategoryInput_Unfocused(object? sender, FocusEventArgs e)
    {
        // Small delay so a tap on a suggestion registers before the list hides
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(200);
            CategorySuggestionsBorder.IsVisible = false;
        });
    }

    private void CategorySuggestion_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.CurrentSelection.FirstOrDefault() is not string chosen) return;
            if (_selectedBook == null) return;

            CategoryInput.Text = chosen;
            _selectedBook.Category = chosen;
            LibraryData.SetCategory(_selectedBook.CalibreKey, chosen);

            CategorySuggestionsBorder.IsVisible = false;
            CategorySuggestions.SelectedItem = null;

            var savedBook = _selectedBook;
            if (!string.IsNullOrEmpty(_selectedFandom))
                RebuildCategorySections(_selectedFandom);

            _selectedBook = savedBook;
            DescriptionPanel.IsVisible = true;
            ResetCategoryRow();

        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CategorySuggestion_SelectionChanged: {ex}");
        }
    }



    private async Task LoadDriveBooksAsync(string folderId)
    {
        try
        {
            // Show a loading state while Drive is being scanned
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _books = new List<BookItem>();
                // Optionally show a spinner / status label here
            });

            var driveBooks = await GoogleAuthService.Instance.ScanLibraryFolderAsync(folderId);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                _books = driveBooks;
                LoadFandoms();
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadDriveBooksAsync error: {ex}");
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _books = new List<BookItem>();
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
            FandomListAndroid.ItemsSource = fandoms;


            if(!string.IsNullOrEmpty(previousSelection) && fandoms.Contains(previousSelection))
            {
                FandomList.SelectedItem = previousSelection;
                FandomListAndroid.SelectedItem = previousSelection;
            }
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
        _lastSelectedCard = null;
        _cardViews.Clear();
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

        _currentSections = sections;

        CategorySections.ItemTemplate = CreateCategorySectionTemplate();
        CategorySections.ItemsSource = sections;

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

            // Horizontal book strip — extra height to allow for scale-up without clipping
            var bookScroll = new ScrollView
            {
                Orientation = ScrollOrientation.Horizontal
            };

            var bookCollection = new CollectionView
            {
                SelectionMode = SelectionMode.None,
                ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Horizontal) { ItemSpacing = 16 },
                HeightRequest = 280,
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
                WidthRequest = 145,
                HeightRequest = 215,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                StrokeThickness = 1,
                // Ensure the card scales from center
                AnchorX = 0.5,
                AnchorY = 0.5,
                // Add margin so the scaled card doesn't clip against neighbours
                Margin = new Thickness(4, 20, 4, 20)
            }; 
            card.SetAppThemeColor(Border.BackgroundColorProperty,
                Color.FromArgb("#ffffff"), Color.FromArgb("#1a1a1a"));

            card.BindingContextChanged += (s, _) =>
            {
                if (card.BindingContext is BookItem book)
                    _cardViews[book] = card;
            };


            var grid = new Grid { InputTransparent = false, BackgroundColor = Colors.Transparent };

            // Cover image — binds to the async-loaded CoverSource, no converter needed
            var coverImage = new Image
            {
                Aspect = Aspect.AspectFill,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            coverImage.SetBinding(Image.SourceProperty, new Binding("CoverSource"));
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

            // Drag handle — sits on top, only this element captures drag
            // InputTransparent = false so it receives touch, but it's transparent visually
            var dragHandle = new Grid
            {
                InputTransparent = false,
                BackgroundColor = Colors.Transparent,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                // Show a subtle drag icon in the top-right corner
            };

            var dragIcon = new Label
            {
                Text = "⠿",
                FontSize = 16,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, 6, 8, 0),
                Opacity = 0.4
            };
            dragIcon.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#555555"), Color.FromArgb("#bbbbbb"));
            dragHandle.Children.Add(dragIcon);

            var dragGesture = new DragGestureRecognizer();
            dragGesture.DragStarting += (s, args) =>
            {
                if (card.BindingContext is BookItem book)
                {
                    args.Data.Properties["BookItem"] = book;
                    args.Data.Text = book.Title;
                }
            };
            dragHandle.GestureRecognizers.Add(dragGesture);

            // Also make the drag handle select the card when tapped (not just dragged)
            var dragTap = new TapGestureRecognizer();
            dragTap.Tapped += (s, args) =>
            {
                // Find the parent CollectionView and set its selected item
                if (card.BindingContext is BookItem book)
                {
                    var parent = card.Parent;
                    while (parent != null && parent is not CollectionView)
                        parent = parent.Parent;
                    if (parent is CollectionView cv)
                        cv.SelectedItem = book;
                }
            };
            dragHandle.GestureRecognizers.Add(dragTap);

            grid.Children.Add(dragHandle);

            // ── Tap to select on Android (CollectionView selection doesn't fire reliably) ──
            var cardTap = new TapGestureRecognizer();
            cardTap.Tapped += (s, args) =>
            {
                if (card.BindingContext is BookItem book)
                {
                    var parent = card.Parent;
                    while (parent != null && parent is not CollectionView)
                        parent = parent.Parent;
                    if (parent is CollectionView cv)
                    {
                        // If the item is already selected, SelectionChanged won't fire,
                        // so manually invoke the description panel logic.
                        if (cv.SelectedItem == book)
                            SelectBook(book, cv);
                        else
                            cv.SelectedItem = book;
                    }
                }
            };
            card.GestureRecognizers.Add(cardTap);

            card.Content = grid;

            return card;
        });
    }


    private void SelectBook(BookItem book, CollectionView cv)
    {
        try
        {
            // Animate previous card back to normal
            if (_lastSelectedCard != null)
            {
                _lastSelectedCard.ScaleTo(1.0, 200, Easing.CubicOut);
                _lastSelectedCard = null;
            }

            _selectedBook = book;

            _cardViews.TryGetValue(book, out var card);
            if (card != null)
            {
                card.ScaleTo(1.2, 250, Easing.CubicOut);
                _lastSelectedCard = card;
            }

            SelectedBookTitle.Text = book.Title;
            SelectedBookAuthor.Text = $"by {book.Author}";
            SelectedBookType.Text = book.FileType.ToUpperInvariant();
            UpdateMarkReadButton(_selectedBook);

            // Read status badge
            var readCount = LibraryData.GetReadCount(book.CalibreKey);
            if (readCount > 0)
            {
                SelectedBookReadLabel.Text = readCount == 1 ? "READ" : $"READ ×{readCount}";
                SelectedBookReadBadge.IsVisible = true;
            }
            else
            {
                SelectedBookReadLabel.Text = "UNREAD";
                SelectedBookReadBadge.BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                    ? Color.FromArgb("#2a2a2a") : Color.FromArgb("#e8e8e8");
                SelectedBookReadLabel.TextColor = Application.Current?.RequestedTheme == AppTheme.Dark
                    ? Color.FromArgb("#888888") : Color.FromArgb("#999999");
                SelectedBookReadBadge.IsVisible = true;
            }

            CategoryInput.Text = book.Category;
            SelectedBookDescription.Text = BuildDescriptionText(book);


            DescriptionPanel.IsVisible = true;

            //Hide the category select
            ResetCategoryRow();

            // Scroll so the selected category row is centred in the viewport
            ScrollToCategoryAsync(book.Category);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error selecting book: {ex}");
        }
    }

    private void ScrollToCategoryAsync(string categoryName)
    {
        try
        {
            var section = _currentSections.FirstOrDefault(s =>
                string.Equals(s.Name, categoryName, StringComparison.OrdinalIgnoreCase));
            if (section == null) return;

            CategorySections.ScrollTo(section, position: ScrollToPosition.Start, animate: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ScrollToCategoryAsync: {ex.Message}");
        }
    }

    // Read counter
    private void UpdateMarkReadButton(BookItem? book)
    {
        if (book == null || MarkReadButton == null) return;

        var count = LibraryData.GetReadCount(book.CalibreKey);
        MarkReadButton.Text = count > 0
            ? $"✓ Read ({count})"
            : "✓ Read";

        // Highlight the button if already read at least once
        MarkReadButton.BackgroundColor = count > 0
            ? Color.FromArgb("#E50914")
            : (Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#2a2a2a")
                : Color.FromArgb("#e8e8e8"));

        MarkReadButton.TextColor = count > 0
            ? Colors.White
            : (Application.Current?.RequestedTheme == AppTheme.Dark
                ? Colors.White
                : Colors.Black);
    }
    private void MarkRead_Click(object? sender, EventArgs e)
    {
        try
        {
            if (_selectedBook == null) return;

            LibraryData.RecordBookRead(_selectedBook.CalibreKey);
            UpdateMarkReadButton(_selectedBook);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MarkRead_Click: {ex}");
        }
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

    // ── Book selection & Netflix-style animation ──────────────────────────────

    private void BookCollection_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            // Animate previous card back to normal
            if (_lastSelectedCard != null)
            {
                _lastSelectedCard.ScaleTo(1.0, 200, Easing.CubicOut);
                _lastSelectedCard = null;
            }

            if (e.CurrentSelection.FirstOrDefault() is not BookItem book) return;

            _selectedBook = book;

            // Find the visual Border for the selected item by walking the CollectionView
            if (sender is CollectionView cv)
            {
                var card = FindCardForItem(cv, book);
                if (card != null)
                {
                    card.ScaleTo(1.2, 250, Easing.CubicOut);
                    _lastSelectedCard = card;
                }
            }

            // Update description panel
            SelectedBookTitle.Text = book.Title;
            SelectedBookAuthor.Text = $"by {book.Author}";
            SelectedBookType.Text = book.FileType.ToUpperInvariant();
            CategoryInput.Text = book.Category;

            SelectedBookDescription.Text = BuildDescriptionText(book);


            DescriptionPanel.IsVisible = true;
            ScrollToCategoryAsync(book.Category);



        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error selecting book: {ex}");
        }
    }

    /// <summary>
    /// Walk the visual tree under a CollectionView to find the Border whose
    /// BindingContext matches the given BookItem.
    /// </summary>
    private static Border? FindCardForItem(VisualElement parent, BookItem target)
    {
        if (parent is Border border && border.BindingContext == target)
            return border;

        foreach (var child in GetVisualChildren(parent))
        {
            var result = FindCardForItem(child, target);
            if (result != null) return result;
        }

        return null;
    }

    private static IEnumerable<VisualElement> GetVisualChildren(VisualElement parent)
    {
        if (parent is IVisualTreeElement treeElement)
        {
            foreach (var child in treeElement.GetVisualChildren())
            {
                if (child is VisualElement ve)
                    yield return ve;
            }
        }
    }

    // ── Open book (double-tap on description panel or via Open button) ────────

    private async void OpenBook_Click(object? sender, EventArgs e)
    {
        await OpenSelectedBookAsync();
    }

    private async Task OpenSelectedBookAsync()
    {
        try
        {
            if (_selectedBook == null) return;

            var ext = Path.GetExtension(_selectedBook.FilePath).ToLowerInvariant();
            if (ext == ".epub")
            {
                await Navigation.PushAsync(new ReaderPage(_selectedBook, _scanner));
            }
            else
            {
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

    private void CategoryToggle_Click(object? sender, EventArgs e)
    {
        ActionButtonsRow.IsVisible = false;
        CategoryEditRow.IsVisible = true;
        CategoryInput.Focus();
    }

    private void ResetCategoryRow()
    {
        CategorySuggestionsBorder.IsVisible = false;
        CategoryEditRow.IsVisible = false;
        ActionButtonsRow.IsVisible = true;
    }

    private void SaveCategory_Click(object? sender, EventArgs e)
    {
        try
        {
            if (_selectedBook == null) return;

            var category = CategoryInput.Text?.Trim() ?? "";
            _selectedBook.Category = category;
            LibraryData.SetCategory(_selectedBook.CalibreKey, category);

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

        // Collapse the category row after saving
        ResetCategoryRow();
    }

    // Settings
    private async void Settings_Click(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new SettingsPage());
    }


    private async void SendToKindle_Click(object? sender, EventArgs e)
{
    var kindleEmail = LibraryData.KindleEmail?.Trim();

    if (string.IsNullOrEmpty(kindleEmail))
    {
        await DisplayAlert("Send to Kindle",
            "No Send-to-Kindle address saved. Add one in Settings.", "OK");
        return;
    }

    if (_selectedBook == null)
    {
        await DisplayAlert("Send to Kindle", "No book selected.", "OK");
        return;
    }

    try
    {
        string? filePath = _selectedBook.FilePath;

        // Resolve gdrive:// paths to a local cached copy first
        if (DriveLibraryManifest.ParseDriveFileId(filePath) != null)
        {
            filePath = await DriveLibraryScanner.ResolveToLocalPathAsync(filePath);
            if (filePath == null)
            {
                await DisplayAlert("Send to Kindle",
                    "Failed to download the book from Google Drive. Check your connection and try again.", "OK");
                return;
            }
        }

#if ANDROID
        await SendToKindleAndroidAsync(filePath, kindleEmail, _selectedBook);
#else
            var message = new EmailMessage
        {
            Subject = Path.GetFileNameWithoutExtension(_selectedBook.FilePath),
            Body = "Sent from your eBook library.",
            To = new List<string> { kindleEmail },
            Attachments = new List<EmailAttachment> { new EmailAttachment(filePath) }
        };
        await Email.Default.ComposeAsync(message);
#endif
    }
    catch (Exception ex)
    {
        await DisplayAlert("Send to Kindle", $"Failed: {ex.Message}", "OK");
    }
}

#if ANDROID
private async Task SendToKindleAndroidAsync(string cachedFilePath, string kindleEmail, BookItem book)
{
    try
    {
        // Derive the original filename from CalibreKey (last segment: "filename.epub")
        var originalFileName = Path.GetFileName(book.CalibreKey);

        // If CalibreKey is unavailable or doesn't look like a filename, fall back to title
        if (string.IsNullOrEmpty(originalFileName) || !originalFileName.Contains('.'))
        {
            var ext = Path.GetExtension(cachedFilePath);
            var safeTitle = string.Concat(book.Title.Select(c =>
                Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            originalFileName = safeTitle + ext;
        }

        // Copy cached file to a friendly-named temp path so the attachment shows correctly
        var friendlyPath = Path.Combine(FileSystem.CacheDirectory, originalFileName);
        File.Copy(cachedFilePath, friendlyPath, overwrite: true);

        var file = new Java.IO.File(friendlyPath);
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
            Android.App.Application.Context,
            $"{Android.App.Application.Context.PackageName}.fileprovider",
            file);

        var intent = new Android.Content.Intent(Android.Content.Intent.ActionSend);
        intent.SetType("application/epub+zip");
        intent.PutExtra(Android.Content.Intent.ExtraEmail, new[] { kindleEmail });
        intent.PutExtra(Android.Content.Intent.ExtraSubject, book.Title);
        intent.PutExtra(Android.Content.Intent.ExtraStream, uri);
        intent.AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission);

        var chooser = Android.Content.Intent.CreateChooser(intent, "Send to Kindle via…");
        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException("No current Activity");
        activity.StartActivity(chooser);
    }
    catch (Exception ex)
    {
        await DisplayAlert("Send to Kindle", $"Failed to open email app: {ex.Message}", "OK");
    }
}
#endif

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

    private void Fandom_DragOver(object? sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;

        if (sender is GestureRecognizer recognizer &&
            recognizer.Parent is Border border)
        {
            border.Stroke = Color.FromArgb("#E50914");
            border.StrokeThickness = 2;
        }
    }

    private void Fandom_DragLeave(object? sender, DragEventArgs e)
    {
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
            if (sender is GestureRecognizer recognizer &&
                recognizer.Parent is Border border)
            {
                ResetFandomBorder(border);
            }

            if (e.Data?.Properties == null) return;
            if (!e.Data.Properties.ContainsKey("BookItem")) return;
            if (e.Data.Properties["BookItem"] is not BookItem book) return;

            string? targetFandom = null;
            if (sender is Element element)
            {
                targetFandom = element.BindingContext as string;
            }

            if (string.IsNullOrEmpty(targetFandom)) return;

            var fandomValue = targetFandom == Unsorted ? "" : targetFandom;
            book.Fandom = fandomValue;
            LibraryData.SetFandom(book.CalibreKey, fandomValue);

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
        border.SetAppThemeColor(Border.StrokeProperty,
            Color.FromArgb("#e0e0e0"),
            Color.FromArgb("#3a3a3a"));
    }

    private void ApplyAndroidLayout()
    {
        // Collapse the sidebar column
        SidebarColumn.Width = new GridLength(0);
        SidebarPanel.IsVisible = false;
        SidebarBorder.IsVisible = false;

        // Show the bottom bar row and the bar itself
        BottomBarRow.Height = new GridLength(64);
        AndroidBottomBar.IsVisible = true;
    }

    // ── Bottom bar button handlers ─────────────────────────────────────────────

    private async void BottomBarFandoms_Click(object? sender, EventArgs e)
    {
        FandomOverlay.IsVisible = true;
        FandomSheet.TranslationY = 500;
        await FandomSheet.TranslateTo(0, 0, 250, Easing.CubicOut);
    }

    private async void BottomBarHome_Click(object? sender, EventArgs e)
    {
        await Navigation.PopToRootAsync();
    }

    private async void BottomBarAddFandom_Click(object? sender, EventArgs e)
    {
        FandomOverlay.IsVisible = true;
        FandomSheet.TranslationY = 500;
        await FandomSheet.TranslateTo(0, 0, 250, Easing.CubicOut);
        await Task.Delay(280);
        NewFandomInputAndroid.Focus();
    }

    // ── Fandom overlay dismiss ────────────────────────────────────────────────

    private async void FandomOverlay_Tapped(object? sender, EventArgs e)
        => await CloseFandomSheetAsync();

    private async void CloseFandomSheet_Click(object? sender, EventArgs e)
        => await CloseFandomSheetAsync();

    private async Task CloseFandomSheetAsync()
    {
        await FandomSheet.TranslateTo(0, 500, 220, Easing.CubicIn);
        FandomOverlay.IsVisible = false;
        FandomSheet.TranslationY = 0;
    }

    // ── Fandom selected in Android sheet ─────────────────────────────────────

    private async void FandomListAndroid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (FandomListAndroid.SelectedItem is string fandom)
            {
                _selectedFandom = fandom;
                SelectedFandomHeader.Text = fandom;
                FandomList.SelectedItem = fandom;
                BottomBarFandomsButton.Text = $"📚  {fandom}";
                RebuildCategorySections(fandom);
                await CloseFandomSheetAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error selecting fandom (Android): {ex}");
            ShowError("Error loading fandom", ex.Message);
        }
    }

    // ── Add fandom from Android sheet ────────────────────────────────────────

    private void AddFandomAndroid_Click(object? sender, EventArgs e)
        => CommitNewFandomAndroid(NewFandomInputAndroid.Text ?? "");

    private void NewFandomInputAndroid_Completed(object? sender, EventArgs e)
        => CommitNewFandomAndroid(NewFandomInputAndroid.Text ?? "");

    private void CommitNewFandomAndroid(string fandom)
    {
        fandom = fandom.Trim();
        if (string.IsNullOrWhiteSpace(fandom)) return;

        try
        {
            LibraryData.AddStandaloneFandom(fandom);
            NewFandomInputAndroid.Text = "";
            LoadFandoms();
            FandomListAndroid.SelectedItem = fandom;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error adding fandom (Android): {ex}");
            ShowError("Failed to add fandom", ex.Message);
        }
    }

    /// <summary>
    /// After LoadFandoms() populates the list, pick a fandom to show books immediately
    /// rather than waiting for the user to tap one.
    /// Prefers the previously-selected fandom, then "Unsorted", then whatever is first.
    /// </summary>
    private void AutoSelectFandom()
    {
        try
        {
            var fandoms = FandomList.ItemsSource as IList<string>;
            if (fandoms == null || fandoms.Count == 0) return;

            string? toSelect = null;

            if (!string.IsNullOrEmpty(_selectedFandom) && fandoms.Contains(_selectedFandom))
                toSelect = _selectedFandom;          // ← honours fandom from HomePage
            else if (fandoms.Contains(Unsorted))
                toSelect = Unsorted;
            else
                toSelect = fandoms[0];

            FandomList.SelectedItem = toSelect;
            FandomListAndroid.SelectedItem = toSelect;
            _selectedFandom = toSelect;
            SelectedFandomHeader.Text = toSelect;
            RebuildCategorySections(toSelect);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AutoSelectFandom: {ex.Message}");
        }
    }

    private static string BuildDescriptionText(BookItem book)
    {
        var desc = !string.IsNullOrWhiteSpace(book.Description)
            ? book.Description
            : "No description available.";
        //return $"{desc}\n\n📁 {book.FilePath}";
        return $"{desc}";
    }
}