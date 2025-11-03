using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AeroDriver.Core.Services;
using AeroDriver.Core;
using Avalonia.Input;
using Avalonia.Automation;

namespace AeroDriver.UI;

public class MainWindow : Window
{
    private readonly ISimpleLogger _logger;
    private readonly CoreDriverService _driverService;

    private TabControl _mainTabControl;
    private ListBox _driverListBox;
    private Button _refreshButton;
    private Button _scanButton;
    private TextBox _logTextBox;
    private TextBlock _statusTextBlock;

    public MainWindow(ISimpleLogger logger)
    {
        _logger = logger;
        _driverService = new CoreDriverService(_logger);

        InitializeUI();
        SetupEventHandlers();
        LoadInitialData();
    }

    private void InitializeUI()
    {
        Title = LocalizationManager.GetString(LocalizationKeys.ApplicationTitle);
        Width = 900;
        Height = 700;
        MinWidth = 600; // 最小幅を設定
        MinHeight = 500; // 最小高さを設定
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanResize = true; // リサイズ可能に設定

        var mainPanel = new DockPanel();

        // Menu Bar
        var menuBar = new Menu
        {
            DockPanel.Dock = Dock.Top
        };

        var fileMenu = new MenuItem { Header = LocalizationManager.GetString(LocalizationKeys.File) };
        var scanMenuItem = new MenuItem { Header = LocalizationManager.GetString(LocalizationKeys.Scan) };
        scanMenuItem.Click += ScanMenuItem_Click;
        var exitMenuItem = new MenuItem { Header = LocalizationManager.GetString(LocalizationKeys.Exit) };
        exitMenuItem.Click += ExitMenuItem_Click;
        fileMenu.Items.Add(scanMenuItem);
        fileMenu.Items.Add(new Separator());
        fileMenu.Items.Add(exitMenuItem);

        var toolsMenu = new MenuItem { Header = LocalizationManager.GetString(LocalizationKeys.Tools) };
        var optimizeMenuItem = new MenuItem { Header = LocalizationManager.GetString(LocalizationKeys.Optimize) };
        optimizeMenuItem.Click += OptimizeMenuItem_Click;
        var backupMenuItem = new MenuItem { Header = LocalizationManager.GetString(LocalizationKeys.Backup) };
        backupMenuItem.Click += BackupMenuItem_Click;
        toolsMenu.Items.Add(optimizeMenuItem);
        toolsMenu.Items.Add(backupMenuItem);

        var helpMenu = new MenuItem { Header = LocalizationManager.GetString(LocalizationKeys.Help) };
        var aboutMenuItem = new MenuItem { Header = LocalizationManager.GetString(LocalizationKeys.About) };
        aboutMenuItem.Click += AboutMenuItem_Click;
        helpMenu.Items.Add(aboutMenuItem);

        menuBar.Items.Add(fileMenu);
        menuBar.Items.Add(toolsMenu);
        menuBar.Items.Add(helpMenu);

        // Main Content
        _mainTabControl = new TabControl
        {
            DockPanel.Dock = Dock.Fill
        };

        // Dashboard Tab
        var dashboardTab = new TabItem { Header = LocalizationManager.GetString(LocalizationKeys.Dashboard) };

        // Improved responsive grid layout with proper spacing
        var dashboardGrid = new Grid
        {
            Margin = new Thickness(DesignTokens.Spacing.Space200),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowSpacing = DesignTokens.Spacing.Space150,
            ColumnSpacing = DesignTokens.Spacing.Space150
        };

        // System Info Card (left top)
        var systemInfoCard = CreateInfoCard(
            LocalizationManager.GetString(LocalizationKeys.SystemInformation),
            new StackPanel
            {
                Spacing = DesignTokens.Spacing.Space100,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"{Environment.OSVersion.Platform} {Environment.OSVersion.Version}",
                        FontSize = DesignTokens.Typography.FontSize.Size14,
                        FontWeight = DesignTokens.Typography.FontWeight.Medium,
                        Foreground = new SolidColorBrush(DesignTokens.SemanticColors.Text)
                    },
                    new TextBlock
                    {
                        Text = $"{LocalizationManager.GetString(LocalizationKeys.Processor)}: {Environment.ProcessorCount} cores",
                        FontSize = DesignTokens.Typography.FontSize.Size12,
                        Foreground = new SolidColorBrush(DesignTokens.SemanticColors.TextSubtle)
                    },
                    new TextBlock
                    {
                        Text = $"{LocalizationManager.GetString(LocalizationKeys.Machine)}: {Environment.MachineName}",
                        FontSize = DesignTokens.Typography.FontSize.Size12,
                        Foreground = new SolidColorBrush(DesignTokens.SemanticColors.TextSubtle)
                    }
                }
            }
        );

        Grid.SetRow(systemInfoCard, 0);
        Grid.SetColumn(systemInfoCard, 0);

        // Status Card (right top)
        var statusCard = CreateInfoCard(
            LocalizationManager.GetString(LocalizationKeys.SystemStatus),
            new StackPanel
            {
                Spacing = DesignTokens.Spacing.Space150,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = DesignTokens.Spacing.Space100,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"{LocalizationManager.GetString(LocalizationKeys.Status)}:",
                                FontSize = DesignTokens.Typography.FontSize.Size14,
                                FontWeight = DesignTokens.Typography.FontWeight.Medium,
                                Foreground = new SolidColorBrush(DesignTokens.SemanticColors.Text)
                            },
                            _statusTextBlock = new TextBlock
                            {
                                Text = $"{LocalizationManager.GetString(LocalizationKeys.Loading)}...",
                                FontSize = DesignTokens.Typography.FontSize.Size14,
                                FontWeight = DesignTokens.Typography.FontWeight.Bold,
                                Foreground = new SolidColorBrush(DesignTokens.SemanticColors.Info)
                            }
                        }
                    },
                    new TextBlock
                    {
                        Text = $"{LocalizationManager.GetString(LocalizationKeys.Drivers)}: {LocalizationManager.GetString(LocalizationKeys.Scanning)}...",
                        FontSize = DesignTokens.Typography.FontSize.Size12,
                        Foreground = new SolidColorBrush(DesignTokens.SemanticColors.TextSubtle)
                    },
                    new TextBlock
                    {
                        Text = $"{LocalizationManager.GetString(LocalizationKeys.Performance)}: {LocalizationManager.GetString(LocalizationKeys.Loading)}...",
                        FontSize = DesignTokens.Typography.FontSize.Size12,
                        Foreground = new SolidColorBrush(DesignTokens.SemanticColors.TextSubtle)
                    }
                }
            }
        );

        Grid.SetRow(statusCard, 0);
        Grid.SetColumn(statusCard, 1);

        // Action Panel (bottom, spans both columns)
        var actionPanel = new Border
        {
            Background = new SolidColorBrush(DesignTokens.SemanticColors.Surface),
            BorderBrush = new SolidColorBrush(DesignTokens.SemanticColors.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(DesignTokens.Card.BorderRadius),
            Padding = new Thickness(DesignTokens.Card.Padding),
            BoxShadow = DesignTokens.Card.Shadow
        };

        var actionStack = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = DesignTokens.Spacing.Space100
        };

        _scanButton = new ActionButton(LocalizationManager.GetString(LocalizationKeys.QuickScan), ButtonVariant.Primary);
        var refreshButton = new ActionButton(LocalizationManager.GetString(LocalizationKeys.Refresh), ButtonVariant.Secondary);
        var performanceButton = new ActionButton(LocalizationManager.GetString(LocalizationKeys.Performance), ButtonVariant.Secondary);

        actionStack.Children.Add(_scanButton);
        actionStack.Children.Add(refreshButton);
        actionStack.Children.Add(performanceButton);

        actionPanel.Child = actionStack;

        Grid.SetRow(actionPanel, 1);
        Grid.SetColumn(actionPanel, 0);
        Grid.SetColumnSpan(actionPanel, 2);

        // Recent Activity Section (bottom area)
        var activityCard = CreateInfoCard(
            LocalizationManager.GetString(LocalizationKeys.RecentActivity),
            new ScrollViewer
            {
                Content = new StackPanel
                {
                    Spacing = DesignTokens.Spacing.Space075,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = LocalizationManager.GetString(LocalizationKeys.NoRecentActivity),
                            FontSize = DesignTokens.Typography.FontSize.Size12,
                            Foreground = new SolidColorBrush(DesignTokens.SemanticColors.TextSubtle),
                            FontStyle = FontStyle.Italic
                        }
                    }
                },
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }
        );

        Grid.SetRow(activityCard, 2);
        Grid.SetColumn(activityCard, 0);
        Grid.SetColumnSpan(activityCard, 2);

        dashboardGrid.Children.Add(systemInfoCard);
        dashboardGrid.Children.Add(statusCard);
        dashboardGrid.Children.Add(actionPanel);
        dashboardGrid.Children.Add(activityCard);
        dashboardTab.Content = dashboardGrid;

        // Drivers Tab
        var driversTab = new TabItem { Header = LocalizationManager.GetString(LocalizationKeys.Drivers) };
        var driversPanel = new DockPanel { Margin = new Thickness(DesignTokens.Spacing.Space200) };

        _driverListBox = new ListBox
        {
            DockPanel.Dock = Dock.Fill,
            Margin = new Thickness(0, 0, 0, DesignTokens.Spacing.Space100),
            Background = new SolidColorBrush(DesignTokens.SemanticColors.Background),
            BorderBrush = new SolidColorBrush(DesignTokens.SemanticColors.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(DesignTokens.BorderRadius.Radius100)
        };

        var driverButtonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            DockPanel.Dock = Dock.Bottom,
            Spacing = DesignTokens.Spacing.Space100,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var updateButton = new ActionButton(LocalizationManager.GetString(LocalizationKeys.UpdateSelected), ButtonVariant.Primary);
        var backupButton = new ActionButton(LocalizationManager.GetString(LocalizationKeys.BackupSelected), ButtonVariant.Secondary);

        driverButtonPanel.Children.Add(updateButton);
        driverButtonPanel.Children.Add(backupButton);

        driversPanel.Children.Add(_driverListBox);
        driversPanel.Children.Add(driverButtonPanel);
        driversTab.Content = driversPanel;

        // Logs Tab
        var logsTab = new TabItem { Header = LocalizationManager.GetString(LocalizationKeys.Logs) };
        var logsPanel = new DockPanel { Margin = new Thickness(10) };

        _logTextBox = new TextBox
        {
            DockPanel.Dock = Dock.Fill,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Background = Brushes.Black,
            Foreground = Brushes.LightGreen
        };

        var clearLogsButton = new Button
        {
            Content = LocalizationManager.GetString(LocalizationKeys.ClearLogs),
            DockPanel.Dock = Dock.Bottom,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(15, 8, 15, 8),
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 0)
        };

        logsPanel.Children.Add(_logTextBox);
        logsPanel.Children.Add(clearLogsButton);
        logsTab.Content = logsPanel;

        _mainTabControl.Items.Add(dashboardTab);
        _mainTabControl.Items.Add(driversTab);
        _mainTabControl.Items.Add(logsTab);

        mainPanel.Children.Add(menuBar);
        mainPanel.Children.Add(_mainTabControl);

        // Accessibility and keyboard navigation
        Focusable = true;
        KeyDown += MainWindow_KeyDown;

        // Set tab order and accessibility
        _mainTabControl.TabIndex = 0;
        _mainTabControl.SetValue(Avalonia.Automation.AutomationProperties.NameProperty, "Main Navigation");

        // Ensure proper focus management
        Loaded += (s, e) =>
        {
            _scanButton?.Focus();
        };

    private void SetupEventHandlers()
    {
        _scanButton.Click += ScanButton_Click;
        _refreshButton.Click += RefreshButton_Click;
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        _ = PerformScanAsync();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadInitialData();
    }

    private void ScanMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _ = PerformScanAsync();
    }

    private void OptimizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _ = PerformOptimizationAsync();
    }

    private void BackupMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _ = PerformBackupAsync();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var aboutWindow = new Window
        {
            Title = LocalizationManager.GetString(LocalizationKeys.About),
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock
                    {
                        Text = "AeroDriver",
                        FontSize = 24,
                        FontWeight = FontWeight.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 10)
                    },
                    new TextBlock
                    {
                        Text = "Professional Windows Driver Management Suite",
                        FontSize = 14,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 20)
                    },
                    new TextBlock
                    {
                        Text = $"Version: {GetType().Assembly.GetName().Version}\nCopyright © 2024 AeroDriver Team",
                        FontSize = 12,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };

        aboutWindow.ShowDialog(this);
    }

    private async Task PerformScanAsync()
    {
        _scanButton.IsEnabled = false;
        _statusTextBlock.Text = $"{LocalizationManager.GetString(LocalizationKeys.SystemStatus)}: {LocalizationManager.GetString(LocalizationKeys.Scanning)}...";

        try
        {
            var scanResult = await Task.Run(() => _driverService.ScanSystem());
            _statusTextBlock.Text = LocalizationManager.GetFormattedString(LocalizationKeys.SystemScanComplete, scanResult.ScannedDrivers);
            LoadDriverList();
            LogMessage(LocalizationManager.GetFormattedString(LocalizationKeys.ScanCompleted, scanResult.ScannedDrivers));
        }
        catch (Exception ex)
        {
            _statusTextBlock.Text = $"{LocalizationManager.GetString(LocalizationKeys.SystemStatus)}: {LocalizationManager.GetString(LocalizationKeys.Failed)}";
            LogMessage(LocalizationManager.GetFormattedString(LocalizationKeys.ScanFailed, ex.Message));
        }
        finally
        {
            _scanButton.IsEnabled = true;
        }
    }

    private async Task PerformOptimizationAsync()
    {
        _statusTextBlock.Text = LocalizationManager.GetString(LocalizationKeys.SystemOptimizing);

        try
        {
            var result = await Task.Run(() => _driverService.OptimizeSystem());
            _statusTextBlock.Text = result.Success ? LocalizationManager.GetString(LocalizationKeys.OptimizationComplete) : LocalizationManager.GetString(LocalizationKeys.OptimizationFailed);
            LogMessage(LocalizationManager.GetFormattedString(LocalizationKeys.OptimizationResult, result.Message));
        }
        catch (Exception ex)
        {
            _statusTextBlock.Text = LocalizationManager.GetString(LocalizationKeys.OptimizationFailed);
            LogMessage(LocalizationManager.GetFormattedString(LocalizationKeys.OptimizationError, ex.Message));
        }
    }

    private async Task PerformBackupAsync()
    {
        _statusTextBlock.Text = LocalizationManager.GetString(LocalizationKeys.SystemBackup);

        try
        {
            var result = await Task.Run(() => _driverService.BackupAllDrivers());
            _statusTextBlock.Text = result.Success ? LocalizationManager.GetString(LocalizationKeys.BackupComplete) : LocalizationManager.GetString(LocalizationKeys.BackupFailed);
            LogMessage(LocalizationManager.GetFormattedString(LocalizationKeys.BackupResult, result.Message));
        }
        catch (Exception ex)
        {
            _statusTextBlock.Text = LocalizationManager.GetString(LocalizationKeys.BackupFailed);
            LogMessage(LocalizationManager.GetFormattedString(LocalizationKeys.BackupError, ex.Message));
        }
    }

    private void LoadInitialData()
    {
        _statusTextBlock.Text = $"{LocalizationManager.GetString(LocalizationKeys.SystemStatus)}: {LocalizationManager.GetString(LocalizationKeys.Loading)}...";
        LoadDriverList();
        LoadSystemInfo();
    }

    private void LoadDriverList()
    {
        _driverListBox.Items.Clear();

        try
        {
            var drivers = _driverService.GetAllDrivers();
            foreach (var driver in drivers)
            {
                // Create driver item with status badge
                var driverItem = new ListBoxItem
                {
                    Margin = new Thickness(DesignTokens.Spacing.Space050),
                    Padding = new Thickness(DesignTokens.Spacing.Space100),
                    Background = new SolidColorBrush(DesignTokens.SemanticColors.Background),
                    BorderBrush = new SolidColorBrush(DesignTokens.SemanticColors.Border),
                    BorderThickness = new Thickness(0.5),
                    CornerRadius = new CornerRadius(DesignTokens.BorderRadius.Radius100)
                };

                var driverLayout = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto")
                };

                // Driver info
                var driverInfo = new StackPanel
                {
                    Spacing = DesignTokens.Spacing.Space025
                };

                var driverName = new TextBlock
                {
                    Text = driver.Name,
                    FontSize = DesignTokens.Typography.FontSize.Size14,
                    FontWeight = DesignTokens.Typography.FontWeight.Medium,
                    Foreground = new SolidColorBrush(DesignTokens.SemanticColors.Text)
                };

                var driverVersion = new TextBlock
                {
                    Text = $"Version: {driver.Version}",
                    FontSize = DesignTokens.Typography.FontSize.Size12,
                    Foreground = new SolidColorBrush(DesignTokens.SemanticColors.TextSubtle)
                };

                driverInfo.Children.Add(driverName);
                driverInfo.Children.Add(driverVersion);

                // Status badge
                var statusBadge = new DriverStatusBadge(driver.Status);

                Grid.SetColumn(driverInfo, 0);
                Grid.SetColumn(statusBadge, 1);

                var itemGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Margin = new Thickness(0)
                };

                itemGrid.Children.Add(driverInfo);
                itemGrid.Children.Add(statusBadge);

                driverItem.Content = itemGrid;
                driverItem.Tag = driver;

                _driverListBox.Items.Add(driverItem);
            }
        }
        catch (Exception ex)
        {
            LogMessage(LocalizationManager.GetFormattedString(LocalizationKeys.LoadDriverListFailed, ex.Message));
        }
    }

    private Border CreateInfoCard(string title, Control content)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(DesignTokens.SemanticColors.Surface),
            BorderBrush = new SolidColorBrush(DesignTokens.SemanticColors.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(DesignTokens.Card.BorderRadius),
            Padding = new Thickness(DesignTokens.Card.Padding),
            BoxShadow = DesignTokens.Card.Shadow
        };

        var layout = new StackPanel
        {
            Spacing = DesignTokens.Spacing.Space100
        };

        // Card title
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = DesignTokens.Typography.FontSize.Size16,
            FontWeight = DesignTokens.Typography.FontWeight.SemiBold,
            Foreground = new SolidColorBrush(DesignTokens.SemanticColors.Text),
            Margin = new Thickness(0, 0, 0, DesignTokens.Spacing.Space050)
        };

        layout.Children.Add(titleText);
        layout.Children.Add(content);
        card.Child = layout;

        return card;
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        // Global keyboard shortcuts
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            switch (e.Key)
            {
                case Key.R:
                    // Ctrl+R: Refresh
                    e.Handled = true;
                    LoadInitialData();
                    break;
                case Key.S:
                    // Ctrl+S: Scan
                    e.Handled = true;
                    _ = PerformScanAsync();
                    break;
                case Key.L:
                    // Ctrl+L: Focus logs tab
                    e.Handled = true;
                    _mainTabControl.SelectedIndex = 2; // Logs tab
                    break;
                case Key.D:
                    // Ctrl+D: Focus drivers tab
                    e.Handled = true;
                    _mainTabControl.SelectedIndex = 1; // Drivers tab
                    break;
            }
        }
        else if (e.Key == Key.F5)
        {
            // F5: Refresh
            e.Handled = true;
            LoadInitialData();
        }
        else if (e.Key == Key.Escape)
        {
            // Escape: Close any open dialogs or focus main content
            e.Handled = true;
            Focus();
        }
    }

    private void LogMessage(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var logEntry = $"[{timestamp}] {message}";

        Dispatcher.UIThread.Post(() =>
        {
            if (_logTextBox != null)
            {
                _logTextBox.Append(logEntry + Environment.NewLine);
                _logTextBox.CaretIndex = _logTextBox.Text.Length;
            }
        });

        _logger.LogInformation(message);
    }
}