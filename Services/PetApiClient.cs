using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using pr7_shestakov.Models;

namespace pr7_shestakov.Services;

public sealed class PetApiClient(HttpClient httpClient) : IPetApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private Uri _serverAddress = new("http://localhost:7193/");

    public void SetServerAddress(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("Укажите адрес сервера в формате http://localhost:7193/ или https://localhost:7193/.");
        }

        // HttpClient.BaseAddress нельзя менять после первого запроса. Используем абсолютные URI.
        _serverAddress = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    public async Task<IReadOnlyList<Pet>> GetAllAsync(string? species, bool? adopted, CancellationToken cancellationToken)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(species))
            query.Add("species=" + Uri.EscapeDataString(species.Trim()));
        if (adopted.HasValue)
            query.Add("adopted=" + (adopted.Value ? "true" : "false"));

        var path = "api/v1/Pet" + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);
        using var response = await httpClient.GetAsync(new Uri(_serverAddress, path), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<List<Pet>>(response, cancellationToken);
    }

    public async Task<Pet> GetAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(RecordUri(id), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<Pet>(response, cancellationToken);
    }

    public async Task CreateAsync(Pet pet, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            new Uri(_serverAddress, "api/v1/Pet"), WriteBody(pet), JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        // В приложенном контроллере POST возвращает 201 без JSON.
    }

    public async Task UpdateAsync(int id, Pet pet, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync(RecordUri(id), WriteBody(pet), JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task PatchAsync(int id, IReadOnlyDictionary<string, object> changes, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, RecordUri(id))
        {
            Content = JsonContent.Create(changes, options: JsonOptions)
        };
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        // PATCH также может вернуть 200 без тела.
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await httpClient.DeleteAsync(RecordUri(id), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private Uri RecordUri(int id)
    {
        if (id <= 0)
            throw new ArgumentException("ID должен быть положительным целым числом.");

        return new Uri(_serverAddress, $"api/v1/Pet/{id}");
    }

    private static object WriteBody(Pet pet) => new
    {
        pet.Name,
        pet.Species,
        pet.Age,
        pet.Weight,
        pet.IsAdopted
    };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                ?? throw new ApiException(response.StatusCode, "Сервер вернул пустые данные вместо записи или списка.");
        }
        catch (JsonException)
        {
            throw new ApiException(response.StatusCode, "Сервер вернул данные неверного формата. Проверьте адрес и модель API.");
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var message = response.StatusCode switch
        {
            HttpStatusCode.BadRequest => "Сервер отклонил данные. Проверьте поля формы.",
            HttpStatusCode.NotFound => "Запись или адрес API не найдены. Проверьте ID и адрес сервера.",
            HttpStatusCode.Unauthorized => "Для обращения к API требуется авторизация.",
            HttpStatusCode.Forbidden => "Сервер запретил выполнение операции.",
            HttpStatusCode.Conflict => "Запись была изменена или конфликтует с данными на сервере.",
            _ => "Сервер не смог выполнить запрос."
        };

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = ReadErrorDetail(body);
        throw new ApiException(response.StatusCode,
            $"{message} HTTP {(int)response.StatusCode}." + (detail.Length > 0 ? $" {detail}" : string.Empty));
    }

    private static string ReadErrorDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return string.Empty;

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var messages = errors.EnumerateObject()
                    .Where(property => property.Value.ValueKind == JsonValueKind.Array)
                    .SelectMany(property => property.Value.EnumerateArray())
                    .Where(value => value.ValueKind == JsonValueKind.String)
                    .Select(value => value.GetString());
                return Limit(string.Join(" ", messages));
            }

            foreach (var key in new[] { "detail", "title", "message" })
                if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                    return Limit(value.GetString() ?? string.Empty);
        }
        catch (JsonException)
        {
            // HTML-страницу ошибки или stack trace в интерфейс не выводим.
        }
        return string.Empty;
    }

    private static string Limit(string text) => text.Length <= 500 ? text : text[..500] + "…";
}
