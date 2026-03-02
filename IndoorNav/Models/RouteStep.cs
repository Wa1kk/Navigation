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

    /// <summary>Второй узел для зума: если задан вместе с FocusNode — зум на область между двумя точками.</summary>
    public NavNode? FocusNode2 { get; init; }
}
