using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using pr7_shestakov.Models;
using pr7_shestakov.Services;
using pr7_shestakov.ViewModels;

namespace PetClient.Tests;

internal static class Program
{
    private static readonly CancellationToken Token = CancellationToken.None;

    public static async Task<int> Main()
    {
        (string Name, Func<Task> Run)[] tests =
        [
            ("GET list deserializes the API model", ListContract),
            ("Filters encode species and preserve false/true/null", FilterContract),
            ("GET by ID and invalid IDs", GetContract),
            ("POST sends camelCase typed JSON and accepts empty 201", PostContract),
            ("PUT sends all editable fields", PutContract),
            ("PATCH sends only supplied fields and accepts empty 200", PatchContract),
            ("DELETE accepts empty 204", DeleteContract),
            ("HTTP errors expose status and server validation details", HttpErrors),
            ("Malformed and null JSON are reported", InvalidJson),
            ("Server address can change after the first request", ServerAddress),
            ("Form validates types and Russian decimal separator", FormValidation),
            ("PATCH includes false and zero but excludes unchanged fields", PatchDiff),
            ("ViewModel loads data and edits a copy through bindings", ViewModelLoad),
            ("GET by ID opens a record outside current filters", ViewModelGet),
            ("Invalid ID makes no request", ViewModelInvalidId),
            ("Creating a record refreshes the list", ViewModelCreate),
            ("Invalid form makes no write request", ViewModelInvalidForm),
            ("PUT refreshes the saved record", ViewModelPut),
            ("PATCH applies active server filters after saving", ViewModelPatch),
            ("PATCH without changes makes no request", ViewModelNoChanges),
            ("DELETE requires confirmation and refreshes the list", ViewModelDelete),
            ("Reset removes both server filters", ViewModelReset),
            ("Network error is readable and retry works", ViewModelNetwork),
            ("Timeout is readable", ViewModelTimeout),
            ("Successful write is preserved if refresh fails", ViewModelRefreshFailure),
            ("Server change clears old selection", ViewModelServerChange),
            ("Busy state blocks overlapping actions and supports cancellation", BusyAndCancellation)
        ];
        var failures = 0;
        foreach (var (name, run) in tests)
        {
            try { await run(); Console.WriteLine($"PASS {name}"); }
            catch (Exception exception) { failures++; Console.Error.WriteLine($"FAIL {name}\n{exception}"); }
        }
        Console.WriteLine($"{tests.Length - failures}/{tests.Length} checks passed.");
        return failures == 0 ? 0 : 1;
    }

    private static Pet Sample(int id = 1) => new()
    {
        Id = id, Name = "Барсик", Species = "Кошка", Age = 3, Weight = 4.5m, IsAdopted = false
    };

    private static RecordingHandler Handler(Func<RequestSnapshot, HttpResponseMessage> response) =>
        new((request, _) => Task.FromResult(response(request)));

    private static async Task ListContract()
    {
        using var handler = Handler(_ => RecordingHandler.Json(new[] { Sample() }));
        using var http = new HttpClient(handler);
        var pets = await new PetApiClient(http).GetAllAsync(null, null, Token);
        Equal(1, pets.Count); Equal("Барсик", pets[0].Name); Equal(4.5m, pets[0].Weight);
        Equal("Ищет дом", pets[0].AdoptionStatus);
        Equal(HttpMethod.Get, handler.Requests[0].Method);
        Equal("/api/v1/Pet", handler.Requests[0].Uri.PathAndQuery);
    }

    private static async Task FilterContract()
    {
        using var handler = Handler(_ => RecordingHandler.Json(Array.Empty<Pet>()));
        using var http = new HttpClient(handler);
        var api = new PetApiClient(http);
        await api.GetAllAsync(" Кошка & кот ", false, Token);
        var uri = handler.Requests[0].Uri;
        Check(uri.Query.Contains("%26"), "Species must be URL-escaped.");
        Check(Uri.UnescapeDataString(uri.Query).Contains("species=Кошка & кот&adopted=false"));
        await api.GetAllAsync(null, true, Token);
        Equal("?adopted=true", handler.Requests[1].Uri.Query);
        await api.GetAllAsync(" ", null, Token);
        Equal(string.Empty, handler.Requests[2].Uri.Query);
    }

    private static async Task GetContract()
    {
        using var handler = Handler(_ => RecordingHandler.Json(Sample(7)));
        using var http = new HttpClient(handler);
        var api = new PetApiClient(http);
        Equal(7, (await api.GetAsync(7, Token)).Id);
        Equal("/api/v1/Pet/7", handler.Requests.Single().Uri.AbsolutePath);
        await Throws<ArgumentException>(() => api.GetAsync(0, Token));
        await Throws<ArgumentException>(() => api.GetAsync(-1, Token));
        Equal(1, handler.Requests.Count);
    }

    private static async Task PostContract()
    {
        using var handler = Handler(_ => RecordingHandler.Empty(HttpStatusCode.Created));
        using var http = new HttpClient(handler);
        await new PetApiClient(http).CreateAsync(Sample(), Token);
        var request = handler.Requests.Single();
        Equal(HttpMethod.Post, request.Method); Equal("/api/v1/Pet", request.Uri.AbsolutePath);
        Equal("application/json", request.ContentType);
        using var json = JsonDocument.Parse(request.Body!);
        var root = json.RootElement;
        Equal(5, root.EnumerateObject().Count());
        Equal("Барсик", root.GetProperty("name").GetString());
        Equal("Кошка", root.GetProperty("species").GetString());
        Equal(3, root.GetProperty("age").GetInt32());
        Equal(4.5m, root.GetProperty("weight").GetDecimal());
        Check(!root.GetProperty("isAdopted").GetBoolean());
    }

    private static async Task PutContract()
    {
        using var handler = Handler(_ => RecordingHandler.Json(Sample(8)));
        using var http = new HttpClient(handler);
        await new PetApiClient(http).UpdateAsync(8, Sample(8), Token);
        var request = handler.Requests.Single();
        Equal(HttpMethod.Put, request.Method); Equal("/api/v1/Pet/8", request.Uri.AbsolutePath);
        using var body = JsonDocument.Parse(request.Body!);
        Equal(5, body.RootElement.EnumerateObject().Count());
    }

    private static async Task PatchContract()
    {
        using var handler = Handler(_ => RecordingHandler.Empty());
        using var http = new HttpClient(handler);
        await new PetApiClient(http).PatchAsync(8, new Dictionary<string, object> { ["isAdopted"] = false }, Token);
        var request = handler.Requests.Single();
        Equal(HttpMethod.Patch, request.Method); Equal("/api/v1/Pet/8", request.Uri.AbsolutePath);
        using var body = JsonDocument.Parse(request.Body!);
        Equal(1, body.RootElement.EnumerateObject().Count());
        Check(!body.RootElement.GetProperty("isAdopted").GetBoolean());
    }

    private static async Task DeleteContract()
    {
        using var handler = Handler(_ => RecordingHandler.Empty(HttpStatusCode.NoContent));
        using var http = new HttpClient(handler);
        await new PetApiClient(http).DeleteAsync(9, Token);
        Equal(HttpMethod.Delete, handler.Requests.Single().Method);
        Equal("/api/v1/Pet/9", handler.Requests.Single().Uri.AbsolutePath);
    }

    private static async Task HttpErrors()
    {
        foreach (var status in new[] { HttpStatusCode.BadRequest, HttpStatusCode.NotFound, HttpStatusCode.InternalServerError })
        {
            using var handler = Handler(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent("{\"errors\":{\"Name\":[\"Name is required\"]}}")
            });
            using var http = new HttpClient(handler);
            var error = await Throws<ApiException>(() => new PetApiClient(http).GetAsync(1, Token));
            Equal(status, error.StatusCode);
            Check(error.Message.Contains(((int)status).ToString(CultureInfo.InvariantCulture)));
            Check(error.Message.Contains("Name is required"));
        }
    }

    private static async Task InvalidJson()
    {
        foreach (var body in new[] { "<html>error</html>", "null", "{\"id\":\"wrong\"}" })
        {
            using var handler = Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
            using var http = new HttpClient(handler);
            await Throws<ApiException>(() => new PetApiClient(http).GetAllAsync(null, null, Token));
        }
    }

    private static async Task ServerAddress()
    {
        using var handler = Handler(_ => RecordingHandler.Json(Array.Empty<Pet>()));
        using var http = new HttpClient(handler);
        var api = new PetApiClient(http);
        await api.GetAllAsync(null, null, Token);
        api.SetServerAddress("https://localhost:8001");
        await api.GetAllAsync(null, null, Token);
        Equal("https://localhost:8001/api/v1/Pet", handler.Requests[1].Uri.AbsoluteUri);
        foreach (var invalid in new[] { "", "localhost:7193", "ftp://server/", "https://server/?q=1", "https://server/#x", "https://u:p@server/" })
            await Throws<ArgumentException>(() => { api.SetServerAddress(invalid); return Task.CompletedTask; });
    }

    private static Task FormValidation()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            var editor = new PetEditor(Sample()) { Name = " Барсик ", Weight = "2,75" };
            Check(editor.TryCreatePet(out var pet)); Equal(2.75m, pet.Weight); Equal("Барсик", pet.Name);
            editor.Weight = "2.75"; Check(editor.TryCreatePet(out pet)); Equal(2.75m, pet.Weight);
            foreach (var invalid in new[] { "abc", "0", "-1", "1.234,5", "NaN", "1e2", "" })
            { editor.Weight = invalid; Check(!editor.TryCreatePet(out _), invalid); }
            editor.Weight = "1";
            foreach (var invalid in new[] { "-1", "2.5", "cat", "2147483648" })
            { editor.Age = invalid; Check(!editor.TryCreatePet(out _), invalid); }
            editor.Age = "0"; Check(editor.TryCreatePet(out _));
            editor.Name = " "; Check(!editor.TryCreatePet(out _));
            editor.Name = "Кот"; editor.Species = " "; Check(!editor.TryCreatePet(out _));
        }
        finally { CultureInfo.CurrentCulture = previous; }
        return Task.CompletedTask;
    }

    private static Task PatchDiff()
    {
        var pet = Sample(); pet.IsAdopted = true;
        var editor = new PetEditor(pet);
        Equal(0, editor.GetChanges(pet).Count);
        editor.Age = "0"; editor.IsAdopted = false;
        var changes = editor.GetChanges(pet);
        Equal(2, changes.Count); Equal(0, (int)changes["age"]); Equal(false, (bool)changes["isAdopted"]);
        return Task.CompletedTask;
    }

    private static (MainViewModel Vm, MemoryApi Api, TestDialogs Dialogs) Fixture()
    {
        var api = new MemoryApi(); api.Data.Add(Sample());
        var dialogs = new TestDialogs();
        return (new MainViewModel(api, dialogs), api, dialogs);
    }

    private static async Task ViewModelLoad()
    {
        var (vm, api, _) = Fixture(); using var lifetime = vm;
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        await vm.InitializeAsync(); await vm.InitializeAsync();
        Equal(1, api.ListCalls); Equal(1, vm.Pets.Count); Check(vm.IsConnected);
        vm.SelectedPet = vm.Pets[0]; Equal("Барсик", vm.Editor.Name); Check(vm.IsEditing);
        vm.Editor.Name = "Новое имя"; Equal("Барсик", vm.Pets[0].Name);
        Check(notifications.Contains(nameof(vm.Editor))); Check(notifications.Contains(nameof(vm.IsBusy)));
    }

    private static async Task ViewModelGet()
    {
        var (vm, api, _) = Fixture(); using var lifetime = vm;
        await vm.InitializeAsync(); vm.SpeciesFilter = "Собака"; await vm.ApplyFiltersCommand.ExecuteAsync();
        Equal(0, vm.Pets.Count); vm.LookupId = "1"; await vm.GetByIdCommand.ExecuteAsync();
        Equal(1, api.GetCalls); Equal(1, vm.Editor.Id); Equal(0, vm.Pets.Count); Check(vm.IsEditing);
        vm.LookupId = "999"; await vm.GetByIdCommand.ExecuteAsync(); Check(vm.ErrorMessage.Contains("404"));
    }

    private static async Task ViewModelInvalidId()
    {
        var (vm, api, _) = Fixture(); using var lifetime = vm;
        foreach (var id in new[] { "abc", "0", "-1", "1.5" })
        { vm.LookupId = id; await vm.GetByIdCommand.ExecuteAsync(); Check(vm.HasError); }
        Equal(0, api.GetCalls);
    }

    private static async Task ViewModelCreate()
    {
        var (vm, api, _) = Fixture(); using var lifetime = vm;
        await vm.InitializeAsync(); vm.Editor.Name = "Мурка"; vm.Editor.Species = "Кошка";
        await vm.CreateCommand.ExecuteAsync();
        Equal(1, api.Creates); Equal(2, api.ListCalls); Equal(2, vm.Pets.Count);
        Check(vm.IsCreating); Equal(string.Empty, vm.Editor.Name); Check(!vm.HasError);
    }

    private static async Task ViewModelInvalidForm()
    {
        var (vm, api, _) = Fixture(); using var lifetime = vm;
        await vm.InitializeAsync(); vm.Editor.Name = "Кот"; vm.Editor.Species = "Кошка"; vm.Editor.Age = "abc";
        await vm.CreateCommand.ExecuteAsync(); Equal(0, api.Creates); Check(vm.HasError);
        vm.SelectedPet = vm.Pets[0]; vm.Editor.Weight = "wrong";
        await vm.UpdateCommand.ExecuteAsync(); await vm.PatchCommand.ExecuteAsync();
        Equal(0, api.Updates); Equal(0, api.Patches); Check(vm.HasError);
    }

    private static async Task ViewModelPut()
    {
        var (vm, api, _) = Fixture(); using var lifetime = vm;
        await vm.InitializeAsync(); vm.SelectedPet = vm.Pets[0];
        vm.Editor.Name = "Новое имя"; vm.Editor.Species = "Кот"; vm.Editor.Age = "5";
        vm.Editor.Weight = "6,5"; vm.Editor.IsAdopted = true;
        await vm.UpdateCommand.ExecuteAsync();
        Equal(1, api.Updates); Equal(2, api.ListCalls); Equal("Новое имя", vm.Pets[0].Name);
        Equal(6.5m, vm.Pets[0].Weight); Check(vm.Pets[0].IsAdopted); Check(!vm.HasError);
    }

    private static async Task ViewModelPatch()
    {
        var (vm, api, _) = Fixture(); using var lifetime = vm;
        await vm.InitializeAsync(); vm.SpeciesFilter = "Кошка"; vm.SelectedAdoptionFilter = vm.AdoptionFilters[1];
        await vm.ApplyFiltersCommand.ExecuteAsync(); vm.SelectedPet = vm.Pets[0];
        vm.SpeciesFilter = "Не применённый фильтр";
        vm.Editor.IsAdopted = true; await vm.PatchCommand.ExecuteAsync();
        Equal(1, api.Patches); Equal(1, api.LastPatch.Count); Check((bool)api.LastPatch["isAdopted"]);
        Equal("Кошка", api.LastSpecies); Equal(false, api.LastAdopted); Equal(0, vm.Pets.Count); Check(vm.IsCreating);
    }

    private static async Task ViewModelNoChanges()
    {
        var (vm, api, _) = Fixture(); using var lifetime = vm;
        await vm.InitializeAsync(); vm.SelectedPet = vm.Pets[0]; await vm.PatchCommand.ExecuteAsync();
        Equal(0, api.Patches); Check(vm.StatusMessage.Contains("Изменений нет"));
    }

    private static async Task ViewModelDelete()
    {
        var (vm, api, dialogs) = Fixture(); using var lifetime = vm;
        await vm.InitializeAsync(); vm.SelectedPet = vm.Pets[0]; dialogs.Answer = false;
        await vm.DeleteCommand.ExecuteAsync(); Equal(0, api.Deletes); Equal(1, vm.Pets.Count);
        dialogs.Answer = true; await vm.DeleteCommand.ExecuteAsync();
        Equal(1, api.Deletes); Equal(2, dialogs.Calls); Equal(0, vm.Pets.Count); Check(vm.IsCreating);
    }

    private static async Task ViewModelReset()
    {
        var (vm, api, _) = Fixture(); using var lifetime = vm;
        await vm.InitializeAsync(); vm.SpeciesFilter = "Собака"; vm.SelectedAdoptionFilter = vm.AdoptionFilters[2];
        await vm.ApplyFiltersCommand.ExecuteAsync(); await vm.ResetFiltersCommand.ExecuteAsync();
        Equal<string?>(null, api.LastSpecies); Equal<bool?>(null, api.LastAdopted); Equal(1, vm.Pets.Count);
        Equal(string.Empty, vm.SpeciesFilter); Equal(vm.AdoptionFilters[0], vm.SelectedAdoptionFilter);
    }

    private static async Task ViewModelNetwork()
    {
        var (vm, api, _) = Fixture(); using var lifetime = vm;
        api.ListError = new HttpRequestException("offline"); await vm.InitializeAsync();
        Check(vm.ErrorMessage.Contains("API запущен")); Check(!vm.IsBusy); Check(!vm.IsConnected);
        api.ListError = null; await vm.RefreshCommand.ExecuteAsync(); Check(!vm.HasError); Check(vm.IsConnected);
    }

    private static async Task ViewModelTimeout()
    {
        var (vm, api, _) = Fixture(); using var lifetime = vm;
        api.ListError = new TaskCanceledException(); await vm.InitializeAsync();
        Check(vm.ErrorMessage.Contains("15 секунд")); Check(!vm.IsBusy);
    }

    private static async Task ViewModelRefreshFailure()
    {
        var (vm, api, _) = Fixture(); using var lifetime = vm;
        await vm.InitializeAsync(); vm.Editor.Name = "Новый"; vm.Editor.Species = "Кошка";
        api.ListError = new HttpRequestException(); await vm.CreateCommand.ExecuteAsync();
        Equal(1, api.Creates); Equal(2, api.Data.Count); Check(vm.ErrorMessage.Contains("Питомец создан"));
        Check(vm.StatusMessage.Contains("Питомец создан")); Equal(string.Empty, vm.Editor.Name);
        api.ListError = null; await vm.RefreshCommand.ExecuteAsync(); Equal(2, vm.Pets.Count);
    }

    private static async Task ViewModelServerChange()
    {
        var (vm, _, _) = Fixture(); using var lifetime = vm;
        await vm.InitializeAsync(); vm.SelectedPet = vm.Pets[0]; vm.ServerAddress = "http://localhost:9000/";
        Equal(0, vm.Pets.Count); Check(vm.IsCreating); Check(!vm.UpdateCommand.CanExecute(null));
        Check(!vm.CreateCommand.CanExecute(null)); Check(!vm.IsConnected);
    }

    private static async Task BusyAndCancellation()
    {
        var (vm, api, _) = Fixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.BeforeList = async token => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); };
        var running = vm.InitializeAsync(); await started.Task;
        Check(vm.IsBusy); Check(!vm.RefreshCommand.CanExecute(null)); Check(!vm.NewRecordCommand.CanExecute(null));
        await vm.RefreshCommand.ExecuteAsync(); Equal(1, api.ListCalls);
        vm.Dispose(); await running; Check(!vm.IsBusy); Check(!vm.HasError);
    }

    private static void Check(bool condition, string message = "Assertion failed")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected '{expected}', got '{actual}'.");

    private static async Task<T> Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
