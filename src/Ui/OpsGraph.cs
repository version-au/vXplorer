using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace vXplorer.Ui
{
    /// <summary>
    /// An operation's progress in the file operations panel, drawn the way vShell's CPU module draws its graphs (vShell's MetricView -
    /// copied, vXplorer doesn't reference vShell): a Meter (a rounded bar), a Ring (an arc from 12 o'clock with the percent inside) or a
    /// Line - the speed along the way, like Windows' copy window: across = how far through, up = how fast, filled under the line as far as
    /// it has got. The line's look (thickness, fill, curves, box) is vShell's Graph settings.
    /// </summary>
    public sealed class OpsGraph : FrameworkElement
    {
        public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(OpsGraph),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TrackProperty = DependencyProperty.Register("Track", typeof(Brush), typeof(OpsGraph),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty CenterBrushProperty = DependencyProperty.Register("CenterBrush", typeof(Brush), typeof(OpsGraph),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Fill { get { return (Brush)GetValue(FillProperty); } set { SetValue(FillProperty, value); } }
        public Brush Track { get { return (Brush)GetValue(TrackProperty); } set { SetValue(TrackProperty, value); } }
        public Brush CenterBrush { get { return (Brush)GetValue(CenterBrushProperty); } set { SetValue(CenterBrushProperty, value); } }

        /// <summary>Meter, Line or Ring.</summary>
        public string GraphStyle = "Meter";
        public double GraphW = 60, GraphH = 6;
        /// <summary>0..100.</summary>
        public double Value;
        /// <summary>Ring: the text in the middle ("34%").</summary>
        public string Center;
        /// <summary>Line: (how far 0..1, speed) points, oldest first.</summary>
        public IList<Tuple<double, double>> Speeds;

        // ---- the line's look (vShell's Graph settings, same names and values) ----
        public double LineWidth = 1.5;
        public string AreaFill = "Light";
        public bool SmoothLine;
        public bool ShowBack = true;
        public double Radius = 3;

        public static double LineWidthOf(string name)
        {
            switch (name) { case "Thin": return 1; case "Thick": return 2.5; case "Heavy": return 3.5; default: return 1.5; }
        }

        protected override Size MeasureOverride(Size availableSize) { return new Size(GraphW, GraphH); }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 2 || h < 2) return;
            switch (GraphStyle)
            {
                case "Line": DrawLine(dc, w, h); break;
                case "Ring": DrawRing(dc, w, h); break;
                default: DrawMeter(dc, w, h); break;
            }
        }

        static double Clamp100(double v) { return Math.Max(0, Math.Min(100, v)); }

        void DrawMeter(DrawingContext dc, double w, double h)
        {
            double th = Math.Max(4, Math.Min(10, h * 0.4));
            double y = (h - th) / 2;
            dc.DrawRoundedRectangle(Track, null, new Rect(0, y, w, th), th / 2, th / 2);
            double fw = w * Clamp100(Value) / 100.0;
            if (fw >= 1)
            {
                double r = Math.Min(th / 2, fw / 2);
                dc.DrawRoundedRectangle(Fill, null, new Rect(0, y, fw, th), r, r);
            }
        }

        /// <summary>A round gauge: the track, an arc for the value from 12 o'clock clockwise, the value text in the middle.</summary>
        void DrawRing(DrawingContext dc, double w, double h)
        {
            double d = Math.Min(w, h);
            double th = Math.Max(2.5, Math.Round(d * 0.12, 1));
            var c = new Point(w / 2, h / 2);
            double r = d / 2 - th / 2;
            if (r < 2) return;
            dc.DrawEllipse(null, new Pen(Track, th), c, r, r);
            double v = Clamp100(Value);
            if (v >= 99.9) dc.DrawEllipse(null, new Pen(Fill, th), c, r, r);
            else if (v > 0.3)
            {
                double a = v / 100.0 * 2 * Math.PI;
                var s = new Point(c.X, c.Y - r);
                var e = new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
                var g = new StreamGeometry();
                using (StreamGeometryContext ctx = g.Open())
                {
                    ctx.BeginFigure(s, false, false);
                    ctx.ArcTo(e, new Size(r, r), 0, v > 50, SweepDirection.Clockwise, true, false);
                }
                g.Freeze();
                dc.DrawGeometry(null, new Pen(Fill, th) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
            }
            if (string.IsNullOrEmpty(Center)) return;
            var face = new Typeface((FontFamily)GetValue(TextElement.FontFamilyProperty), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            double size = Math.Max(6, d * 0.3);
            var ft = new FormattedText(Center, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, CenterBrush,
                                       VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double room = (r - th / 2) * 2 * 0.86;
            if (ft.Width > room && ft.Width > 0) ft.SetFontSize(Math.Max(5, size * room / ft.Width));
            dc.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));
        }

        /// <summary>The speed along the way: the box, then the line (and the fill under it) from the start to how far it has got.</summary>
        void DrawLine(DrawingContext dc, double w, double h)
        {
            if (ShowBack) dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), Radius, Radius);
            List<Tuple<double, double>> data;
            if (Speeds == null) return;
            lock (Speeds) data = new List<Tuple<double, double>>(Speeds);
            if (data.Count < 2) return;

            // the scale: the fastest it has been, rounded up to 1, 2 or 5 × 10ⁿ (vShell's automatic scale), so the line never hits the top
            double top = 0;
            foreach (var p in data) top = Math.Max(top, p.Item2);
            if (top <= 0) top = 1;
            double full = Nice(top * 1.1);

            var pts = new Point[data.Count];
            for (int i = 0; i < data.Count; i++)
                pts[i] = new Point(Math.Max(0, Math.Min(1, data[i].Item1)) * w, 1 + (h - 2) * (1 - Clamp100(data[i].Item2 / full * 100) / 100.0));
            int m = pts.Length;

            if (AreaFill != "None")
            {
                var area = new StreamGeometry();
                using (StreamGeometryContext ctx = area.Open())
                {
                    ctx.BeginFigure(new Point(pts[0].X, h - 1), true, true);
                    ctx.LineTo(pts[0], false, false);
                    Trace(ctx, pts, h, false);
                    ctx.LineTo(new Point(pts[m - 1].X, h - 1), false, false);
                }
                area.Freeze();
                if (AreaFill == "Fade")
                {
                    // strong just under the line's highest point, fading out towards the bottom
                    double hi = h;
                    foreach (Point pt in pts) hi = Math.Min(hi, pt.Y);
                    var mask = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, hi), EndPoint = new Point(0, h) };
                    mask.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0, 0, 0), 0));
                    mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
                    mask.Freeze();
                    dc.PushOpacityMask(mask);
                }
                else dc.PushOpacity(AreaFill == "Solid" ? 0.55 : 0.2);
                dc.DrawGeometry(Fill, null, area);
                dc.Pop();
            }

            var line = new StreamGeometry();
            using (StreamGeometryContext ctx = line.Open())
            {
                ctx.BeginFigure(pts[0], false, false);
                Trace(ctx, pts, h, true);
            }
            line.Freeze();
            dc.DrawGeometry(null, new Pen(Fill, LineWidth) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, line);
        }

        static double Nice(double v)
        {
            double p = Math.Pow(10, Math.Floor(Math.Log10(v)));
            foreach (double f in new[] { 1.0, 2.0, 5.0 }) if (v <= f * p * 1.000001) return f * p;
            return 10 * p;
        }

        /// <summary>From pts[0] (already there) through the rest: straight lines, or a smooth curve through every point (Catmull-Rom).</summary>
        void Trace(StreamGeometryContext ctx, Point[] pts, double h, bool stroked)
        {
            int m = pts.Length;
            if (!SmoothLine)
            {
                for (int i = 1; i < m; i++) ctx.LineTo(pts[i], stroked, false);
                return;
            }
            Func<Point, Point> keep = p => new Point(p.X, Math.Max(1, Math.Min(h - 1, p.Y)));   // never past the box
            for (int i = 0; i < m - 1; i++)
            {
                Point p0 = pts[Math.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Math.Min(m - 1, i + 2)];
                Point c1 = keep(new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6));
                Point c2 = keep(new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6));
                ctx.BezierTo(c1, c2, p2, stroked, true);
            }
        }
    }
}
