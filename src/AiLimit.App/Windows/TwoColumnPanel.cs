using System.Windows;
using System.Windows.Controls;

namespace AiLimit.App.Windows;

/// <summary>
/// A two-column panel that distributes children in alternating columns
/// (even indices → left, odd indices → right). Each column stacks items
/// independently so shorter cards in one column do not create blank gaps
/// next to taller cards in the other column.
/// </summary>
public sealed class TwoColumnPanel : System.Windows.Controls.Panel
{
    public static readonly DependencyProperty ColumnGapProperty =
        DependencyProperty.Register(
            nameof(ColumnGap),
            typeof(double),
            typeof(TwoColumnPanel),
            new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double ColumnGap
    {
        get => (double)GetValue(ColumnGapProperty);
        set => SetValue(ColumnGapProperty, value);
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
    {
        var gap = ColumnGap;
        var columnWidth = Math.Max(0, (availableSize.Width - gap) / 2);
        var childConstraint = new System.Windows.Size(columnWidth, double.PositiveInfinity);

        double leftHeight = 0;
        double rightHeight = 0;

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(childConstraint);

            if (i % 2 == 0)
            {
                leftHeight += child.DesiredSize.Height;
            }
            else
            {
                rightHeight += child.DesiredSize.Height;
            }
        }

        return new System.Windows.Size(availableSize.Width, Math.Max(leftHeight, rightHeight));
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
    {
        var gap = ColumnGap;
        var columnWidth = Math.Max(0, (finalSize.Width - gap) / 2);

        double leftY = 0;
        double rightY = 0;

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];

            if (i % 2 == 0)
            {
                child.Arrange(new Rect(0, leftY, columnWidth, child.DesiredSize.Height));
                leftY += child.DesiredSize.Height;
            }
            else
            {
                child.Arrange(new Rect(columnWidth + gap, rightY, columnWidth, child.DesiredSize.Height));
                rightY += child.DesiredSize.Height;
            }
        }

        return finalSize;
    }
}
