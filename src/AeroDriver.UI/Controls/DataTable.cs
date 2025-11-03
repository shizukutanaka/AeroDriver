using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AeroDriver.Core.UI;

namespace AeroDriver.UI.Controls;

/// <summary>
/// Atlassian-inspired data table component
/// </summary>
[SupportedOSPlatform("windows")]
public class DataTable : Border
{
    private Grid _grid;
    private readonly List<string> _headers;
    private readonly List<object[]> _rows;

    public DataTable(string[] headers)
    {
        _headers = new List<string>(headers);
        _rows = new List<object[]>();
        Initialize();
    }

    private void Initialize()
    {
        // Table styling
        BorderBrush = new SolidColorBrush(Color.Parse(DesignTokens.Colors.Border));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(DesignTokens.BorderRadius.Medium);
        Background = Brushes.White;

        // Create grid
        _grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            ColumnDefinitions = CreateColumnDefinitions()
        };

        // Add header row
        AddHeaderRow();

        // Scroll viewer for data rows
        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        Grid.SetRow(scrollViewer, 1);
        _grid.Children.Add(scrollViewer);

        Child = _grid;
    }

    private ColumnDefinitions CreateColumnDefinitions()
    {
        var columns = new ColumnDefinitions();
        foreach (var _ in _headers)
        {
            columns.Add(new ColumnDefinition(GridLength.Star));
        }
        return columns;
    }

    private void AddHeaderRow()
    {
        var headerPanel = new Grid
        {
            Background = new SolidColorBrush(Color.Parse(DesignTokens.Colors.BackgroundSecondary)),
            ColumnDefinitions = CreateColumnDefinitions(),
            Height = 40
        };

        for (int i = 0; i < _headers.Count; i++)
        {
            var headerCell = new Border
            {
                BorderBrush = new SolidColorBrush(Color.Parse(DesignTokens.Colors.Border)),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(DesignTokens.Spacing.Medium),
                Child = new TextBlock
                {
                    Text = _headers[i],
                    FontSize = DesignTokens.Typography.BodySize,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse(DesignTokens.Colors.TextPrimary)),
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            Grid.SetColumn(headerCell, i);
            headerPanel.Children.Add(headerCell);
        }

        Grid.SetRow(headerPanel, 0);
        _grid.Children.Add(headerPanel);
    }

    public void AddRow(params object[] values)
    {
        _rows.Add(values);
        RenderRows();
    }

    public void AddRows(IEnumerable<object[]> rows)
    {
        _rows.AddRange(rows);
        RenderRows();
    }

    public void ClearRows()
    {
        _rows.Clear();
        RenderRows();
    }

    private void RenderRows()
    {
        // Remove existing data rows
        var rowsPanel = _grid.Children.OfType<ScrollViewer>().FirstOrDefault()?.Content as StackPanel;
        if (rowsPanel == null)
        {
            rowsPanel = new StackPanel();
            var scrollViewer = _grid.Children.OfType<ScrollViewer>().FirstOrDefault();
            if (scrollViewer != null)
            {
                scrollViewer.Content = rowsPanel;
            }
        }
        else
        {
            rowsPanel.Children.Clear();
        }

        // Add data rows
        foreach (var row in _rows)
        {
            var rowGrid = new Grid
            {
                ColumnDefinitions = CreateColumnDefinitions(),
                MinHeight = 36
            };

            for (int i = 0; i < Math.Min(_headers.Count, row.Length); i++)
            {
                var cell = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.Parse(DesignTokens.Colors.Border)),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(DesignTokens.Spacing.Medium, DesignTokens.Spacing.Small,
                                           DesignTokens.Spacing.Medium, DesignTokens.Spacing.Small),
                    Child = new TextBlock
                    {
                        Text = row[i]?.ToString() ?? "",
                        FontSize = DesignTokens.Typography.BodySize,
                        Foreground = new SolidColorBrush(Color.Parse(DesignTokens.Colors.TextPrimary)),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                };

                Grid.SetColumn(cell, i);
                rowGrid.Children.Add(cell);
            }

            // Hover effect
            rowGrid.PointerEntered += (s, e) =>
            {
                foreach (var child in rowGrid.Children.OfType<Border>())
                {
                    child.Background = new SolidColorBrush(Color.Parse(DesignTokens.Colors.BackgroundSecondary));
                }
            };

            rowGrid.PointerExited += (s, e) =>
            {
                foreach (var child in rowGrid.Children.OfType<Border>())
                {
                    child.Background = Brushes.Transparent;
                }
            };

            rowsPanel.Children.Add(rowGrid);
        }
    }
}
