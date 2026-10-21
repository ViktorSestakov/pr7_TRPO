using pr7_shestakov.Models;

namespace pr7_shestakov.Services;

public interface IPetApiClient
{
    void SetServerAddress(string address);
    Task<IReadOnlyList<Pet>> GetAllAsync(string? species, bool? adopted, CancellationToken cancellationToken);
    Task<Pet> GetAsync(int id, CancellationToken cancellationToken);
    Task CreateAsync(Pet pet, CancellationToken cancellationToken);
    Task UpdateAsync(int id, Pet pet, CancellationToken cancellationToken);
    Task PatchAsync(int id, IReadOnlyDictionary<string, object> changes, CancellationToken cancellationToken);
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
