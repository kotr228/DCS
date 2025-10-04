using System.Drawing;
using System.Windows.Forms;

public class NoBorderRenderer : ToolStripProfessionalRenderer
{
    public NoBorderRenderer() : base(new NoBorderColorTable()) { }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // ❌ Взагалі не малюємо рамку
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        Rectangle rect = new Rectangle(Point.Empty, e.Item.Size);
        Color bg = e.Item.Selected ? Color.FromArgb(60, 60, 60) : Color.FromArgb(36, 36, 36);
        using (var brush = new SolidBrush(bg))
        {
            e.Graphics.FillRectangle(brush, rect);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Color.White; 
        base.OnRenderItemText(e);
    }
}

public class NoBorderColorTable : ProfessionalColorTable
{
    public override Color ToolStripBorder => Color.FromArgb(36, 36, 36);
    public override Color MenuBorder => Color.FromArgb(36, 36, 36);
    public override Color MenuItemSelected => Color.FromArgb(60, 60, 60);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(60, 60, 60);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(60, 60, 60);
    public override Color ToolStripDropDownBackground => Color.FromArgb(36, 36, 36);
    public override Color ImageMarginGradientBegin => Color.FromArgb(36, 36, 36);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(36, 36, 36);
    public override Color ImageMarginGradientEnd => Color.FromArgb(36, 36, 36);
}
