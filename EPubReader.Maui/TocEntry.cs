namespace EPubReader.Maui;

public class TocEntry
{
    public string Title { get; set; } = "";
    public int ChapterIndex { get; set; } = -1;
    public int Depth { get; set; } = 0;

    /// <summary>Pixel indent based on nesting depth.</summary>
    public double IndentWidth => Depth * 16.0;
}