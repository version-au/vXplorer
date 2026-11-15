using System.Windows;
using System.Windows.Media;

namespace vXplorer.Ui
{
    /// <summary>
    /// vXplorer's icon drawing (AppIcon.Drawer, set in Program): a folder outline in the Main Accent holding vShell's three-dot mark (Main
    /// Accent, Secondary Accent, text), on a 64 x 64 grid. The folder is filled with the theme's background, so the dots read on any
    /// surface (a light Explorer window, a dark taskbar). small: a heavier outline and bigger dots that stay readable at 16-24 px.
    /// `vXplorer.exe --make-icon vxplorer.ico` writes it in vShell's default colours for the exe's own icon.
    /// </summary>
    public static class Brand
    {
        static readonly Geometry Folder = Geometry.Parse("M8,20 A5,5 0 0 1 13,15 H24.5 L29.5,20 H51 A5,5 0 0 1 56,25 V47 A5,5 0 0 1 51,52 H13 A5,5 0 0 1 8,47 Z");

        public static Drawing Draw(Color main, Color secondary, Color text, Color background, bool small)
        {
            double stroke = small ? 5.5 : 4, r = small ? 5.6 : 4.6, y1 = small ? 32 : 31, y2 = small ? 43 : 42, dx = small ? 7.5 : 7;
            var g = new DrawingGroup();
            var pen = new Pen(AppIcon.Brush(main), stroke) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            g.Children.Add(new GeometryDrawing(AppIcon.Brush(background), pen, Folder));
            g.Children.Add(new GeometryDrawing(AppIcon.Brush(main), null, new EllipseGeometry(new Point(32 - dx, y1), r, r)));
            g.Children.Add(new GeometryDrawing(AppIcon.Brush(secondary), null, new EllipseGeometry(new Point(32 + dx, y1), r, r)));
            g.Children.Add(new GeometryDrawing(AppIcon.Brush(text), null, new EllipseGeometry(new Point(32, y2), r, r)));
            g.Freeze();
            return g;
        }
    }
}