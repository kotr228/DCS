namespace GrayCat.UI.Controls;

using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GrayCat.UI.ViewModels;

public partial class DraggableBlock : UserControl
{
    public DraggableBlock()
    {
        InitializeComponent();
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is not BlockViewModel block)
            return;

        // Round to 10px so sizes stay tidy; the view model clamps to the allowed range.
        block.Width = Math.Round((block.Width + e.HorizontalChange) / 10) * 10;
        block.Height = Math.Round((block.Height + e.VerticalChange) / 10) * 10;
        e.Handled = true;
    }
}
