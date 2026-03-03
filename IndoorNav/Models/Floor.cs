namespace IndoorNav.Models;

public class Floor
{
    public int Number { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SvgPath { get; set; } = string.Empty;

    public Floor(int number, string svgPath)
    {
        Number = number;
        Name = $"Этаж {number}";
        SvgPath = svgPath;
    }
}
