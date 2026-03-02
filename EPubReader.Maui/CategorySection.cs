using System.Collections.Generic;
using System.Linq;

namespace EPubReader.Maui;

public class CategorySection
{
    public string Name { get; set; } = "";
    public List<BookItem> Books { get; set; } = new();
    public string CountLabel => $"{Books.Count} title{(Books.Count != 1 ? "s" : "")}";
}