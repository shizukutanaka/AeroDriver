using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AeroDriver.Core;
using AeroDriver.Core.Services;
using AeroDriver.Core.UI;
using AeroDriver.UI.Controls;

namespace AeroDriver.UI.Views;

/// <summary>
/// Improved dashboard with Atlassian Design System principles
/// </summary>
[SupportedOSPlatform("windows")]
public class ImprovedDashboard : UserControl
{
    private readonly ISimpleLogger _logger;
    private readonly CoreDriverService _driverService;

    private StatusCard _totalDriversCard;
    private StatusCard _problemDriversCard;
    private StatusCard _systemHealthCard;
    private StatusCard _performanceCard;
    private DataTable _recentIssuesTable;
    private NotificationBanner? _notificationBanner;
    private StackPanel _notificationPanel;

    public ImprovedDashboard(ISimpleLogger logger, CoreDriverService driverService)
    {
        _logger = logger;
        _driverService = driverService;
        InitializeComponent();
        LoadDataAsync();
    }

    private void InitializeComponent()
    {
        var mainPanel = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(DesignTokens.Spacing.XLarge)
        };

        var content = new StackPanel { Spacing = DesignTokens.Spacing.XLarge };

        // Page header
        var header = new StackPanel { Spacing = DesignTokens.Spacing.Small };
        header.Children.Add(new TextBlock
        {
            Text = "Dashboard",
            FontSize = DesignTokens.Typography.H1Size,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse(DesignTokens.Colors.TextPrimary))
        });
        header.Children.Add(new TextBlock
        {
            Text = "System overview and health status",
            FontSize = DesignTokens.Typography.BodySize,
            Foreground = new SolidColorBrush(Color.Parse(DesignTokens.Colors.TextSecondary))
        });
        content.Children.Add(header);

        // Notification area
        _notificationPanel = new StackPanel { Spacing = DesignTokens.Spacing.Small };
        content.Children.Add(_notificationPanel);

        // Status cards grid
        var cardsGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            RowDefinitions = new RowDefinitions("Auto")
        };

        _totalDriversCard = new StatusCard("Total Drivers", "...", StatusLevel.Info);
        Grid.SetColumn(_totalDriversCard, 0);
        cardsGrid.Children.Add(_totalDriversCard);

        _problemDriversCard = new StatusCard("Problem Drivers", "...", StatusLevel.Success);
        Grid.SetColumn(_problemDriversCard, 1);
        cardsGrid.Children.Add(_problemDriversCard);

        _systemHealthCard = new StatusCard("System Health", "...", StatusLevel.Info);
        Grid.SetColumn(_systemHealthCard, 2);
        cardsGrid.Children.Add(_systemHealthCard);

        _performanceCard = new StatusCard("Performance", "...", StatusLevel.Info);
        Grid.SetColumn(_performanceCard, 3);
        cardsGrid.Children.Add(_performanceCard);

        content.Children.Add(cardsGrid);

        // Quick actions section
        var actionsSection = CreateActionsSection();
        content.Children.Add(actionsSection);

        // Recent issues section
        var issuesSection = CreateIssuesSection();
        content.Children.Add(issuesSection);

        mainPanel.Content = content;
        Content = mainPanel;
    }

    private Border CreateActionsSection()
    {
        var section = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse(DesignTokens.Colors.Border)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(DesignTokens.BorderRadius.Medium),
            Padding = new Thickness(DesignTokens.Spacing.XLarge)
        };

        var panel = new StackPanel { Spacing = DesignTokens.Spacing.Large };

        panel.Children.Add(new TextBlock
        {
            Text = "Quick Actions",
            FontSize = DesignTokens.Typography.H3Size,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse(DesignTokens.Colors.TextPrimary))
        });

        var buttonsPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = 180
        };

        buttonsPanel.Children.Add(new ActionButton("Scan System", ButtonVariant.Primary, "🔍"));
        buttonsPanel.Children.Add(new ActionButton("View Problems", ButtonVariant.Secondary, "⚠"));
        buttonsPanel.Children.Add(new ActionButton("Optimize", ButtonVariant.Secondary, "⚡"));
        buttonsPanel.Children.Add(new ActionButton("Backup Drivers", ButtonVariant.Tertiary, "💾"));
        buttonsPanel.Children.Add(new ActionButton("Update Drivers", ButtonVariant.Tertiary, "⬆"));
        buttonsPanel.Children.Add(new ActionButton("Settings", ButtonVariant.Tertiary, "⚙"));

        panel.Children.Add(buttonsPanel);
        section.Child = panel;

        return section;
    }

    private Border CreateIssuesSection()
    {
        var section = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse(DesignTokens.Colors.Border)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(DesignTokens.BorderRadius.Medium),
            Padding = new Thickness(DesignTokens.Spacing.XLarge)
        };

        var panel = new StackPanel { Spacing = DesignTokens.Spacing.Large };

        panel.Children.Add(new TextBlock
        {
            Text = "Recent Issues",
            FontSize = DesignTokens.Typography.H3Size,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse(DesignTokens.Colors.TextPrimary))
        });

        _recentIssuesTable = new DataTable(new[] { "Driver Name", "Status", "Type", "Last Updated" });
        panel.Children.Add(_recentIssuesTable);

        section.Child = panel;
        return section;
    }

    private async void LoadDataAsync()
    {
        try
        {
            // Show loading notification
            ShowNotification("Loading system data...", StatusLevel.Info);

            await Task.Run(() =>
            {
                // Scan system
                var scanResult = _driverService.ScanSystem();
                var drivers = _driverService.GetAllDrivers();
                var problemDrivers = drivers.Where(d =>
                    d.Status.Contains("Problem", StringComparison.OrdinalIgnoreCase) ||
                    d.Status.Contains("Error", StringComparison.OrdinalIgnoreCase)
                ).ToList();

                // Update UI on main thread
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    UpdateDashboard(drivers, problemDrivers, scanResult);
                });
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Dashboard load error: {ex.Message}");
            ShowNotification($"Error loading dashboard: {ex.Message}", StatusLevel.Error);
        }
    }

    private void UpdateDashboard(List<DriverInfo> drivers, List<DriverInfo> problemDrivers, ScanResult scanResult)
    {
        // Update status cards
        _totalDriversCard.UpdateValue(drivers.Count.ToString(), StatusLevel.Info);

        var problemStatus = problemDrivers.Count == 0 ? StatusLevel.Success :
                           problemDrivers.Count < 3 ? StatusLevel.Warning : StatusLevel.Error;
        _problemDriversCard.UpdateValue(problemDrivers.Count.ToString(), problemStatus);

        var healthPercentage = drivers.Count > 0
            ? (int)((drivers.Count - problemDrivers.Count) / (double)drivers.Count * 100)
            : 100;
        var healthStatus = healthPercentage >= 90 ? StatusLevel.Success :
                          healthPercentage >= 70 ? StatusLevel.Warning : StatusLevel.Error;
        _systemHealthCard.UpdateValue($"{healthPercentage}%", healthStatus);

        _performanceCard.UpdateValue("Good", StatusLevel.Success);

        // Update recent issues table
        _recentIssuesTable.ClearRows();
        var issueRows = problemDrivers.Take(10).Select(d => new object[]
        {
            d.Name,
            d.Status,
            d.Type,
            DateTime.Now.ToString("yyyy-MM-dd")
        }).ToList();

        _recentIssuesTable.AddRows(issueRows);

        // Update notification
        if (problemDrivers.Any())
        {
            ShowNotification(
                $"Found {problemDrivers.Count} driver issue(s) requiring attention.",
                StatusLevel.Warning
            );
        }
        else
        {
            ShowNotification("All drivers are functioning normally. System is healthy!", StatusLevel.Success);
        }
    }

    private void ShowNotification(string message, StatusLevel level)
    {
        _notificationPanel.Children.Clear();
        _notificationBanner = new NotificationBanner(message, level, true);
        _notificationBanner.Dismissed += (s, e) =>
        {
            _notificationPanel.Children.Clear();
        };
        _notificationPanel.Children.Add(_notificationBanner);
    }
}
