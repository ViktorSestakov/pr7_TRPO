using System.Net.Http;
using System.Windows;
using pr7_shestakov.Services;
using pr7_shestakov.ViewModels;

namespace pr7_shestakov;

public partial class MainWindow : Window
{
    private readonly HttpClient? _httpClient;
    private readonly MainViewModel _viewModel;

    public MainWindow() : this(null) { }

    public MainWindow(MainViewModel? viewModel)
    {
        InitializeComponent();
        if (viewModel is null)
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            viewModel = new MainViewModel(new PetApiClient(_httpClient), new MessageBoxDialogService());
        }
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        _httpClient?.Dispose();
        base.OnClosed(e);
    }
}
