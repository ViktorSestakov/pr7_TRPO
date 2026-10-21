

using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Net.Http;

namespace pr7_shestakov
{
    public partial class MainWindow : Window
    {
        public async void GetItems()
        {
            HttpClient hc = new HttpClient();
            hc.BaseAddress = new Uri("http://localhost:7193/");
            var items = await hc.GetAsync("api/v1/Pet");
        }

        public MainWindow()
        {
            InitializeComponent();
            GetItems();
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Change_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Del_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}