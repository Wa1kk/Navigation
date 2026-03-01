namespace IndoorNav.Models;

public class Floor
{
    public int Number { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SvgPath { get; set; } = string.Empty;

    public Floor(int number, string svgPath)
    {
        Number = number;
        Name = number switch
        {
            -1 => "Подвал",
             0 => "Цоколь",
             _ => $"Этаж {number}"
        };
        SvgPath = svgPath;
    }
}
