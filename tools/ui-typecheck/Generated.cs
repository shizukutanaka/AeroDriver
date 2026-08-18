// CommunityToolkit.Mvvm のソースジェネレーターが MainViewModel に対して生成するはずの
// メンバーを手書きで再現したもの。ジェネレーター自体は動かせないが、
// 「MainViewModel の手書きコードがこの契約と整合するか」は本物のコンパイラで検証できる。
using System.Globalization;
using AeroDriver.Core.Models;
using AeroDriver.UI.Services;
using CommunityToolkit.Mvvm.Input;

namespace AeroDriver.UI.ViewModels
{
    public partial class MainViewModel
    {
        // [ObservableProperty] フィールド -> 公開プロパティ (_isBusy -> IsBusy)
        public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(nameof(IsBusy)); } }
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); } }
        public DriverInfo? SelectedUpdate { get => _selectedUpdate; set { _selectedUpdate = value; OnPropertyChanged(nameof(SelectedUpdate)); } }
        public DriverInfo? SelectedInstalledDriver
        {
            get => _selectedInstalledDriver;
            set { _selectedInstalledDriver = value; OnSelectedInstalledDriverChanged(value); OnPropertyChanged(nameof(SelectedInstalledDriver)); }
        }
        public DriverDetailInfo? SelectedDetail { get => _selectedDetail; set { _selectedDetail = value; OnPropertyChanged(nameof(SelectedDetail)); } }
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

        // [RelayCommand] メソッド -> コマンド (ScanAsync -> ScanCommand, Cancel -> CancelCommand)
        public IAsyncRelayCommand ScanCommand { get; } = new StubAsyncCommand();
        public IAsyncRelayCommand CheckUpdatesCommand { get; } = new StubAsyncCommand();
        public IAsyncRelayCommand InstallSelectedCommand { get; } = new StubAsyncCommand();
        public IAsyncRelayCommand InstallAllUpdatesCommand { get; } = new StubAsyncCommand();
        public IAsyncRelayCommand RollbackSelectedCommand { get; } = new StubAsyncCommand();
        public IAsyncRelayCommand BackupSelectedCommand { get; } = new StubAsyncCommand();
        public IAsyncRelayCommand InstallCustomDriverCommand { get; } = new StubAsyncCommand();
        public IAsyncRelayCommand ShowDetailsCommand { get; } = new StubAsyncCommand();
        public IRelayCommand CancelCommand { get; } = new StubAsyncCommand();
    }
}
