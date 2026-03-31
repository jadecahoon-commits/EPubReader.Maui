using System.Diagnostics;
using System.Text.Json;

namespace EPubReader.Maui;

/// <summary>
/// Manages highlight persistence. Stored as highlights.json in the same
/// directory as library-data.json (follows SaveDataPath / local fallback).
/// </summary>
public static class HighlightData
{
    // ── Model ─────────────────────────────────────────────────────────────────

    public class Highlight
    {
        public string Id { get; set; } = "";
        public string CalibreKey { get; set; } = "";
        public int Chapter { get; set; }
        public string Text { get; set; } = "";
        /// <summary>"favourites" or "needs_corrections"</summary>
        public string Category { get; set; } = "favourites";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }

    private class HighlightsRoot
    {
        public List<Highlight> Highlights { get; set; } = new();
    }

    // ── In-memory state ───────────────────────────────────────────────────────

    private static List<Highlight> _highlights = new();
    private static bool _loaded = false;

    // ── Public API ────────────────────────────────────────────────────────────

    public static void Load()
    {
        try
        {
            var path = GetFilePath();
            if (path == null || !File.Exists(path)) { _highlights = new(); _loaded = true; return; }

            var json = File.ReadAllText(path);
            var root = JsonSerializer.Deserialize<HighlightsRoot>(json);
            _highlights = root?.Highlights ?? new();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HighlightData.Load: {ex}");
            _highlights = new();
        }
        _loaded = true;
    }

    public static void Save()
    {
        try
        {
            var path = GetFilePath();
            if (path == null) return;

            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);

            var root = new HighlightsRoot { Highlights = _highlights };
            var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex) { Debug.WriteLine($"HighlightData.Save: {ex}"); }
    }

    public static Highlight AddHighlight(string calibreKey, int chapter, string text)
    {
        EnsureLoaded();
        var h = new Highlight
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            CalibreKey = calibreKey,
            Chapter = chapter,
            Text = text,
            Category = "favourites",
            CreatedUtc = DateTime.UtcNow
        };
        _highlights.Add(h);
        Save();
        return h;
    }

    public static void ToggleCategory(string highlightId)
    {
        EnsureLoaded();
        var h = _highlights.FirstOrDefault(x => x.Id == highlightId);
        if (h == null) return;
        h.Category = h.Category == "favourites" ? "needs_corrections" : "favourites";
        Save();
    }

    public static List<Highlight> GetHighlights(string calibreKey, int chapter)
    {
        EnsureLoaded();
        return _highlights
            .Where(h => h.CalibreKey == calibreKey && h.Chapter == chapter)
            .ToList();
    }

    public static List<Highlight> GetAllHighlights(string calibreKey)
    {
        EnsureLoaded();
        return _highlights.Where(h => h.CalibreKey == calibreKey).ToList();
    }

    /// <summary>Returns all highlights across all books.</summary>
    public static List<Highlight> GetAllHighlights()
    {
        EnsureLoaded();
        return _highlights.ToList();
    }

    public static void DeleteHighlight(string highlightId)
    {
        EnsureLoaded();
        _highlights.RemoveAll(h => h.Id == highlightId);
        Save();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void EnsureLoaded() { if (!_loaded) Load(); }

    /// <summary>
    /// Returns the path to highlights.json, co-located with library-data.json.
    /// </summary>
    private static string? GetFilePath()
    {
        var saveDataPath = LibraryData.SaveDataPath;

        if (!string.IsNullOrEmpty(saveDataPath) && !saveDataPath.StartsWith("content://"))
            return Path.Combine(saveDataPath, "highlights.json");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EPubReader",
            "highlights.json");
    }
}