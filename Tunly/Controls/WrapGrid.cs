using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Tunly.Controls;

/// <summary>
/// A simple fluid grid: children are laid out with a minimum width and wrap
/// onto new rows as the available width shrinks. Used for the dashboard cards
/// so the layout stays balanced at any window size.
/// </summary>
public sealed class WrapGrid : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(WrapGrid), new PropertyMetadata(260d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ItemSpacingProperty = DependencyProperty.Register(
        nameof(ItemSpacing), typeof(double), typeof(WrapGrid), new PropertyMetadata(16d, OnLayoutPropertyChanged));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double ItemSpacing
    {
        get => (double)GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((WrapGrid)d).InvalidateMeasure();

    int ColumnsFor(double width)
    {
        var spacing = ItemSpacing;
        var item = Math.Max(1, MinItemWidth);
        if (double.IsInfinity(width) || width <= 0) return 1;
        return Math.Max(1, (int)Math.Floor((width + spacing) / (item + spacing)));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MinItemWidth : availableSize.Width;
        var columns = ColumnsFor(width);
        var spacing = ItemSpacing;
        var itemWidth = Math.Max(0, (width - spacing * (columns - 1)) / columns);

        var rowHeights = new double[(Children.Count + columns - 1) / columns];
        var index = 0;
        var height = 0.0;

        foreach (var child in Children)
        {
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            var row = index / columns;
            rowHeights[row] = Math.Max(rowHeights[row], child.DesiredSize.Height);
            index++;
        }

        for (var i = 0; i < rowHeights.Length; i++)
        {
            height += rowHeights[i];
            if (i < rowHeights.Length - 1) height += spacing;
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = ColumnsFor(finalSize.Width);
        var spacing = ItemSpacing;
        var itemWidth = Math.Max(0, (finalSize.Width - spacing * (columns - 1)) / columns);
        var y = 0.0;
        var rowHeight = 0.0;
        var index = 0;

        foreach (var child in Children)
        {
            var column = index % columns;
            if (column == 0 && index > 0)
            {
                y += rowHeight + spacing;
                rowHeight = 0;
            }

            child.Arrange(new Rect(column * (itemWidth + spacing), y, itemWidth, child.DesiredSize.Height));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            index++;
        }

        return finalSize;
    }
}
