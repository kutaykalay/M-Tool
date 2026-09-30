using System.Drawing;
using System.Windows.Forms;

namespace MTool.App.Tray;

/// <summary>Tray menu colours for the dark theme; the light theme uses the system renderer.</summary>
internal static class TrayMenuRenderer
{
    private static readonly Color Background = Color.FromArgb(0x26, 0x28, 0x2C);
    private static readonly Color Hover = Color.FromArgb(0x30, 0x34, 0x3B);
    private static readonly Color Border = Color.FromArgb(0x3A, 0x3D, 0x43);
    private static readonly Color Text = Color.FromArgb(0xEC, 0xED, 0xEF);
    private static readonly Color DisabledText = Color.FromArgb(0x80, 0x84, 0x8B);

    public static ToolStripRenderer For(bool dark) =>
        dark ? new DarkRenderer() : new ToolStripProfessionalRenderer();

    private sealed class DarkRenderer() : ToolStripProfessionalRenderer(new DarkColors())
    {
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : DisabledText;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var box = e.ImageRectangle;
            using var pen = new Pen(Text, 2);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(pen, [
                new PointF(box.Left + box.Width * 0.2f, box.Top + box.Height * 0.5f),
                new PointF(box.Left + box.Width * 0.42f, box.Top + box.Height * 0.72f),
                new PointF(box.Left + box.Width * 0.8f, box.Top + box.Height * 0.28f),
            ]);
        }
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Background;

        public override Color ImageMarginGradientBegin => Background;

        public override Color ImageMarginGradientMiddle => Background;

        public override Color ImageMarginGradientEnd => Background;

        public override Color MenuBorder => Border;

        public override Color MenuItemBorder => Hover;

        public override Color MenuItemSelected => Hover;

        public override Color SeparatorDark => Border;

        public override Color SeparatorLight => Border;

        public override Color CheckBackground => Background;

        public override Color CheckSelectedBackground => Hover;

        public override Color CheckPressedBackground => Hover;
    }
}
