using System.Windows.Input;

namespace pr7_shestakov.Infrastructure;

public sealed class AsyncCommand(
    Func<Task> execute,
    Func<bool> canExecute,
    Action<Exception> onError) : ICommand
{
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isExecuting && canExecute();

    public async void Execute(object? parameter)
    {
        try
        {
            await ExecuteAsync();
        }
        catch (Exception exception)
        {
            onError(exception);
        }
    }

    public async Task ExecuteAsync()
    {
        if (!CanExecute(null))
            return;

        _isExecuting = true;
        NotifyCanExecuteChanged();
        try
        {
            await execute();
        }
        finally
        {
            _isExecuting = false;
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
