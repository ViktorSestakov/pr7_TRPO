using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using pr7_shestakov.Infrastructure;
using pr7_shestakov.Models;
using pr7_shestakov.Services;

namespace pr7_shestakov.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IPetApiClient _api;
    private readonly IUserDialogService _dialogs;
    private readonly List<AsyncCommand> _commands = [];
    private readonly CancellationTokenSource _lifetime = new();
    private bool _initialized;
    private bool _disposed;
    private bool _isBusy;
    private bool _isConnected;
    private string _serverAddress = "http://localhost:7193/";
    private string _speciesFilter = string.Empty;
    private AdoptionFilter _selectedAdoptionFilter;
    private string? _appliedSpecies;
    private bool? _appliedAdopted;
    private string _lookupId = string.Empty;
    private Pet? _selectedPet;
    private Pet? _originalPet;
    private PetEditor _editor = new();
    private string _errorMessage = string.Empty;
    private string _statusMessage = "Подключитесь к запущенному API или укажите другой адрес сервера.";

    public ObservableCollection<Pet> Pets { get; } = [];
    public IReadOnlyList<AdoptionFilter> AdoptionFilters { get; } =
    [
        new("Все питомцы", null),
        new("Ищут дом", false),
        new("Пристроены", true)
    ];

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ApplyFiltersCommand { get; }
    public AsyncCommand ResetFiltersCommand { get; }
    public AsyncCommand GetByIdCommand { get; }
    public AsyncCommand NewRecordCommand { get; }
    public AsyncCommand CreateCommand { get; }
    public AsyncCommand UpdateCommand { get; }
    public AsyncCommand PatchCommand { get; }
    public AsyncCommand DeleteCommand { get; }

    public MainViewModel(IPetApiClient api, IUserDialogService dialogs)
    {
        _api = api;
        _dialogs = dialogs;
        _selectedAdoptionFilter = AdoptionFilters[0];

        RefreshCommand = Command(() => RunAsync(async token =>
        {
            await LoadListAsync(_appliedSpecies, _appliedAdopted, _originalPet?.Id, token);
            StatusMessage = $"Список обновлён. Записей: {Pets.Count}.";
        }));

        ApplyFiltersCommand = Command(() => RunAsync(async token =>
        {
            await LoadListAsync(SpeciesFilter, SelectedAdoptionFilter.Value, _originalPet?.Id, token);
            StatusMessage = $"Фильтры применены на сервере. Найдено записей: {Pets.Count}.";
        }));

        ResetFiltersCommand = Command(() => RunAsync(async token =>
        {
            SpeciesFilter = string.Empty;
            SelectedAdoptionFilter = AdoptionFilters[0];
            await LoadListAsync(null, null, _originalPet?.Id, token);
            StatusMessage = $"Фильтры сброшены. Записей: {Pets.Count}.";
        }));

        GetByIdCommand = Command(() => RunAsync(async token =>
        {
            if (!int.TryParse(LookupId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
            {
                ErrorMessage = "ID должен быть положительным целым числом.";
                return;
            }

            var pet = await _api.GetAsync(id, token);
            var index = Pets.ToList().FindIndex(item => item.Id == id);
            if (index >= 0) Pets[index] = pet;
            SelectedPet = index >= 0 ? pet : null;
            ShowPet(pet);
            IsConnected = true;
            StatusMessage = $"Загружен питомец с ID {id}.";
        }));

        NewRecordCommand = Command(() =>
        {
            SelectedPet = null;
            ShowPet(null);
            LookupId = string.Empty;
            ErrorMessage = string.Empty;
            StatusMessage = "Заполните форму и нажмите «Создать питомца».";
            return Task.CompletedTask;
        });

        CreateCommand = Command(() => RunAsync(async token =>
        {
            if (!ValidateEditor(out var pet)) return;
            await _api.CreateAsync(pet, token);
            SelectedPet = null;
            ShowPet(null);
            await RefreshAfterMutationAsync(null, "Питомец создан.", token);
        }), () => IsConnected && !IsEditing);

        UpdateCommand = Command(() => RunAsync(async token =>
        {
            if (!ValidateEditor(out var pet)) return;
            await _api.UpdateAsync(pet.Id, pet, token);
            ShowPet(pet);
            await RefreshAfterMutationAsync(pet.Id, "Запись полностью сохранена (PUT).", token);
        }), () => IsEditing);

        PatchCommand = Command(() => RunAsync(async token =>
        {
            if (_originalPet is null || !ValidateEditor(out var pet)) return;
            var changes = Editor.GetChanges(_originalPet);
            if (changes.Count == 0)
            {
                StatusMessage = "Изменений нет. Отредактируйте хотя бы одно поле.";
                return;
            }

            await _api.PatchAsync(pet.Id, changes, token);
            ShowPet(pet);
            await RefreshAfterMutationAsync(pet.Id,
                $"Изменённые поля сохранены (PATCH): {changes.Count}.", token);
        }), () => IsEditing);

        DeleteCommand = Command(async () =>
        {
            var pet = _originalPet;
            if (pet is null || !_dialogs.ConfirmDelete(pet)) return;
            await RunAsync(async token =>
            {
                await _api.DeleteAsync(pet.Id, token);
                SelectedPet = null;
                ShowPet(null);
                LookupId = string.Empty;
                await RefreshAfterMutationAsync(null, "Питомец удалён.", token);
            });
        }, () => IsEditing);
    }

    public string ServerAddress
    {
        get => _serverAddress;
        set
        {
            if (!SetProperty(ref _serverAddress, value)) return;
            // Запись с одного сервера не должна случайно сохраниться на другом.
            SelectedPet = null;
            ShowPet(null);
            Pets.Clear();
            LookupId = string.Empty;
            IsConnected = false;
            OnPropertyChanged(nameof(RecordCount));
            StatusMessage = "Адрес изменён. Нажмите «Подключиться / обновить».";
        }
    }

    public string SpeciesFilter { get => _speciesFilter; set => SetProperty(ref _speciesFilter, value); }
    public AdoptionFilter SelectedAdoptionFilter
    {
        get => _selectedAdoptionFilter;
        set => SetProperty(ref _selectedAdoptionFilter, value);
    }
    public string LookupId { get => _lookupId; set => SetProperty(ref _lookupId, value); }
    public Pet? SelectedPet
    {
        get => _selectedPet;
        set
        {
            if (SetProperty(ref _selectedPet, value))
                ShowPet(value);
        }
    }
    public PetEditor Editor { get => _editor; private set => SetProperty(ref _editor, value); }
    public bool IsEditing => _originalPet is not null;
    public bool IsCreating => !IsEditing;
    public string EditorTitle => IsEditing ? $"Питомец #{_originalPet!.Id}" : "Новый питомец";
    public string RecordCount => $"Записей в списке: {Pets.Count}";
    public string FilterSummary => string.Join(" · ", new[]
    {
        string.IsNullOrWhiteSpace(_appliedSpecies) ? "Все виды" : $"Вид: {_appliedSpecies}",
        _appliedAdopted switch { true => "Пристроены", false => "Ищут дом", null => "Любой статус" }
    });

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsNotBusy));
            NotifyCommands();
        }
    }
    public bool IsNotBusy => !IsBusy;
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (!SetProperty(ref _isConnected, value)) return;
            OnPropertyChanged(nameof(ConnectionStatus));
            NotifyCommands();
        }
    }
    public string ConnectionStatus => IsConnected ? "API подключён" : "Нет подключения";
    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError));
        }
    }
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await RefreshCommand.ExecuteAsync();
    }

    private AsyncCommand Command(Func<Task> action, Func<bool>? condition = null)
    {
        var command = new AsyncCommand(action,
            () => !_disposed && !IsBusy && (condition?.Invoke() ?? true),
            exception => ErrorMessage = DescribeError(exception));
        _commands.Add(command);
        return command;
    }

    private void NotifyCommands()
    {
        foreach (var command in _commands) command.NotifyCanExecuteChanged();
    }

    private void ShowPet(Pet? pet)
    {
        _originalPet = pet?.Copy();
        Editor = pet is null ? new PetEditor() : new PetEditor(pet);
        if (pet is not null) LookupId = pet.Id.ToString(CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(IsCreating));
        NotifyCommands();
    }

    private bool ValidateEditor(out Pet pet)
    {
        if (Editor.TryCreatePet(out pet)) return true;
        ErrorMessage = Editor.Error;
        return false;
    }

    private async Task LoadListAsync(string? species, bool? adopted, int? preferredId, CancellationToken token)
    {
        var pets = await _api.GetAllAsync(species, adopted, token);
        _appliedSpecies = string.IsNullOrWhiteSpace(species) ? null : species.Trim();
        _appliedAdopted = adopted;
        SelectedPet = null;
        ShowPet(null);
        Pets.Clear();
        foreach (var pet in pets) Pets.Add(pet);
        SelectedPet = Pets.FirstOrDefault(pet => pet.Id == preferredId);
        IsConnected = true;
        OnPropertyChanged(nameof(RecordCount));
        OnPropertyChanged(nameof(FilterSummary));
    }

    private async Task RefreshAfterMutationAsync(int? preferredId, string successMessage, CancellationToken token)
    {
        StatusMessage = successMessage;
        try
        {
            await LoadListAsync(_appliedSpecies, _appliedAdopted, preferredId, token);
            StatusMessage = $"{successMessage} Записей в списке: {Pets.Count}.";
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            // Успешное сохранение нельзя выдавать за ошибку записи: повторный POST создаст дубль.
            ErrorMessage = $"{successMessage} Не удалось обновить список. Нажмите «Подключиться / обновить». {DescribeError(exception)}";
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy || _disposed) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = "Выполняется запрос к серверу…";
        var token = _lifetime.Token;
        try
        {
            _api.SetServerAddress(ServerAddress);
            await action(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Закрытие окна отменяет запрос без лишнего сообщения.
        }
        catch (Exception exception)
        {
            ErrorMessage = DescribeError(exception);
            StatusMessage = "Действие не выполнено. Исправьте ошибку и повторите попытку.";
        }
        finally
        {
            if (StatusMessage == "Выполняется запрос к серверу…")
                StatusMessage = HasError ? "Исправьте ошибку и повторите попытку." : "Готово.";
            IsBusy = false;
        }
    }

    private static string DescribeError(Exception exception) => exception switch
    {
        ApiException => exception.Message,
        HttpRequestException => "Не удалось связаться с сервером. Проверьте, что API запущен, а протокол и порт указаны верно. Для HTTPS проверьте сертификат разработки.",
        OperationCanceledException => "Сервер не ответил за 15 секунд. Проверьте соединение и повторите запрос.",
        ArgumentException => exception.Message,
        _ => $"Не удалось выполнить действие: {exception.Message}"
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        NotifyCommands();
    }
}
