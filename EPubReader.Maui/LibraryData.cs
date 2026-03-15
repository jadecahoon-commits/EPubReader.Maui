using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EPubReader.Maui;

public static class LibraryData
{

    // ── Bootstrap file (local only, never synced) ─────────────────────────────
    // Stores just the two paths so we know where to find everything else.

    private static readonly string BootstrapFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EPubReader",
        "bootstrap.json"
    );

    // ── Background Drive upload (debounced) ───────────────────────────────────
    private static CancellationTokenSource? _uploadDebounce;

    private class BootstrapRoot
    {
        public string LibraryPath { get; set; } = "";
        public string SaveDataPath { get; set; } = "";
    }

    // ── Cloud save file (lives at user-chosen SaveDataPath) ───────────────────

    private class DataRoot
    {
        public Dictionary<string, string> Fandoms { get; set; } = new();
        public Dictionary<string, string> Categories { get; set; } = new();
        public List<string> StandaloneFandoms { get; set; } = new();
        public Dictionary<string, ReadingPosition> Positions { get; set; } = new();
        public string Theme { get; set; } = "Dark";
        public string KindleEmail { get; set; } = "";
        public int    ReaderFontSize  { get; set; } = 17;
        public string ReaderTextColor { get; set; } = "#DCDCDC";
        public string ReaderFont { get; set; } = "Georgia";
        public LastReadBookInfo? LastReadBook { get; set; }
        public ReadingStats Stats { get; set; } = new();

    }

    // ── In-memory state ───────────────────────────────────────────────────────

    private static Dictionary<string, string> _fandoms = new();
    private static Dictionary<string, string> _categories = new();
    private static HashSet<string> _standaloneFandoms = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, ReadingPosition> _positions = new();
    private static LastReadBookInfo? _lastReadBook;
    private static ReadingStats _stats = new();

    private static string _theme = "Dark";
    private static string _libraryPath = ""; 
    private static string _saveDataPath = "";
    private static string _kindleEmail = "";
    private static int _readerFontSize = 17;
    private static string _readerTextColor = "#DCDCDC";
    private static string _readerFont = "Georgia";


    public class ReadingPosition
    {
        public int Chapter { get; set; } = 0;
        public int Page { get; set; } = 0;
    }

    public class LastReadBookInfo
    {
        public string CalibreKey { get; set; } = "";
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
        public string? CoverImagePath { get; set; }
        public string? FilePath { get; set; }   // ← ADD THIS

    }

    // ── Public properties ─────────────────────────────────────────────────────

    public static string Theme
    {
        get => _theme;
        set { _theme = value; SaveData(); }
    }

    public static string LibraryPath
    {
        get => _libraryPath;
        set { _libraryPath = value; SaveBootstrap(); }
    }

    public static string SaveDataPath
    {
        get => _saveDataPath;
        set
        {
            _saveDataPath = value;
            SaveBootstrap();
            SaveData();
        }
    }


    public static string KindleEmail
    {
        get => _kindleEmail;
        set { _kindleEmail = value; SaveData(); }
    }

    public static int ReaderFontSize
     {
         get => _readerFontSize;
         set { _readerFontSize = value; SaveData(); }
     }

     public static string ReaderTextColor
     {
         get => _readerTextColor;
         set { _readerTextColor = value; SaveData(); }
     }

    public static string ReaderFont
    {
        get => _readerFont;
        set { _readerFont = value; SaveData(); }
    }
    public static LastReadBookInfo? LastReadBook => _lastReadBook;

    public static void SetLastReadBook(BookItem book)
    {
        _lastReadBook = new LastReadBookInfo
        {
            CalibreKey = book.CalibreKey,
            Title = book.Title,
            Author = book.Author,
            CoverImagePath = book.CoverImagePath,
            FilePath = book.FilePath
        };
        SaveData();

#if ANDROID
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var ctx = Android.App.Application.Context;
                var prefs = ctx.GetSharedPreferences("epubreader_widget", Android.Content.FileCreationMode.Private);
                var editor = prefs?.Edit();
                if (editor == null) return;
                editor.PutString("last_title", book.Title ?? "");
                editor.PutString("last_author", book.Author ?? "");
                editor.PutString("last_file_path", book.FilePath ?? "");

                editor.Commit();

                // Update widget directly — no broadcast needed
                var manager = Android.Appwidget.AppWidgetManager.GetInstance(ctx);
                var component = new Android.Content.ComponentName(ctx, Java.Lang.Class.FromType(typeof(BookWidgetProvider)));
                var ids = manager?.GetAppWidgetIds(component);
                if (ids == null || ids.Length == 0) return;

                foreach (var id in ids)
                    BookWidgetProvider.UpdateWidget(ctx, manager!, id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Widget update failed: {ex.Message}");
            }
        });
#endif
    }


    // ── Calibre key normalization ─────────────────────────────────────────────

    /// <summary>
    /// Builds a platform-independent Calibre key from author, book folder, and filename.
    /// Format: "AuthorName/BookFolder/filename.ext"
    /// All scanners should call this when creating BookItems.
    /// </summary>
    public static string BuildCalibreKey(string author, string bookFolder, string fileName)
    {
        // Normalize separators to forward slash and trim whitespace
        var key = $"{author.Trim()}/{bookFolder.Trim()}/{fileName.Trim()}";
        return key.Replace('\\', '/');
    }

    /// <summary>
    /// Attempts to extract a Calibre-style relative key from a platform-specific path.
    /// Handles Windows paths, content:// URIs, and gdrive:// paths.
    /// Returns the original path if extraction fails (graceful degradation).
    /// </summary>
    public static string NormalizeCalibreKey(string rawPath)
    {
        if (string.IsNullOrEmpty(rawPath)) return rawPath;

        try
        {
            // gdrive:// paths cannot be normalized without manifest context —
            // these should always use BuildCalibreKey at scan time instead.
            if (rawPath.StartsWith("gdrive://")) return rawPath;

            // content:// URIs (Android SAF) — try to extract from the encoded path.
            // Typical SAF URI contains the display path segments URL-encoded.
            if (rawPath.StartsWith("content://"))
            {
                return TryExtractCalibreKeyFromUri(rawPath) ?? rawPath;
            }

            // Regular filesystem path (Windows or Linux):
            // e.g. "C:\Users\...\Calibre Library\Ray Newton\Book Title (17)\file.epub"
            // or   "/home/user/Calibre Library/Ray Newton/Book Title (17)/file.epub"
            // We need the last 3 segments: author/bookFolder/filename
            var normalized = rawPath.Replace('\\', '/');
            var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length >= 3)
            {
                var author = segments[^3];
                var bookFolder = segments[^2];
                var fileName = segments[^1];
                return $"{author}/{bookFolder}/{fileName}";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NormalizeCalibreKey failed for '{rawPath}': {ex.Message}");
        }

        return rawPath;
    }

    /// <summary>
    /// Try to extract author/bookFolder/filename from a content:// URI.
    /// SAF URIs from Google Drive typically encode the path like:
    /// content://com.google.android.apps.docs.storage/document/...%2FAuthor%2FBookFolder%2Ffile.epub
    /// </summary>
    private static string? TryExtractCalibreKeyFromUri(string uri)
    {
        try
        {
            // URL-decode the URI to get the display path segments
            var decoded = Uri.UnescapeDataString(uri);
            // Replace encoded separators and normalize
            decoded = decoded.Replace('\\', '/');

            // Split on '/' and take the last 3 segments
            var segments = decoded.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 3)
            {
                var author = segments[^3];
                var bookFolder = segments[^2];
                var fileName = segments[^1];

                // Sanity check: the last segment should have a file extension
                if (Path.HasExtension(fileName))
                {
                    return $"{author}/{bookFolder}/{fileName}";
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TryExtractCalibreKeyFromUri failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Returns all distinct categories used by books in the given fandom.
    /// Pass the "Unsorted" sentinel to get categories for fandom-less books.
    /// </summary>
    public static List<string> GetCategoriesForFandom(string fandom, string unsortedSentinel = "Unsorted")
    {
        try
        {
            // Build a set of CalibreKeys that belong to this fandom
            var keys = fandom.Equals(unsortedSentinel, StringComparison.OrdinalIgnoreCase)
                ? _fandoms.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key)
                : _fandoms.Where(kv => kv.Value.Equals(fandom, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key);

            var keySet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);

            return _categories
                .Where(kv => keySet.Contains(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => kv.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();
        }
        catch (Exception ex) { Debug.WriteLine($"Error getting categories for fandom: {ex}"); return new(); }
    }


    // -- STATS -------------------------
    public class ReadingStats
    {
        /// <summary>Total seconds the user spent on the ReaderPage across all sessions.</summary>
        public long TotalReadingSeconds { get; set; } = 0;

        /// <summary>
        /// CalibreKey → number of times the user has marked the book as read.
        /// Increments on each "Mark as Read" tap, so re-reads count separately.
        /// </summary>    
        public Dictionary<string, List<DateTime>> ReadHistory { get; set; } = new(StringComparer.OrdinalIgnoreCase);


        /// <summary>Seconds spent reading, bucketed by fandom name.</summary>
        public Dictionary<string, long> SecondsPerFandom { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Seconds spent reading, bucketed by local date ("yyyy-MM-dd").</summary>
        public Dictionary<string, long> SecondsPerDate { get; set; } = new();
    }
    /// <summary>
    /// Call this when the user leaves the ReaderPage. Adds <paramref name="seconds"/>
    /// to the global total and to the per-fandom bucket for the book being read.
    /// </summary>
    public static void RecordReadingSession(string calibreKey, long seconds)
    {
        if (seconds <= 0) return;
        try
        {
            _stats.TotalReadingSeconds += seconds;

            var fandom = GetFandom(calibreKey);
            if (string.IsNullOrWhiteSpace(fandom))
                fandom = "(No Fandom)";

            if (!_stats.SecondsPerFandom.ContainsKey(fandom))
                _stats.SecondsPerFandom[fandom] = 0;
            _stats.SecondsPerFandom[fandom] += seconds;

            var dateKey = DateTime.Now.ToString("yyyy-MM-dd");
            if (!_stats.SecondsPerDate.ContainsKey(dateKey))
                _stats.SecondsPerDate[dateKey] = 0;
            _stats.SecondsPerDate[dateKey] += seconds;

            // SaveData() intentionally removed — caller (FlushAndClose) handles
            // the single disk write that also commits position at book-close.
        }
        catch (Exception ex) { Debug.WriteLine($"Error recording reading session: {ex}"); }
    }

    /// <summary>
    /// Records the current UTC time as a "read" event for this book.
    /// Each call appends a timestamp, so re-reads are fully tracked.
    /// </summary>
    public static void RecordBookRead(string calibreKey)
    {
        if (string.IsNullOrWhiteSpace(calibreKey)) return;
        try
        {
            if (!_stats.ReadHistory.ContainsKey(calibreKey))
                _stats.ReadHistory[calibreKey] = new List<DateTime>();

            _stats.ReadHistory[calibreKey].Add(DateTime.UtcNow);
            SaveData();
        }
        catch (Exception ex) { Debug.WriteLine($"Error recording book read: {ex}"); }
    }

    /// <summary>Returns the raw stats object (for serialisation / future display).</summary>
    public static ReadingStats GetStats() => _stats;

    /// <summary>Total seconds spent on the ReaderPage across all sessions.</summary>
    public static long GetTotalReadingSeconds() => _stats.TotalReadingSeconds;

    /// <summary>
    /// Seconds spent reading on a specific local date (default: today).
    /// Returns 0 if no reading was recorded for that date.
    /// </summary>
    public static long GetReadingSecondsForDate(DateTime? date = null)
    {
        var key = (date ?? DateTime.Now).ToString("yyyy-MM-dd");
        return _stats.SecondsPerDate.TryGetValue(key, out var s) ? s : 0;
    }

    /// <summary>
    /// Returns a defensive copy of the full date → seconds dictionary.
    /// Keys are "yyyy-MM-dd" local date strings.
    /// </summary>
    public static Dictionary<string, long> GetTimePerDate() =>
        new Dictionary<string, long>(_stats.SecondsPerDate);

    /// <summary>
    /// Total number of times any book has been marked read — sum across all books,
    /// so re-reads count (e.g. one book read twice + one book read once = 3).
    /// </summary>
    public static int GetBooksReadCount() =>
        _stats.ReadHistory.Values.Sum(list => list.Count);

    /// <summary>How many times a specific book has been marked read (0 if never).</summary>
    public static int GetReadCount(string calibreKey) =>
        _stats.ReadHistory.TryGetValue(calibreKey, out var list) ? list.Count : 0;

    /// <summary>Whether the user has ever marked this book as read at least once.</summary>
    public static bool HasBeenRead(string calibreKey) =>
        _stats.ReadHistory.TryGetValue(calibreKey, out var list) && list.Count > 0;

    /// <summary>
    /// All UTC timestamps when this book was marked read, oldest first.
    /// Returns an empty list if never read.
    /// </summary>
    public static List<DateTime> GetReadDates(string calibreKey) =>
        _stats.ReadHistory.TryGetValue(calibreKey, out var list)
            ? new List<DateTime>(list)   // defensive copy
            : new List<DateTime>();

    /// <summary>
    /// Fandom → total times books in that fandom have been marked read (re-reads included).
    /// Books with no fandom are bucketed under "(No Fandom)".
    /// </summary>
    public static Dictionary<string, int> GetBooksPerFandom()
    {
        try
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _stats.ReadHistory)
            {
                if (kvp.Value.Count == 0) continue;
                var fandom = _fandoms.TryGetValue(kvp.Key, out var f) && !string.IsNullOrWhiteSpace(f)
                    ? f : "(No Fandom)";
                if (!result.ContainsKey(fandom)) result[fandom] = 0;
                result[fandom] += kvp.Value.Count;
            }
            return result;
        }
        catch (Exception ex) { Debug.WriteLine($"Error computing books per fandom: {ex}"); return new(); }
    }

    

    /// <summary>
    /// Fandom → seconds spent reading books in that fandom.
    /// Returns a defensive copy so callers can't mutate internal state.
    /// </summary>
    public static Dictionary<string, long> GetTimePerFandom() =>
        new Dictionary<string, long>(_stats.SecondsPerFandom, StringComparer.OrdinalIgnoreCase);





    // ── Load ──────────────────────────────────────────────────────────────────

    public static void Load()
    {
        LoadBootstrap();
        LoadData();
    }

    private static void LoadBootstrap()
    {
        try
        {
            if (!File.Exists(BootstrapFile)) return;

            var json = File.ReadAllText(BootstrapFile);
            var root = JsonSerializer.Deserialize<BootstrapRoot>(json);
            if (root == null) return;

            _libraryPath = root.LibraryPath ?? "";
            _saveDataPath = root.SaveDataPath ?? "";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading bootstrap: {ex}");
        }
    }

    private static void LoadData()
    {
        var dataFile = GetDataFilePath();
        if (dataFile == null || !File.Exists(dataFile))
        {
            // No cloud save file yet — attempt to migrate legacy file
            TryMigrateLegacy();
            return;
        }

        try
        {
            var json = File.ReadAllText(dataFile);
            var root = JsonSerializer.Deserialize<DataRoot>(json);
            if (root == null) return;

            _fandoms = root.Fandoms ?? new();
            _categories = root.Categories ?? new();
            _standaloneFandoms = new HashSet<string>(
                root.StandaloneFandoms ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);
            _positions = root.Positions ?? new();
            _theme = root.Theme ?? "Dark"; 
            _kindleEmail = root.KindleEmail ?? "";
            _readerFontSize = root.ReaderFontSize;
            _readerTextColor = root.ReaderTextColor ?? "#DCDCDC";
            _readerFont = root.ReaderFont ?? "Georgia";
            _lastReadBook = root.LastReadBook;
            _stats = root.Stats ?? new();


            // Migrate any old platform-specific keys to normalized Calibre keys
            MigrateKeysToNormalized();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading save data: {ex}");
            ResetData();
        }
    }

    /// <summary>
    /// One-time migration: converts any old platform-specific keys (Windows paths,
    /// content:// URIs) to normalized Calibre keys (Author/BookFolder/file.ext).
    /// Keys that are already normalized or can't be converted are left as-is.
    /// </summary>
    private static void MigrateKeysToNormalized()
    {
        bool changed = false;

        _fandoms = MigrateDictionary(_fandoms, ref changed);
        _categories = MigrateDictionary(_categories, ref changed);
        _positions = MigrateDictionary(_positions, ref changed);

        if (changed)
        {
            Debug.WriteLine("LibraryData: migrated old keys to normalized Calibre keys");
            SaveData();
        }
    }

    private static Dictionary<string, T> MigrateDictionary<T>(
        Dictionary<string, T> source, ref bool changed)
    {
        var result = new Dictionary<string, T>();

        foreach (var kvp in source)
        {
            var normalizedKey = NormalizeCalibreKey(kvp.Key);

            // If the key changed, we're migrating
            if (normalizedKey != kvp.Key)
                changed = true;

            // Use the normalized key; if there's a collision, keep the first one
            if (!result.ContainsKey(normalizedKey))
                result[normalizedKey] = kvp.Value;
        }

        return result;
    }

    /// <summary>
    /// Migrate from the old all-in-one library-data.json if it exists,
    /// so existing users don't lose their data.
    /// </summary>
    private static void TryMigrateLegacy()
    {
        var legacyFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EPubReader",
            "library-data.json"
        );

        if (!File.Exists(legacyFile)) return;

        try
        {
            var json = File.ReadAllText(legacyFile);

            // Legacy DataRoot included LibraryPath and Theme
            var legacyRoot = JsonSerializer.Deserialize<LegacyDataRoot>(json);
            if (legacyRoot == null) return;

            _fandoms = legacyRoot.Fandoms ?? new();
            _categories = legacyRoot.Categories ?? new();
            _standaloneFandoms = new HashSet<string>(
                legacyRoot.StandaloneFandoms ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);
            _positions = legacyRoot.Positions ?? new();
            _theme = legacyRoot.Theme ?? "Dark";

            // Preserve library path from legacy file if not already set
            if (string.IsNullOrEmpty(_libraryPath) && !string.IsNullOrEmpty(legacyRoot.LibraryPath))
                _libraryPath = legacyRoot.LibraryPath;

            Debug.WriteLine("Migrated legacy library-data.json");

            // Normalize keys during legacy migration as well
            MigrateKeysToNormalized();

            // Write to new locations
            SaveBootstrap();
            SaveData();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error migrating legacy data: {ex}");
        }
    }

    private class LegacyDataRoot
    {
        public Dictionary<string, string> Fandoms { get; set; } = new();
        public Dictionary<string, string> Categories { get; set; } = new();
        public List<string> StandaloneFandoms { get; set; } = new();
        public Dictionary<string, ReadingPosition> Positions { get; set; } = new();
        public string Theme { get; set; } = "Dark";
        public string LibraryPath { get; set; } = "";
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    private static void SaveBootstrap()
    {
        try
        {
            var dir = Path.GetDirectoryName(BootstrapFile)!;
            Directory.CreateDirectory(dir);

            var root = new BootstrapRoot
            {
                LibraryPath = _libraryPath,
                SaveDataPath = _saveDataPath
            };

            var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(BootstrapFile, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving bootstrap: {ex}");
        }
    }

    private static void SaveData()
    {
        var dataFile = GetDataFilePath();
        if (dataFile == null)
        {
            Debug.WriteLine("SaveData skipped: no SaveDataPath configured.");
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(dataFile)!;
            Directory.CreateDirectory(dir);

            var root = new DataRoot
            {
                Fandoms = _fandoms,
                Categories = _categories,
                StandaloneFandoms = _standaloneFandoms.OrderBy(f => f).ToList(),
                Positions = _positions,
                Theme = _theme,
                KindleEmail = _kindleEmail,
                ReaderFontSize = _readerFontSize,
                ReaderTextColor = _readerTextColor,
                ReaderFont = _readerFont,
                LastReadBook = _lastReadBook,
                Stats = _stats


            };

            var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dataFile, json);

            // Fire-and-forget upload to Drive, debounced so rapid saves coalesce
            _ = ScheduleDriveUploadAsync(dataFile);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving data: {ex}");
        }
    }

    /// <summary>
    /// Waits 2 seconds after the last save, then uploads to Drive in the background.
    /// Multiple rapid saves (e.g. reading position updates) coalesce into one upload.
    /// </summary>
    private static async Task ScheduleDriveUploadAsync(string localFile)
    {
        _uploadDebounce?.Cancel();
        _uploadDebounce = new CancellationTokenSource();
        var token = _uploadDebounce.Token;

        try
        {
            await Task.Delay(2000, token);

            if (!GoogleAuthService.Instance.IsSignedIn) return;
            if (!File.Exists(localFile)) return;

            var ok = await GoogleAuthService.Instance.UploadLibraryDataAsync(localFile);
            Debug.WriteLine(ok
                ? "LibraryData: background Drive upload succeeded"
                : "LibraryData: background Drive upload failed");
        }
        catch (TaskCanceledException)
        {
            // A newer save came in — this upload was superseded, that's fine
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LibraryData: background Drive upload error: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the full path to library-data.json, or null if SaveDataPath is not set.
    /// Falls back to LocalApplicationData if SaveDataPath is empty, so the app
    /// works out of the box before the user configures a sync folder.
    /// </summary>
    private static string? GetDataFilePath()
    {
        if (!string.IsNullOrEmpty(_saveDataPath))
        {
            // On Android the path may be a content:// URI — we can't write a plain
            // file there, so fall back to local storage in that case.
            if (_saveDataPath.StartsWith("content://"))
                return GetLocalFallbackDataFile();

            return Path.Combine(_saveDataPath, "library-data.json");
        }

        // No sync folder chosen yet — use local fallback so app works immediately.
        return GetLocalFallbackDataFile();
    }

    private static string GetLocalFallbackDataFile() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EPubReader",
            "library-data.json"
        );

    // ── Public save method (called by anything that mutates data) ─────────────

    public static void Save() => SaveData();

    // ── Reset helpers ─────────────────────────────────────────────────────────

    private static void ResetData()
    {
        _fandoms = new();
        _categories = new();
        _standaloneFandoms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _positions = new();
        _theme = "Dark";
        _kindleEmail = "";
    }

    // ── Fandoms ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets the fandom for a book. The key should be a CalibreKey
    /// (Author/BookFolder/filename.ext).
    /// </summary>
    public static string GetFandom(string calibreKey)
    {
        try { return _fandoms.TryGetValue(calibreKey, out var v) ? v : ""; }
        catch (Exception ex) { Debug.WriteLine($"Error getting fandom: {ex}"); return ""; }
    }

    public static void SetFandom(string calibreKey, string fandom)
    {
        try { _fandoms[calibreKey] = fandom; SaveData(); }
        catch (Exception ex) { Debug.WriteLine($"Error setting fandom: {ex}"); }
    }

    public static void AddStandaloneFandom(string fandom)
    {
        if (string.IsNullOrWhiteSpace(fandom)) return;
        try { _standaloneFandoms.Add(fandom.Trim()); SaveData(); }
        catch (Exception ex) { Debug.WriteLine($"Error adding standalone fandom: {ex}"); }
    }

    public static List<string> GetAllFandoms()
    {
        try
        {
            return _fandoms.Values
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Concat(_standaloneFandoms)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f)
                .ToList();
        }
        catch (Exception ex) { Debug.WriteLine($"Error getting all fandoms: {ex}"); return new(); }
    }

    // ── Categories ────────────────────────────────────────────────────────────

    public static string GetCategory(string calibreKey)
    {
        try { return _categories.TryGetValue(calibreKey, out var v) ? v : ""; }
        catch (Exception ex) { Debug.WriteLine($"Error getting category: {ex}"); return ""; }
    }

    public static void SetCategory(string calibreKey, string category)
    {
        try { _categories[calibreKey] = category; SaveData(); }
        catch (Exception ex) { Debug.WriteLine($"Error setting category: {ex}"); }
    }

    public static List<string> GetAllCategories()
    {
        try
        {
            return _categories.Values
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();
        }
        catch (Exception ex) { Debug.WriteLine($"Error getting all categories: {ex}"); return new(); }
    }

    // ── Reading Positions ─────────────────────────────────────────────────────

    public static ReadingPosition? GetPosition(string calibreKey)
    {
        try { return _positions.TryGetValue(calibreKey, out var v) ? v : null; }
        catch (Exception ex) { Debug.WriteLine($"Error getting position: {ex}"); return null; }
    }

    public static void SetPosition(string calibreKey, int chapter, int page)
    {
        try
        {
            _positions[calibreKey] = new ReadingPosition { Chapter = chapter, Page = page };
            // No SaveData() here — caller is responsible for flushing at close time.
        }
        catch (Exception ex) { Debug.WriteLine($"Error setting position: {ex}"); }
    }
}