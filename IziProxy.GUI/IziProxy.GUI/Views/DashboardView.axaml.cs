using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using IziProxy.GUI.ViewModels;

namespace IziProxy.GUI.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            vm.ChartSegments.CollectionChanged += OnChartSegmentsChanged;
            RebuildChart(vm);
        }
    }

    private void OnChartSegmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
            RebuildChart(vm);
    }

    /// <summary>
    /// Динамически строит сегменты Stacked Bar Chart и легенду
    /// на основе текущей коллекции ChartSegments.
    /// </summary>
    private void RebuildChart(DashboardViewModel vm)
    {
        var chartGrid = this.FindControl<Grid>("ChartBarGrid");
        var legendPanel = this.FindControl<WrapPanel>("ChartLegendPanel");
        if (chartGrid == null || legendPanel == null) return;

        chartGrid.ColumnDefinitions.Clear();
        chartGrid.Children.Clear();
        legendPanel.Children.Clear();

        for (int i = 0; i < vm.ChartSegments.Count; i++)
        {
            var seg = vm.ChartSegments[i];

            // star-ширина пропорционально доле трафика
            int starWidth = Math.Max(1, (int)Math.Round(seg.WidthFraction * 100));
            chartGrid.ColumnDefinitions.Add(
                new ColumnDefinition(starWidth, GridUnitType.Star));

            // Цветной сегмент полоски
            var segBorder = new Border
            {
                Background = SolidColorBrush.Parse(seg.Color),
                [ToolTip.TipProperty] = seg.Label,
                [ToolTip.ShowDelayProperty] = 100,
            };
            Grid.SetColumn(segBorder, i);
            chartGrid.Children.Add(segBorder);

            // Элемент легенды
            var dot = new Avalonia.Controls.Shapes.Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = SolidColorBrush.Parse(seg.Color),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };

            var label = new TextBlock
            {
                Text = seg.Label,
                FontSize = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            // Наследуем стиль цвета текста из ресурсов
            if (this.TryFindResource("TextBrush", ActualThemeVariant, out var textBrush)
                && textBrush is IBrush foundBrush)
            {
                label.Foreground = foundBrush;
            }

            var legendItem = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 0, 16, 0),
            };
            legendItem.Children.Add(dot);
            legendItem.Children.Add(label);
            legendPanel.Children.Add(legendItem);
        }
    }
}
