using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;

namespace CommunityToolkit.Mvvm.ComponentModel
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        protected bool SetProperty<T>(ref T field, T value, string? name = null)
        { field = value; OnPropertyChanged(name); return true; }
    }
    [AttributeUsage(AttributeTargets.Field)] public sealed class ObservablePropertyAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public sealed class NotifyCanExecuteChangedForAttribute : Attribute
    { public NotifyCanExecuteChangedForAttribute(string name) { } }
}

namespace CommunityToolkit.Mvvm.Input
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class RelayCommandAttribute : Attribute
    { public string? CanExecute { get; set; } }

    public interface IRelayCommand : ICommand { void NotifyCanExecuteChanged(); }
    public interface IAsyncRelayCommand : IRelayCommand { Task ExecuteAsync(object? parameter); }

    public sealed class StubAsyncCommand : IAsyncRelayCommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) { }
        public Task ExecuteAsync(object? p) => Task.CompletedTask;
        public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

namespace AeroDriver.Languages.Services
{
    public interface ILanguageService
    {
        string GetString(string name);
        string GetString(string name, CultureInfo culture);
        string GetString(string name, params object[] args);
        CultureInfo CurrentCulture { get; }
        void SetCulture(CultureInfo culture);
        IReadOnlyList<CultureInfo> SupportedCultures { get; }
    }
}

// ---- WPF の最小スタブ(App.xaml.cs の手書きロジックを型検査するため) ----
namespace System.Windows
{
    public class Application
    {
        public static Application? Current { get; set; }
        public ResourceDictionary Resources { get; } = new();
        public event System.Windows.Threading.DispatcherUnhandledExceptionEventHandler? DispatcherUnhandledException;
        protected virtual void OnStartup(StartupEventArgs e) { }
        protected virtual void OnExit(ExitEventArgs e) { }
        protected void RaiseForStubOnly() => DispatcherUnhandledException?.Invoke(this, null!);
    }
    public class StartupEventArgs : EventArgs { }
    public class ExitEventArgs : EventArgs { }
    public class ResourceDictionary { public IList<ResourceDictionary> MergedDictionaries { get; } = new List<ResourceDictionary>(); public Uri? Source { get; set; } }
    public class Window { public void Show() { } public object? DataContext { get; set; } }
    public enum MessageBoxButton { OK }
    public enum MessageBoxImage { Error }
    public static class MessageBox
    { public static void Show(string t, string c, MessageBoxButton b, MessageBoxImage i) { } }
}
namespace System.Windows.Threading
{
    public class DispatcherUnhandledExceptionEventArgs : EventArgs
    { public Exception Exception { get; set; } = null!; public bool Handled { get; set; } }
    public delegate void DispatcherUnhandledExceptionEventHandler(object sender, DispatcherUnhandledExceptionEventArgs e);
}
// MainWindow の XAML 生成側 partial(InitializeComponent)を再現する
namespace AeroDriver.UI
{
    public partial class MainWindow : System.Windows.Window
    {
        private void InitializeComponent() { }
    }
}

// ConfigureServices は WMI 依存のサービスを登録するため、ここでは拡張メソッドの形だけ再現する
namespace AeroDriver.Core
{
    public static class ServiceCollectionExtensions
    {
        public static Microsoft.Extensions.DependencyInjection.IServiceCollection ConfigureServices(
            this Microsoft.Extensions.DependencyInjection.IServiceCollection services) => services;
    }
}

// DI 登録の型引数として名前解決が必要な具象クラス。
// 実装は WPF/resx 依存なのでここでは形だけ(登録が型として成立するかの検査が目的)。
namespace AeroDriver.Languages.Services
{
    public sealed class LanguageService : ILanguageService
    {
        public string GetString(string n) => n;
        public string GetString(string n, System.Globalization.CultureInfo c) => n;
        public string GetString(string n, params object[] a) => n;
        public System.Globalization.CultureInfo CurrentCulture => System.Globalization.CultureInfo.InvariantCulture;
        public void SetCulture(System.Globalization.CultureInfo c) { }
        public IReadOnlyList<System.Globalization.CultureInfo> SupportedCultures { get; } = new List<System.Globalization.CultureInfo>();
    }
}
namespace AeroDriver.UI.Services
{
    public sealed class FileDialogService : IFileDialogService { public string? PickDriverFile() => null; }
    public sealed class ThemeService : IThemeService
    {
        public AppTheme CurrentTheme => AppTheme.Light;
        public IReadOnlyList<AppTheme> AvailableThemes { get; } = new[] { AppTheme.Light, AppTheme.Dark };
        public void Apply(AppTheme t) { }
    }
}
