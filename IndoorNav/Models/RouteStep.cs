namespace IndoorNav.Models;

/// <summary>Один шаг пошагового маршрута, отображаемый пользователю.</summary>
public class RouteStep
{
    public string Text        { get; init; } = string.Empty;
    public string Icon        { get; init; } = "🚶";

    /// <summary>Этаж, который нужно показать на карте при этом шаге.</summary>
    public Floor? TargetFloor { get; init; }

    /// <summary>Узел, на который нужно приблизиться (зум) при отображении шага. null = обычный вид.</summary>
    public NavNode? FocusNode { get; init; }

    /// <summary>Прямоугольная область SVG для зума (bounding box сегмента маршрута). Приоритетнее FocusNode.</summary>
    public (float MinX, float MinY, float MaxX, float MaxY)? FocusRect { get; init; }

    /// <summary>Если задано — показывается в подписи рядом с кнопкой «назад» вместо «Этаж N».</summary>
    public string? FloorLabel { get; init; }
}
