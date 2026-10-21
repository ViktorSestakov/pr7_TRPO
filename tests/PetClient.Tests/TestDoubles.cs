using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using pr7_shestakov.Models;
using pr7_shestakov.Services;

namespace PetClient.Tests;

internal sealed record RequestSnapshot(HttpMethod Method, Uri Uri, string? Body, string? ContentType);

internal sealed class RecordingHandler(Func<RequestSnapshot, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    public List<RequestSnapshot> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var snapshot = new RequestSnapshot(request.Method, request.RequestUri!,
            request.Content is null ? null : await request.Content.ReadAsStringAsync(token),
            request.Content?.Headers.ContentType?.MediaType);
        Requests.Add(snapshot);
        return await respond(snapshot, token);
    }

    public static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Encoding.UTF8, "application/json")
    };

    public static HttpResponseMessage Empty(HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(string.Empty)
    };
}

internal sealed class MemoryApi : IPetApiClient
{
    public List<Pet> Data { get; } = [];
    public int ListCalls { get; private set; }
    public int GetCalls { get; private set; }
    public int Creates { get; private set; }
    public int Updates { get; private set; }
    public int Patches { get; private set; }
    public int Deletes { get; private set; }
    public string? LastSpecies { get; private set; }
    public bool? LastAdopted { get; private set; }
    public Dictionary<string, object> LastPatch { get; private set; } = [];
    public Exception? ListError { get; set; }
    public Func<CancellationToken, Task>? BeforeList { get; set; }

    public void SetServerAddress(string address) { }

    public async Task<IReadOnlyList<Pet>> GetAllAsync(string? species, bool? adopted, CancellationToken token)
    {
        ListCalls++;
        LastSpecies = species;
        LastAdopted = adopted;
        if (BeforeList is not null) await BeforeList(token);
        if (ListError is not null) throw ListError;
        return Data.Where(p => string.IsNullOrWhiteSpace(species) || p.Species == species.Trim())
            .Where(p => !adopted.HasValue || p.IsAdopted == adopted.Value).Select(p => p.Copy()).ToList();
    }

    public Task<Pet> GetAsync(int id, CancellationToken token)
    {
        GetCalls++;
        return Task.FromResult(Find(id).Copy());
    }

    public Task CreateAsync(Pet pet, CancellationToken token)
    {
        Creates++;
        var saved = pet.Copy();
        saved.Id = Data.Select(p => p.Id).DefaultIfEmpty().Max() + 1;
        Data.Add(saved);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(int id, Pet pet, CancellationToken token)
    {
        Updates++;
        Data[Data.IndexOf(Find(id))] = pet.Copy();
        return Task.CompletedTask;
    }

    public Task PatchAsync(int id, IReadOnlyDictionary<string, object> changes, CancellationToken token)
    {
        Patches++;
        LastPatch = changes.ToDictionary(pair => pair.Key, pair => pair.Value);
        var pet = Find(id);
        foreach (var (key, value) in changes)
        {
            switch (key)
            {
                case "name": pet.Name = (string)value; break;
                case "species": pet.Species = (string)value; break;
                case "age": pet.Age = (int)value; break;
                case "weight": pet.Weight = (decimal)value; break;
                case "isAdopted": pet.IsAdopted = (bool)value; break;
            }
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken token)
    {
        Deletes++;
        Data.Remove(Find(id));
        return Task.CompletedTask;
    }

    private Pet Find(int id) => Data.FirstOrDefault(p => p.Id == id)
        ?? throw new ApiException(HttpStatusCode.NotFound, "Запись не найдена. HTTP 404.");
}

internal sealed class TestDialogs : IUserDialogService
{
    public bool Answer { get; set; } = true;
    public int Calls { get; private set; }
    public bool ConfirmDelete(Pet pet) { Calls++; return Answer; }
}
