namespace IndoorNav.Models;

public class NavEdge
{
    public string FromId { get; set; } = string.Empty;
    public string ToId { get; set; } = string.Empty;

    /// <summary>Вес рассчитывается автоматически как евклидово расстояние (или задаётся вручную).</summary>
    public float Weight { get; set; }

    /// <summary>Межэтажное ребро (между transition-узлами).</summary>
    public bool IsCrossFloor { get; set; }
}
