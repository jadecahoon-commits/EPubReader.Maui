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
    }

    // ── In-memory state ───────────────────────────────────────────────────────

    private static Dictionary<string, string> _fandoms = new();
    private static Dictionary<string, string> _categories = new();
    private static HashSet<string> _standaloneFandoms = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, ReadingPosition> _positions = new();
    private static string _theme = "Dark";
    private static string _libraryPath = "";
    private static string _saveDataPath = "";

    public class ReadingPosition
    {
        public int Chapter { get; set; } = 0;
        public int Page { get; set; } = 0;
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
            // Immediately write current state to the new location
            SaveData();
        }
    }

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
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading save data: {ex}");
            ResetData();
        }
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
                Theme = _theme
            };

            var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dataFile, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving data: {ex}");
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
    }

    // ── Fandoms ───────────────────────────────────────────────────────────────

    public static string GetFandom(string filePath)
    {
        try { return _fandoms.TryGetValue(filePath, out var v) ? v : ""; }
        catch (Exception ex) { Debug.WriteLine($"Error getting fandom: {ex}"); return ""; }
    }

    public static void SetFandom(string filePath, string fandom)
    {
        try { _fandoms[filePath] = fandom; SaveData(); }
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

    public static string GetCategory(string filePath)
    {
        try { return _categories.TryGetValue(filePath, out var v) ? v : ""; }
        catch (Exception ex) { Debug.WriteLine($"Error getting category: {ex}"); return ""; }
    }

    public static void SetCategory(string filePath, string category)
    {
        try { _categories[filePath] = category; SaveData(); }
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

    public static ReadingPosition? GetPosition(string filePath)
    {
        try { return _positions.TryGetValue(filePath, out var v) ? v : null; }
        catch (Exception ex) { Debug.WriteLine($"Error getting position: {ex}"); return null; }
    }

    public static void SetPosition(string filePath, int chapter, int page)
    {
        try
        {
            _positions[filePath] = new ReadingPosition { Chapter = chapter, Page = page };
            SaveData();
        }
        catch (Exception ex) { Debug.WriteLine($"Error setting position: {ex}"); }
    }
}