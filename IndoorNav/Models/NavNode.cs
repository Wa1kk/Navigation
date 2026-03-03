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

    /// <summary>Огнетушитель — отображается только в режиме ЧС.</summary>
    public bool IsFireExtinguisher { get; set; }

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

    /// <summary>Узел является аудиторией/помещением — отображается подпись "Аудитория" в попапе пользователя.</summary>
    public bool IsRoom { get; set; }

    /// <summary>Промежуточная точка коридора — скрывается в пользовательском режиме.</summary>
    public bool IsWaypoint { get; set; }

    /// <summary>Подпись внутри кружка точки (пустая = не отображается).</summary>
    public string InnerLabel { get; set; } = string.Empty;

    /// <summary>Доп. ключевые слова для поиска. Не отображаются на карте, учитываются при фильтрации.</summary>
    public string SearchTags { get; set; } = string.Empty;

    /// <summary>Отображаемое имя в пикере поиска: если SearchTags заданы — выводится "Имя · Теги", иначе просто Имя.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(SearchTags)
        ? Name
        : $"{Name} {SearchTags}";

    /// <summary>
    /// Устаревшее поле (один полигон). Хранится только для миграции старых данных — не используйте напрямую.
    /// </summary>
    public List<float[]>? Boundary { get; set; }

    /// <summary>
    /// Список полигонов области аудитории в SVG-координатах.
    /// Каждый полигон — List&lt;float[]&gt;, где float[] = [x, y].
    /// Если задан — пользователь может нажать внутрь области, а не только на кружок.
    /// </summary>
    public List<List<float[]>>? Boundaries { get; set; }

    public override string ToString() => Name;
}
