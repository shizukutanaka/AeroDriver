// CommunityToolkit.Mvvm と ILanguageService の最小スタブ(ui-typecheck と同系だが、
// こちらはコマンドを「実際に private ハンドラーへ配線」して ViewModel を実行するための版)。
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

    /// <summary>本物の RelayCommand 相当: 渡されたデリゲートを実際に実行する。</summary>
    public sealed class RealAsyncCommand : IAsyncRelayCommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool>? _canExecute;
        public RealAsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
        { _execute = execute; _canExecute = canExecute; }
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? p) => _canExecute?.Invoke() ?? true;
        public void Execute(object? p) => _ = _execute();
        public Task ExecuteAsync(object? p) => _execute();
        public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public sealed class RealCommand : IRelayCommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;
        public RealCommand(Action execute, Func<bool>? canExecute = null)
        { _execute = execute; _canExecute = canExecute; }
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? p) => _canExecute?.Invoke() ?? true;
        public void Execute(object? p) => _execute();
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
