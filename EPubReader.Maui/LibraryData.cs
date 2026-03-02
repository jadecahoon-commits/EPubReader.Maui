using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EPubReader.Maui;

public static class LibraryData
{
    private static readonly string DataFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EPubReader",
        "library-data.json"
    );

    private static Dictionary<string, string> _fandoms = new();
    private static Dictionary<string, string> _categories = new();
    private static HashSet<string> _standaloneFandoms = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, ReadingPosition> _positions = new();
    private static string _theme = "Dark"; 
    private static string _libraryPath = "";

    public class ReadingPosition
    {
        public int Chapter { get; set; } = 0;
        public int Page { get; set; } = 0;
    }

    private class DataRoot
    {
        public Dictionary<string, string> Fandoms { get; set; } = new();
        public Dictionary<string, string> Categories { get; set; } = new();
        public List<string> StandaloneFandoms { get; set; } = new();
        public Dictionary<string, ReadingPosition> Positions { get; set; } = new();
        public string Theme { get; set; } = "Dark";
        public string LibraryPath { get; set; } = "";
    }

    public static string Theme
    {
        get => _theme;
        set { _theme = value; Save(); }
    }

    public static string LibraryPath
    {
        get => _libraryPath;
        set { _libraryPath = value; Save(); }
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(DataFile)) return;

            var json = File.ReadAllText(DataFile);

            try
            {
                var root = JsonSerializer.Deserialize<DataRoot>(json);
                if (root != null)
                {
                    _fandoms = root.Fandoms ?? new();
                    _categories = root.Categories ?? new();
                    _standaloneFandoms = new HashSet<string>(
                        root.StandaloneFandoms ?? new List<string>(),
                        StringComparer.OrdinalIgnoreCase);
                    _positions = root.Positions ?? new();
                    _theme = root.Theme ?? "Dark"; 
                    _libraryPath = root.LibraryPath ?? "";

                    return;
                }
            }
            catch { /* fall through to legacy format */ }

            // Legacy: plain dictionary (fandoms only)
            _fandoms = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            _categories = new();
            _standaloneFandoms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _positions = new();
            _theme = "Dark";
            _libraryPath = "";


        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading library data: {ex}");
            _fandoms = new();
            _categories = new();
            _standaloneFandoms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _positions = new();
            _theme = "Dark";
            _libraryPath = "";

        }
    }

    public static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(DataFile)!;
            Directory.CreateDirectory(dir);

            var root = new DataRoot
            {
                Fandoms = _fandoms,
                Categories = _categories,
                StandaloneFandoms = _standaloneFandoms.OrderBy(f => f).ToList(),
                Positions = _positions,
                Theme = _theme,
                LibraryPath = _libraryPath

            };

            var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(DataFile, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving library data: {ex}");
        }
    }

    // ── Fandoms ───────────────────────────────────────────────────────────────

    public static string GetFandom(string filePath)
    {
        try { return _fandoms.TryGetValue(filePath, out var v) ? v : ""; }
        catch (Exception ex) { Debug.WriteLine($"Error getting fandom: {ex}"); return ""; }
    }

    public static void SetFandom(string filePath, string fandom)
    {
        try { _fandoms[filePath] = fandom; Save(); }
        catch (Exception ex) { Debug.WriteLine($"Error setting fandom: {ex}"); }
    }

    public static void AddStandaloneFandom(string fandom)
    {
        if (string.IsNullOrWhiteSpace(fandom)) return;
        try { _standaloneFandoms.Add(fandom.Trim()); Save(); }
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
        try { _categories[filePath] = category; Save(); }
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
            Save();
        }
        catch (Exception ex) { Debug.WriteLine($"Error setting position: {ex}"); }
    }
}