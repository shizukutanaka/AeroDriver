using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AeroDriver.Core;
using AeroDriver.Core.Services;
using Avalonia.Input;

namespace AeroDriver.UI
{
    public partial class MainWindow : Window
    {
        private readonly ISimpleLogger _logger;
        private readonly CoreDriverService _driverService;

        public MainWindow()
        {
            // Constructor for XAML previewer
            InitializeComponent();
        }

        public MainWindow(ISimpleLogger logger)
        {
            InitializeComponent();
            _logger = logger;
            _driverService = new CoreDriverService(_logger);

            SetupEventHandlers();
            LoadInitialData();
        }

        private void SetupEventHandlers()
        {
            this.FindControl<MenuItem>("ScanMenuItem").Click += ScanMenuItem_Click;
            this.FindControl<MenuItem>("ExitMenuItem").Click += (s, e) => Close();
            this.FindControl<MenuItem>("OptimizeMenuItem").Click += OptimizeMenuItem_Click;
            this.FindControl<MenuItem>("BackupMenuItem").Click += BackupMenuItem_Click;
            this.FindControl<MenuItem>("AboutMenuItem").Click += AboutMenuItem_Click;

            this.FindControl<Button>("ScanButton").Click += ScanButton_Click;
            this.FindControl<Button>("RefreshButton").Click += RefreshButton_Click;
            this.FindControl<Button>("ClearLogsButton").Click += (s, e) => this.FindControl<TextBox>("LogTextBox").Clear();
            
            this.KeyDown += MainWindow_KeyDown;
        }

        private void LoadInitialData()
        {
            this.FindControl<TextBlock>("StatusTextBlock").Text = "Loading...";
            LoadDriverList();
            LoadSystemInfo();
        }

        private void LoadSystemInfo()
        {
            this.FindControl<TextBlock>("OsInfoTextBlock").Text = $"{Environment.OSVersion.Platform} {Environment.OSVersion.Version}";
            this.FindControl<TextBlock>("ProcessorInfoTextBlock").Text = $"Processor: {Environment.ProcessorCount} cores";
            this.FindControl<TextBlock>("MachineNameTextBlock").Text = $"Machine: {Environment.MachineName}";
        }

        private void LoadDriverList()
        {
            var driverListBox = this.FindControl<ListBox>("DriverListBox");
            driverListBox.Items.Clear();
            try
            {
                var drivers = _driverService.GetAllDrivers();
                driverListBox.Items = drivers;
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to load driver list: {ex.Message}");
            }
        }

        private async Task PerformScanAsync()
        {
            var scanButton = this.FindControl<Button>("ScanButton");
            scanButton.IsEnabled = false;
            this.FindControl<TextBlock>("StatusTextBlock").Text = "Scanning...";

            try
            {
                var scanResult = await Task.Run(() => _driverService.ScanSystem());
                this.FindControl<TextBlock>("StatusTextBlock").Text = $"Scan complete. Found {scanResult.ScannedDrivers} drivers.";
                LoadDriverList();
                LogMessage($"Scan completed. Found {scanResult.ScannedDrivers} drivers.");
            }
            catch (Exception ex)
            {
                this.FindControl<TextBlock>("StatusTextBlock").Text = "Scan failed.";
                LogMessage($"Scan failed: {ex.Message}");
            }
            finally
            {
                scanButton.IsEnabled = true;
            }
        }

        private void LogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] {message}";

            Dispatcher.UIThread.Post(() =>
            {
                var logTextBox = this.FindControl<TextBox>("LogTextBox");
                if (logTextBox != null)
                {
                    logTextBox.Text += logEntry + Environment.NewLine;
                    logTextBox.CaretIndex = logTextBox.Text.Length;
                }
            });

            _logger.LogInformation(message);
        }

        private void ScanButton_Click(object sender, RoutedEventArgs e) => _ = PerformScanAsync();
        private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadInitialData();
        private void ScanMenuItem_Click(object sender, RoutedEventArgs e) => _ = PerformScanAsync();
        private void OptimizeMenuItem_Click(object sender, RoutedEventArgs e) { /* Placeholder */ }
        private void BackupMenuItem_Click(object sender, RoutedEventArgs e) { /* Placeholder */ }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new Window
            {
                Title = "About AeroDriver",
                Width = 400, Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "AeroDriver", FontSize = 24, FontWeight = Avalonia.Media.FontWeight.Bold, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                        new TextBlock { Text = "Professional Windows Driver Management Suite", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                        new TextBlock { Text = $"Version: {GetType().Assembly.GetName().Version}", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }
                    }
                }
            };
            aboutWindow.ShowDialog(this);
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyModifiers == KeyModifiers.Control)
            {
                var mainTabControl = this.FindControl<TabControl>("MainTabControl");
                switch (e.Key)
                {
                    case Key.R: LoadInitialData(); e.Handled = true; break;
                    case Key.S: _ = PerformScanAsync(); e.Handled = true; break;
                    case Key.L: mainTabControl.SelectedIndex = 2; e.Handled = true; break;
                    case Key.D: mainTabControl.SelectedIndex = 1; e.Handled = true; break;
                }
            }
            else if (e.Key == Key.F5)
            {
                LoadInitialData();
                e.Handled = true;
            }
        }
    }
}
