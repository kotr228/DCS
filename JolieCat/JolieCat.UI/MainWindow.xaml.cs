using JolieCat.UI.ViewModels;
using System.Windows;

namespace JolieCat.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }
    }
}