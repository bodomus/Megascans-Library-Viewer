using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ScanVault.App.Controls;

/// <summary>
/// Fixed-cell virtualizing wrap panel. Only the rows intersecting the viewport
/// are realized, keeping thousands of thumbnail cards out of the visual tree.
/// </summary>
public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth),
        typeof(double),
        typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(190d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight),
        typeof(double),
        typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(210d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private Size extent;
    private Size viewport;
    private Point scrollOffset;
    private int itemsPerRow = 1;

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width)
            ? Math.Max(ItemWidth, ActualWidth)
            : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height)
            ? Math.Max(ItemHeight, ActualHeight)
            : availableSize.Height;
        viewport = new(Math.Max(0, width), Math.Max(0, height));
        itemsPerRow = Math.Max(1, (int)Math.Floor(viewport.Width / ItemWidth));

        var itemCount = ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;
        var rowCount = (int)Math.Ceiling(itemCount / (double)itemsPerRow);
        extent = new(viewport.Width, rowCount * ItemHeight);
        CoerceOffsets();
        ScrollOwner?.InvalidateScrollInfo();

        if (itemCount == 0)
        {
            RemoveInternalChildRange(0, InternalChildren.Count);
            return availableSize;
        }

        var firstRow = Math.Max(0, (int)Math.Floor(VerticalOffset / ItemHeight));
        var visibleRows = Math.Max(1, (int)Math.Ceiling(ViewportHeight / ItemHeight) + 1);
        var firstIndex = Math.Min(itemCount - 1, firstRow * itemsPerRow);
        var lastIndex = Math.Min(itemCount - 1, (firstRow + visibleRows) * itemsPerRow - 1);
        RealizeRange(firstIndex, lastIndex);
        CleanupRange(firstIndex, lastIndex);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
        {
            var position = new GeneratorPosition(childIndex, 0);
            var itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(position);
            if (itemIndex < 0)
            {
                continue;
            }

            var row = itemIndex / itemsPerRow;
            var column = itemIndex % itemsPerRow;
            InternalChildren[childIndex].Arrange(new(
                column * ItemWidth,
                row * ItemHeight - VerticalOffset,
                ItemWidth,
                ItemHeight));
        }

        return finalSize;
    }

    private void RealizeRange(int firstIndex, int lastIndex)
    {
        var start = ItemContainerGenerator.GeneratorPositionFromIndex(firstIndex);
        var childIndex = start.Offset == 0 ? start.Index : start.Index + 1;
        using var generationScope = ItemContainerGenerator.StartAt(
            start,
            GeneratorDirection.Forward,
            allowStartAtRealizedItem: true);

        for (var itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++, childIndex++)
        {
            var child = (UIElement)ItemContainerGenerator.GenerateNext(out var newlyRealized);
            if (newlyRealized)
            {
                if (childIndex >= InternalChildren.Count)
                {
                    AddInternalChild(child);
                }
                else
                {
                    InsertInternalChild(childIndex, child);
                }

                ItemContainerGenerator.PrepareItemContainer(child);
            }

            child.Measure(new(ItemWidth, ItemHeight));
        }
    }

    private void CleanupRange(int firstIndex, int lastIndex)
    {
        for (var childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
        {
            var position = new GeneratorPosition(childIndex, 0);
            var itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(position);
            if (itemIndex >= firstIndex && itemIndex <= lastIndex)
            {
                continue;
            }

            ItemContainerGenerator.Remove(position, 1);
            RemoveInternalChildRange(childIndex, 1);
        }
    }

    private void CoerceOffsets()
    {
        scrollOffset.X = 0;
        scrollOffset.Y = Math.Max(0, Math.Min(scrollOffset.Y, Math.Max(0, ExtentHeight - ViewportHeight)));
    }

    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; } = true;
    public double ExtentWidth => extent.Width;
    public double ExtentHeight => extent.Height;
    public double ViewportWidth => viewport.Width;
    public double ViewportHeight => viewport.Height;
    public double HorizontalOffset => scrollOffset.X;
    public double VerticalOffset => scrollOffset.Y;
    public ScrollViewer? ScrollOwner { get; set; }

    public void LineUp() => SetVerticalOffset(VerticalOffset - 24);
    public void LineDown() => SetVerticalOffset(VerticalOffset + 24);
    public void LineLeft() { }
    public void LineRight() { }
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - 72);
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + 72);
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void PageLeft() { }
    public void PageRight() { }
    public void SetHorizontalOffset(double offset) { }

    public void SetVerticalOffset(double offset)
    {
        var coerced = Math.Max(0, Math.Min(offset, Math.Max(0, ExtentHeight - ViewportHeight)));
        if (Math.Abs(coerced - scrollOffset.Y) < 0.1)
        {
            return;
        }

        scrollOffset.Y = coerced;
        InvalidateMeasure();
        ScrollOwner?.InvalidateScrollInfo();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;
}
