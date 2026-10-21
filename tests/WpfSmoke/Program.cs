using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using pr7_shestakov;
using pr7_shestakov.Models;
using pr7_shestakov.Services;
using pr7_shestakov.ViewModels;

namespace WpfSmoke;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var bindingErrors = new BindingErrors();
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
        var app = new App();
        app.InitializeComponent();
        using var preview = new MainViewModel(new PreviewApi(), new PreviewDialogs());
        preview.InitializeAsync().GetAwaiter().GetResult();
        preview.SelectedPet = preview.Pets[0];
        var window = new MainWindow(preview);
        window.Show();
        var root = (FrameworkElement)window.Content;
        var directory = args.Length > 0 ? args[0] : "artifacts";
        Directory.CreateDirectory(directory);
        Render(root, Path.Combine(directory, "pet-client.png"));
        preview.NewRecordCommand.ExecuteAsync().GetAwaiter().GetResult();
        preview.Editor.Name = "Мурка";
        preview.Editor.Species = "Кошка";
        window.Width = 1100;
        window.Height = 740;
        Render(root, Path.Combine(directory, "pet-client-create.png"));
        window.Close();
        app.Shutdown();
        if (bindingErrors.Errors.Count > 0)
            throw new InvalidOperationException("WPF binding errors:\n" + string.Join("\n", bindingErrors.Errors));
        Console.WriteLine("WPF window, Material Design resources and bindings loaded successfully.");
        return 0;
    }

    private static void Render(FrameworkElement root, string path)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        root.UpdateLayout();
        var width = (int)Math.Ceiling(root.ActualWidth);
        var height = (int)Math.Ceiling(root.ActualHeight);
        if (width <= 0 || height <= 0) throw new InvalidOperationException("The window did not complete layout.");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}

internal sealed class BindingErrors : TraceListener
{
    public List<string> Errors { get; } = [];
    public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message); }
    public override void WriteLine(string? message) => Write(message);
}

internal sealed class PreviewDialogs : IUserDialogService
{
    public bool ConfirmDelete(Pet pet) => false;
}

internal sealed class PreviewApi : IPetApiClient
{
    private readonly Pet[] _pets =
    [
        new() { Id = 1, Name = "Барсик", Species = "Кошка", Age = 3, Weight = 4.5m },
        new() { Id = 2, Name = "Рекс", Species = "Собака", Age = 5, Weight = 18.2m, IsAdopted = true },
        new() { Id = 3, Name = "Луна", Species = "Кошка", Age = 1, Weight = 2.8m }
    ];
    public void SetServerAddress(string address) { }
    public Task<IReadOnlyList<Pet>> GetAllAsync(string? species, bool? adopted, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<Pet>>(_pets);
    public Task<Pet> GetAsync(int id, CancellationToken token) => Task.FromResult(_pets.First(p => p.Id == id));
    public Task CreateAsync(Pet pet, CancellationToken token) => Task.CompletedTask;
    public Task UpdateAsync(int id, Pet pet, CancellationToken token) => Task.CompletedTask;
    public Task PatchAsync(int id, IReadOnlyDictionary<string, object> changes, CancellationToken token) => Task.CompletedTask;
    public Task DeleteAsync(int id, CancellationToken token) => Task.CompletedTask;
}
