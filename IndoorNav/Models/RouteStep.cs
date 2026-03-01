namespace IndoorNav.Models;

/// <summary>Один шаг пошагового маршрута, отображаемый пользователю.</summary>
public class RouteStep
{
    public string Text        { get; init; } = string.Empty;
    public string Icon        { get; init; } = "🚶";

    /// <summary>Этаж, который нужно показать на карте при этом шаге.</summary>
    public Floor? TargetFloor { get; init; }
}
