namespace IndoorNav.Models;

/// <summary>
/// Describes a single step of the first-launch tutorial overlay.
/// </summary>
public class TutorialStep
{
    /// <summary>Which named element to highlight (x:Name in XAML).</summary>
    public string TargetName { get; set; } = string.Empty;

    /// <summary>Arrow direction: "up", "down", "left", "right".</summary>
    public string ArrowDirection { get; set; } = "down";

    /// <summary>Description text shown below the arrow.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Offset from the target element's center (X, Y) to position the arrow tip.</summary>
    public (double X, double Y) ArrowTipOffset { get; set; }

    /// <summary>Offset for the description label relative to the arrow base.</summary>
    public (double X, double Y) TextOffset { get; set; }
}
