// CommunityToolkit.Mvvm のソースジェネレーターが MainViewModel に対して生成するはずの
// メンバーの再現。ui-typecheck 版との違いは、コマンドが inert なスタブではなく
// **実際の private ハンドラーと CanExecute 述語に配線されている**こと。
// partial class は同じ型の private メンバーにアクセスできるため、これだけで
// ViewModel を「実行」して検証できる。
using System.Globalization;
using AeroDriver.Core.Models;
using AeroDriver.UI.Services;
using CommunityToolkit.Mvvm.Input;

namespace AeroDriver.UI.ViewModels
{
    public partial class MainViewModel
    {
        // [ObservableProperty] フィールド -> 公開プロパティ (_isBusy -> IsBusy)
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged(nameof(IsBusy));
                // [NotifyCanExecuteChangedFor] 相当
                ScanCommand.NotifyCanExecuteChanged();
                CheckUpdatesCommand.NotifyCanExecuteChanged();
                InstallSelectedCommand.NotifyCanExecuteChanged();
                InstallAllUpdatesCommand.NotifyCanExecuteChanged();
                RollbackSelectedCommand.NotifyCanExecuteChanged();
                BackupSelectedCommand.NotifyCanExecuteChanged();
                InstallCustomDriverCommand.NotifyCanExecuteChanged();
                ShowDetailsCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
        }

        public DriverInfo? SelectedUpdate
        {
            get => _selectedUpdate;
            set { _selectedUpdate = value; OnPropertyChanged(nameof(SelectedUpdate)); InstallSelectedCommand.NotifyCanExecuteChanged(); }
        }

        public DriverInfo? SelectedInstalledDriver
        {
            get => _selectedInstalledDriver;
            set
            {
                _selectedInstalledDriver = value;
                OnSelectedInstalledDriverChanged(value);
                OnPropertyChanged(nameof(SelectedInstalledDriver));
                RollbackSelectedCommand.NotifyCanExecuteChanged();
                BackupSelectedCommand.NotifyCanExecuteChanged();
                ShowDetailsCommand.NotifyCanExecuteChanged();
            }
        }

        public DriverDetailInfo? SelectedDetail
        {
            get => _selectedDetail;
            set { _selectedDetail = value; OnPropertyChanged(nameof(SelectedDetail)); }
        }

        public CultureInfo? SelectedCulture
        {
            get => _selectedCulture;
            set { _selectedCulture = value; OnSelectedCultureChanged(value); OnPropertyChanged(nameof(SelectedCulture)); }
        }

        public AppTheme SelectedTheme
        {
            get => _selectedTheme;
            set { _selectedTheme = value; OnSelectedThemeChanged(value); OnPropertyChanged(nameof(SelectedTheme)); }
        }

        // partial メソッドの定義側(実装側は MainViewModel.cs にある)
        partial void OnSelectedThemeChanged(AppTheme value);
        partial void OnSelectedCultureChanged(CultureInfo? value);
        partial void OnSelectedInstalledDriverChanged(DriverInfo? value);

        // [RelayCommand] メソッド -> コマンド。実ハンドラー / 実 CanExecute へ配線する。
        // ここが ui-typecheck との決定的な差(あちらは inert なスタブ)。
        // ジェネレーター本体と同じく遅延生成にしてある。フィールド初期化子では
        // インスタンスメソッドのメソッドグループを参照できないため。
        private IAsyncRelayCommand? _scanCommand;
        public IAsyncRelayCommand ScanCommand => _scanCommand ??= new RealAsyncCommand(ScanAsync, CanRun);

        private IAsyncRelayCommand? _checkUpdatesCommand;
        public IAsyncRelayCommand CheckUpdatesCommand => _checkUpdatesCommand ??= new RealAsyncCommand(CheckUpdatesAsync, CanRun);

        private IAsyncRelayCommand? _installSelectedCommand;
        public IAsyncRelayCommand InstallSelectedCommand => _installSelectedCommand ??= new RealAsyncCommand(InstallSelectedAsync, CanInstall);

        private IAsyncRelayCommand? _installAllUpdatesCommand;
        public IAsyncRelayCommand InstallAllUpdatesCommand => _installAllUpdatesCommand ??= new RealAsyncCommand(InstallAllUpdatesAsync, CanInstallAll);

        private IAsyncRelayCommand? _rollbackSelectedCommand;
        public IAsyncRelayCommand RollbackSelectedCommand => _rollbackSelectedCommand ??= new RealAsyncCommand(RollbackSelectedAsync, CanRollback);

        private IAsyncRelayCommand? _backupSelectedCommand;
        public IAsyncRelayCommand BackupSelectedCommand => _backupSelectedCommand ??= new RealAsyncCommand(BackupSelectedAsync, CanBackup);

        private IAsyncRelayCommand? _installCustomDriverCommand;
        public IAsyncRelayCommand InstallCustomDriverCommand => _installCustomDriverCommand ??= new RealAsyncCommand(InstallCustomDriverAsync, CanRun);

        private IAsyncRelayCommand? _showDetailsCommand;
        public IAsyncRelayCommand ShowDetailsCommand => _showDetailsCommand ??= new RealAsyncCommand(ShowDetailsAsync, CanShowDetails);

        private IRelayCommand? _cancelCommand;
        public IRelayCommand CancelCommand => _cancelCommand ??= new RealCommand(Cancel, CanCancel);
    }
}
