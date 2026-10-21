using System.ComponentModel;
using System.Globalization;
using pr7_shestakov.Infrastructure;
using pr7_shestakov.Models;

namespace pr7_shestakov.ViewModels;

// Числа хранятся в форме как строки: неверный ввод не теряется при привязке.
public sealed class PetEditor : ObservableObject, IDataErrorInfo
{
    private string _name = string.Empty;
    private string _species = string.Empty;
    private string _age = "0";
    private string _weight = "1";
    private bool _isAdopted;

    public int Id { get; }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Species { get => _species; set => SetProperty(ref _species, value); }
    public string Age { get => _age; set => SetProperty(ref _age, value); }
    public string Weight { get => _weight; set => SetProperty(ref _weight, value); }
    public bool IsAdopted { get => _isAdopted; set => SetProperty(ref _isAdopted, value); }

    public PetEditor() { }

    public PetEditor(Pet pet)
    {
        Id = pet.Id;
        Name = pet.Name;
        Species = pet.Species;
        Age = pet.Age.ToString(CultureInfo.CurrentCulture);
        Weight = pet.Weight.ToString(CultureInfo.CurrentCulture);
        IsAdopted = pet.IsAdopted;
    }

    public string this[string columnName] => columnName switch
    {
        nameof(Name) when string.IsNullOrWhiteSpace(Name) => "Введите имя питомца.",
        nameof(Species) when string.IsNullOrWhiteSpace(Species) => "Введите вид питомца.",
        nameof(Age) when !TryGetAge(out _) => "Возраст должен быть целым числом от 0 до 2147483647.",
        nameof(Weight) when !TryGetWeight(out _) => "Вес должен быть числом больше нуля (например, 4,5).",
        _ => string.Empty
    };

    public string Error => string.Join(" ", new[] { nameof(Name), nameof(Species), nameof(Age), nameof(Weight) }
        .Select(property => this[property]).Where(error => error.Length > 0));

    public bool TryCreatePet(out Pet pet)
    {
        pet = new Pet();
        if (Error.Length > 0 || !TryGetAge(out var age) || !TryGetWeight(out var weight))
            return false;

        pet = new Pet
        {
            Id = Id,
            Name = Name.Trim(),
            Species = Species.Trim(),
            Age = age,
            Weight = weight,
            IsAdopted = IsAdopted
        };
        return true;
    }

    public Dictionary<string, object> GetChanges(Pet original)
    {
        if (!TryCreatePet(out var edited))
            throw new InvalidOperationException(Error);

        var changes = new Dictionary<string, object>();
        if (edited.Name != original.Name) changes["name"] = edited.Name;
        if (edited.Species != original.Species) changes["species"] = edited.Species;
        if (edited.Age != original.Age) changes["age"] = edited.Age;
        if (edited.Weight != original.Weight) changes["weight"] = edited.Weight;
        if (edited.IsAdopted != original.IsAdopted) changes["isAdopted"] = edited.IsAdopted;
        return changes;
    }

    private bool TryGetAge(out int age) =>
        int.TryParse(Age, NumberStyles.Integer, CultureInfo.InvariantCulture, out age) && age >= 0;

    private bool TryGetWeight(out decimal weight) =>
        decimal.TryParse(Weight.Trim().Replace(',', '.'),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out weight) && weight > 0;
}
