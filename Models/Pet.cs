using System.Text.Json.Serialization;

namespace pr7_shestakov.Models;

public sealed class Pet
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Species { get; set; } = string.Empty;
    public int Age { get; set; }
    public decimal Weight { get; set; }
    public bool IsAdopted { get; set; }

    [JsonIgnore]
    public string AdoptionStatus => IsAdopted ? "Пристроен" : "Ищет дом";

    public Pet Copy() => new()
    {
        Id = Id,
        Name = Name,
        Species = Species,
        Age = Age,
        Weight = Weight,
        IsAdopted = IsAdopted
    };
}
