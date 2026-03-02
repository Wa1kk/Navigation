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

    /// <summary>true — лифт, false — лестница. Актуально только когда IsTransition=true.</summary>
    public bool IsElevator { get; set; }

    /// <summary>Узел выхода (выход из здания).</summary>
    public bool IsExit { get; set; }

    /// <summary>Скрыть точку в пользовательском режиме (admin видит всегда).</summary>
    public bool IsHidden { get; set; }

    /// <summary>Скрыть подпись в пользовательском режиме.</summary>
    public bool IsLabelHidden { get; set; }

    /// <summary>Множитель радиуса точки (дефолт 1.0).</summary>
    public float NodeRadiusScale { get; set; } = 1f;

    /// <summary>Множитель размера текста (дефолт 1.0).</summary>
    public float LabelScale { get; set; } = 1f;

    /// <summary>Цвет точки в hex-формате "RRGGBB" без #. null/пусто = автоматически.</summary>
    public string? NodeColorHex { get; set; }

    /// <summary>Промежуточная точка коридора — скрывается в пользовательском режиме.</summary>
    public bool IsWaypoint { get; set; }

    /// <summary>Подпись внутри кружка точки (пустая = не отображается).</summary>
    public string InnerLabel { get; set; } = string.Empty;

    /// <summary>Доп. ключевые слова для поиска. Не отображаются на карте, учитываются при фильтрации.</summary>
    public string SearchTags { get; set; } = string.Empty;

    /// <summary>
    /// Полигон области аудитории в SVG-координатах.
    /// Каждый элемент — массив [x, y]. Сериализуется как JSON.
    /// Если задан — пользователь может нажать внутрь области, а не только на кружок.
    /// </summary>
    public List<float[]>? Boundary { get; set; }

    public override string ToString() => Name;
}
