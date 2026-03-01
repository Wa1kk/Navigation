namespace IndoorNav.Models;

public class NavNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;

    // Принадлежность
    public string BuildingId { get; set; } = string.Empty;
    public int FloorNumber { get; set; }

    // Позиция в координатах SVG (0–800 × 0–600)
    public float X { get; set; }
    public float Y { get; set; }

    /// <summary>Узел лестницы/лифта — соединяет этажи.</summary>
    public bool IsTransition { get; set; }

    /// <summary>Промежуточная точка коридора — скрывается в пользовательском режиме.</summary>
    public bool IsWaypoint { get; set; }

    /// <summary>
    /// Полигон области аудитории в SVG-координатах.
    /// Каждый элемент — массив [x, y]. Сериализуется как JSON.
    /// Если задан — пользователь может нажать внутрь области, а не только на кружок.
    /// </summary>
    public List<float[]>? Boundary { get; set; }

    public override string ToString() => Name;
}
