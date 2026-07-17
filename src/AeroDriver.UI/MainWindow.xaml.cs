using System.Windows;
using AeroDriver.UI.ViewModels;

namespace AeroDriver.UI
{
    /// <summary>
    /// メインウィンドウ。DataContext は DI で注入された <see cref="MainViewModel"/>。
    /// ロジックはすべて ViewModel 側にあり、code-behind は配線のみ。
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
