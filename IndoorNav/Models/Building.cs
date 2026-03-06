namespace IndoorNav.Models;

public class Building
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Физический адрес здания (отображается в пилюле на карте).</summary>
    public string Address { get; set; } = string.Empty;
    public List<Floor> Floors { get; set; } = new();
    /// <summary>Диагностика при невозможности загрузить первый этаж.</summary>
    public string? LoadDiagnostic { get; set; }

    public Building(string id, string name, string address = "")
    {
        Id = id;
        Name = name;
        Address = address;
    }
}
