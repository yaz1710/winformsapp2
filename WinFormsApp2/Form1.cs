using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace WinFormsApp2
{
    // =====================================================
    //  Custom controls (all inside this file)
    // =====================================================
    static class Gfx
    {
        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // Full-page background: your photo (cover mode + tint), or a modern drawn night-sky gradient.
    // Everything is drawn ONCE into a cached bitmap, so repainting is fast (no lag)
    class SkyPanel : Panel
    {
        public Image Photo { get; set; }
        public float OverlayStrength { get; set; } = 1f;
        private Bitmap cache;
        private Size cacheSize;

        public SkyPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && cache != null) { cache.Dispose(); cache = null; }
            base.Dispose(disposing);
        }

        private int A(int alpha)
        {
            return Math.Max(0, Math.Min(255, (int)(alpha * OverlayStrength)));
        }

        private void DrawShade(Graphics g, Rectangle rect, int top, int mid, int bottom)
        {
            using (var br = new LinearGradientBrush(rect, Color.Black, Color.Black, 90f))
            {
                br.InterpolationColors = new ColorBlend
                {
                    Positions = new[] { 0f, 0.5f, 1f },
                    Colors = new[]
                    {
                        Color.FromArgb(A(top), 8, 24, 56),
                        Color.FromArgb(A(mid), 8, 24, 56),
                        Color.FromArgb(A(bottom), 8, 24, 56)
                    }
                };
                g.FillRectangle(br, rect);
            }
        }

        private static void Glow(Graphics g, float cx, float cy, float r, Color c, int alpha)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                using (var pgb = new PathGradientBrush(path))
                {
                    pgb.CenterColor = Color.FromArgb(alpha, c);
                    pgb.SurroundColors = new[] { Color.FromArgb(0, c) };
                    g.FillPath(pgb, path);
                }
            }
        }

        private void BuildCache(Size size)
        {
            if (cache != null) cache.Dispose();
            cache = new Bitmap(size.Width, size.Height);
            cacheSize = size;

            using (var g = Graphics.FromImage(cache))
            {
                if (Photo != null) DrawPhoto(g, size);
                else DrawModern(g, size);
            }
        }

        private void DrawPhoto(Graphics g, Size size)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float scale = Math.Max((float)size.Width / Photo.Width, (float)size.Height / Photo.Height);
            float w = Photo.Width * scale, h = Photo.Height * scale;
            g.DrawImage(Photo, (size.Width - w) / 2f, (size.Height - h) / 2f, w, h);

            // Dark blue tint: stronger at the top and bottom so white text stays readable
            DrawShade(g, new Rectangle(Point.Empty, size), 110, 35, 130);
        }

        private void DrawModern(Graphics g, Size size)
        {
            // 1) Soft part (gradient, glows, clouds) drawn small, then scaled up = natural blur
            int sw = Math.Max(2, size.Width / 4), sh = Math.Max(2, size.Height / 4);
            using (var small = new Bitmap(sw, sh))
            {
                using (var sg = Graphics.FromImage(small))
                {
                    sg.SmoothingMode = SmoothingMode.AntiAlias;
                    var r = new Rectangle(0, 0, sw, sh);
                    using (var br = new LinearGradientBrush(r, Color.Black, Color.Black, 90f))
                    {
                        br.InterpolationColors = new ColorBlend
                        {
                            Positions = new[] { 0f, 0.55f, 1f },
                            Colors = new[]
                            {
                                Color.FromArgb(6, 14, 36),
                                Color.FromArgb(16, 44, 98),
                                Color.FromArgb(10, 26, 62)
                            }
                        };
                        sg.FillRectangle(br, r);
                    }

                    Glow(sg, sw * 0.82f, sh * 0.22f, sw * 0.55f, Color.FromArgb(41, 171, 255), 80);
                    Glow(sg, sw * 0.12f, sh * 0.85f, sw * 0.50f, Color.FromArgb(110, 80, 220), 60);
                    Glow(sg, sw * 0.55f, sh * 1.00f, sw * 0.45f, Color.FromArgb(255, 150, 90), 45);

                    var rnd = new Random(7);
                    for (int i = 0; i < 16; i++)
                    {
                        float cw = sw * (0.18f + (float)rnd.NextDouble() * 0.25f);
                        float ch = sh * (0.05f + (float)rnd.NextDouble() * 0.08f);
                        float x = (float)rnd.NextDouble() * sw - cw * 0.3f;
                        float y = sh * (0.55f + (float)rnd.NextDouble() * 0.40f);
                        using (var b = new SolidBrush(Color.FromArgb(rnd.Next(10, 26), 200, 220, 255)))
                            sg.FillEllipse(b, x, y, cw, ch);
                    }
                }
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(small, 0, 0, size.Width, size.Height);
            }

            // 2) Crisp details on top: flight paths and stars
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float W = size.Width, H = size.Height;
            using (var pen = new Pen(Color.FromArgb(45, 255, 255, 255), 1.5f) { DashStyle = DashStyle.Dash })
                g.DrawBezier(pen, -20f, H * 0.90f, W * 0.30f, H * 0.35f, W * 0.65f, H * 0.95f, W + 20f, H * 0.30f);
            using (var pen = new Pen(Color.FromArgb(28, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot })
                g.DrawBezier(pen, -20f, H * 0.70f, W * 0.25f, H * 0.20f, W * 0.70f, H * 0.75f, W + 20f, H * 0.12f);

            var stars = new Random(21);
            for (int i = 0; i < 70; i++)
            {
                float x = (float)stars.NextDouble() * W;
                float y = (float)stars.NextDouble() * H * 0.6f;
                float d = 1f + (float)stars.NextDouble() * 1.8f;
                using (var b = new SolidBrush(Color.FromArgb(stars.Next(50, 140), 255, 255, 255)))
                    g.FillEllipse(b, x, y, d, d);
            }

            // 3) Light shade top/bottom so white text stays readable
            DrawShade(g, new Rectangle(Point.Empty, size), 60, 0, 90);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var rect = ClientRectangle;
            if (rect.Width < 2 || rect.Height < 2) return;
            if (cache == null || cacheSize != rect.Size) BuildCache(rect.Size);
            e.Graphics.DrawImageUnscaled(cache, 0, 0);
        }
    }

    // Rounded translucent "glass" panel
    class GlassPanel : Panel
    {
        public int Radius { get; set; } = 24;
        public Color Fill { get; set; } = Color.FromArgb(110, 30, 34, 50);
        public Color Border { get; set; } = Color.FromArgb(60, 255, 255, 255);

        public GlassPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
            using (var path = Gfx.RoundRect(rect, Radius))
            using (var fill = new SolidBrush(Fill))
            using (var pen = new Pen(Border, 1.2f))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    // Pill-shaped button with an optional round icon on the right
    class PillButton : Control
    {
        public Color Fill { get; set; } = Color.White;
        public Color TextColor { get; set; } = Color.Black;
        public Color IconBack { get; set; } = Color.Black;
        public Color IconFore { get; set; } = Color.White;
        public string Glyph { get; set; } = "\u2708";
        public bool ShowIcon { get; set; } = true;
        private bool hover, down;

        public PillButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Semibold", 10.5F);
            Size = new Size(170, 46);
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        private static Color Shift(Color c, int amt)
        {
            int lum = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
            int d = lum > 170 ? -amt : amt;
            return Color.FromArgb(c.A,
                Math.Max(0, Math.Min(255, c.R + d)),
                Math.Max(0, Math.Min(255, c.G + d)),
                Math.Max(0, Math.Min(255, c.B + d)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var r = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
            Color c = down ? Shift(Fill, 30) : hover ? Shift(Fill, 16) : Fill;
            using (var path = Gfx.RoundRect(r, r.Height / 2))
            using (var b = new SolidBrush(c))
                g.FillPath(b, path);

            float textRight = Width;
            if (ShowIcon)
            {
                float d = Height - 12;
                var ir = new RectangleF(Width - d - 6, (Height - d) / 2f, d, d);
                using (var b = new SolidBrush(IconBack)) g.FillEllipse(b, ir);
                using (var f = new Font("Segoe UI Symbol", 10.5F))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var b = new SolidBrush(IconFore))
                    g.DrawString(Glyph, f, b, ir, sf);
                textRight = ir.Left;
            }

            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var b = new SolidBrush(TextColor))
                g.DrawString(Text, Font, b, new RectangleF(0, 0, textRight, Height), sf);
        }
    }

    // Seat color legend (Available / Booked / Selected)
    class Legend : Control
    {
        public Legend()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 10F);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            string[] texts = { "Available", "Booked", "Selected" };
            Color[] cols = { Color.FromArgb(76, 175, 80), Color.FromArgb(229, 57, 53), Color.FromArgb(62, 66, 88) };

            float total = 0;
            var widths = new float[3];
            for (int i = 0; i < 3; i++)
            {
                widths[i] = 24 + g.MeasureString(texts[i], Font).Width;
                total += widths[i];
            }
            total += 28 * 2;

            float x = (Width - total) / 2f;
            float y = (Height - 18) / 2f;
            for (int i = 0; i < 3; i++)
            {
                using (var path = Gfx.RoundRect(new RectangleF(x, y, 18, 18), 4))
                using (var b = new SolidBrush(cols[i]))
                {
                    g.FillPath(b, path);
                    if (i == 2)
                        using (var pen = new Pen(Color.FromArgb(33, 150, 243), 2.5f))
                            g.DrawPath(pen, path);
                }
                using (var tb = new SolidBrush(ForeColor))
                    g.DrawString(texts[i], Font, tb, x + 24, (Height - Font.Height) / 2f);
                x += widths[i] + 28;
            }
        }
    }

    // Home page background: flat "travel poster" design (solid blue, dashed arcs, clouds, plane, passport).
    // Drawn ONCE into a cached bitmap, so it never lags.
    class HomePanel : Panel
    {
        public const int PadX = 100;
        private static readonly Color Back = Color.FromArgb(38, 37, 156);
        private Bitmap cache;
        private Size cacheSize;

        public HomePanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Back;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && cache != null) { cache.Dispose(); cache = null; }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var rect = ClientRectangle;
            if (rect.Width < 2 || rect.Height < 2) return;
            if (cache == null || cacheSize != rect.Size)
            {
                if (cache != null) cache.Dispose();
                cache = new Bitmap(rect.Width, rect.Height);
                cacheSize = rect.Size;
                using (var g = Graphics.FromImage(cache)) DrawAll(g, rect.Size);
            }
            e.Graphics.DrawImageUnscaled(cache, 0, 0);
        }

        private static PointF P(float x, float y) { return new PointF(x, y); }

        private static void Dot(Graphics g, float x, float y)
        {
            using (var b = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
                g.FillEllipse(b, x - 9, y - 9, 18, 18);
            g.FillEllipse(Brushes.White, x - 4, y - 4, 8, 8);
        }

        private static void DrawAll(Graphics g, Size s)
        {
            float W = s.Width, H = s.Height;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Back);

            // Corner dashed arcs
            using (var pen = new Pen(Color.FromArgb(190, 255, 255, 255), 1.6f) { DashStyle = DashStyle.Dash })
            {
                g.DrawArc(pen, -150f, -170f, 300f, 300f, 0f, 90f);
                g.DrawArc(pen, W - 80f, H - 150f, 200f, 200f, 180f, 90f);
            }

            float px = Math.Min(H * 0.068f, W * 0.042f);
            float L = PadX;
            float y1 = H * 0.31f;

            // Dashed arc above the headline, with glowing end dots
            using (var pen = new Pen(Color.FromArgb(190, 255, 255, 255), 1.6f) { DashStyle = DashStyle.Dash })
                g.DrawBezier(pen, L + 20, y1 - 28, L + 130, y1 - 95, L + 330, y1 - 95, L + 400, y1 - 5);
            Dot(g, L + 20, y1 - 28);
            Dot(g, L + 400, y1 - 5);

            // Headline
            using (var f = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.DrawString("Explore the", f, Brushes.White, L, y1);
                g.DrawString("World with us.", f, Brushes.White, L, y1 + px * 1.18f);
            }

            // Dotted line + paragraph
            float yDot = y1 + px * 2.55f;
            using (var pen = new Pen(Color.White, 3f) { DashCap = DashCap.Round, DashPattern = new float[] { 0.1f, 3.2f } })
                g.DrawLine(pen, L, yDot, L + 190, yDot);

            using (var f = new Font("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var br = new SolidBrush(Color.FromArgb(228, 230, 255)))
            {
                var r = new RectangleF(L, yDot + 26, Math.Min(W * 0.42f, 520f), 90f);
                g.DrawString("Discover great flight deals, trusted airlines and easy seat booking " +
                             "to destinations around the world. Plan your next trip in just a few clicks.",
                             f, br, r);
            }

            DrawIllustration(g, W, H);
        }

        private static void DrawIllustration(Graphics g, float W, float H)
        {
            float k = Math.Min(W / 1280f, H / 780f);

            // Clouds
            Cloud(g, W * 0.455f, H * 0.19f, 120f * k);
            Cloud(g, W * 0.845f, H * 0.24f, 130f * k);
            Cloud(g, W * 0.575f, H * 0.47f, 70f * k);

            // Plane
            var st = g.Save();
            g.TranslateTransform(W * 0.775f, H * 0.37f);
            g.RotateTransform(-32f);
            float sc = k * 0.74f;
            g.ScaleTransform(sc, sc);
            DrawPlane(g);
            g.Restore(st);

            // Tickets + passport
            var st2 = g.Save();
            g.TranslateTransform(W * 0.615f, H * 0.415f);
            g.RotateTransform(8f);
            g.ScaleTransform(k, k);
            DrawTickets(g);
            g.Restore(st2);
        }

        private static void Cloud(Graphics g, float x, float y, float w)
        {
            float h = w * 0.36f;
            using (var b = new SolidBrush(Color.White))
            {
                g.FillEllipse(b, x + w * 0.05f, y + h * 0.30f, w * 0.90f, h * 0.70f);
                g.FillEllipse(b, x + w * 0.20f, y + h * 0.00f, w * 0.36f, h * 0.80f);
                g.FillEllipse(b, x + w * 0.45f, y - h * 0.15f, w * 0.34f, h * 0.95f);
                g.FillEllipse(b, x + w * 0.00f, y + h * 0.40f, w * 0.30f, h * 0.60f);
                g.FillEllipse(b, x + w * 0.70f, y + h * 0.38f, w * 0.30f, h * 0.62f);
            }
        }

        private static void FillPoly(Graphics g, Color c, params PointF[] pts)
        {
            using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
        }

        private static void Engine(Graphics g, float cx, float cy)
        {
            using (var b = new SolidBrush(Color.FromArgb(226, 228, 248)))
                g.FillEllipse(b, cx - 42, cy - 17, 84, 34);
            using (var b = new SolidBrush(Color.FromArgb(30, 20, 104)))
                g.FillEllipse(b, cx + 24, cy - 15, 18, 30);
        }

        // Plane drawn pointing to the right (+x); the caller rotates and scales it
        private static void DrawPlane(Graphics g)
        {
            var wingFar = Color.FromArgb(118, 138, 214);
            var wingNear = Color.FromArgb(160, 178, 238);

            // far wing + far tail (shaded)
            FillPoly(g, wingFar, P(90, 22), P(-140, 285), P(-198, 285), P(-82, 22));
            FillPoly(g, wingFar, P(-205, 14), P(-292, 92), P(-324, 92), P(-266, 12));
            Engine(g, 10, 128);

            // fuselage
            using (var path = new GraphicsPath())
            {
                path.AddClosedCurve(new[]
                {
                    P(-275, 0), P(-250, -14), P(-100, -26), P(120, -28), P(255, -9),
                    P(264, 0), P(255, 9), P(120, 28), P(-100, 26), P(-250, 14)
                }, 0.35f);
                using (var b = new SolidBrush(Color.FromArgb(226, 228, 248))) g.FillPath(b, path);
            }
            // dark window shade along the fuselage
            using (var path = new GraphicsPath())
            {
                path.AddClosedCurve(new[] { P(-190, -7), P(0, -18), P(185, -8), P(0, 2) }, 0.5f);
                using (var b = new SolidBrush(Color.FromArgb(30, 20, 104))) g.FillPath(b, path);
            }

            // near wing + near tail
            FillPoly(g, wingNear, P(90, -22), P(-140, -285), P(-198, -285), P(-82, -22));
            FillPoly(g, wingNear, P(-205, -14), P(-292, -92), P(-324, -92), P(-266, -12));
            Engine(g, 10, -128);
        }

        private static void DrawTickets(Graphics g)
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            using (var white = new SolidBrush(Color.FromArgb(245, 246, 255)))
            using (var line = new SolidBrush(Color.FromArgb(185, 190, 230)))
            {
                var s1 = g.Save();
                g.RotateTransform(-9f);
                using (var p = Gfx.RoundRect(new RectangleF(-96, -112, 82, 176), 6)) g.FillPath(white, p);
                g.FillRectangle(line, -86f, -96f, 50f, 6f);
                g.FillRectangle(line, -86f, -80f, 62f, 4f);
                g.FillRectangle(line, -86f, -68f, 40f, 4f);
                g.Restore(s1);

                var s2 = g.Save();
                g.RotateTransform(7f);
                using (var p = Gfx.RoundRect(new RectangleF(-34, -132, 88, 190), 6)) g.FillPath(white, p);
                g.FillRectangle(line, -22f, -114f, 56f, 6f);
                g.FillRectangle(line, -22f, -98f, 64f, 4f);
                g.FillRectangle(line, -22f, -86f, 44f, 4f);
                g.FillRectangle(line, -22f, -74f, 58f, 4f);
                g.Restore(s2);
            }

            // Passport
            using (var p = Gfx.RoundRect(new RectangleF(-52, -44, 108, 152), 8))
            using (var b = new SolidBrush(Color.FromArgb(30, 18, 98)))
            using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1.5f))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
            using (var f = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                g.DrawString("PASSPORT", f, Brushes.White, new RectangleF(-52, -30, 108, 16), sf);
            using (var pen = new Pen(Color.White, 2f))
            {
                g.DrawEllipse(pen, -26f, 0f, 56f, 56f);
                g.DrawEllipse(pen, -10f, 0f, 24f, 56f);
                g.DrawLine(pen, -26f, 28f, 30f, 28f);
                g.DrawLine(pen, 2f, 0f, 2f, 56f);
            }
            g.FillRectangle(Brushes.White, -26f, 76f, 56f, 6f);
            g.FillRectangle(Brushes.White, -26f, 88f, 56f, 6f);
        }
    }

    class FlightInfo
    {
        public string No, From, To, Time;
        public int Price;
        public FlightInfo(string no, string from, string to, string time, int price)
        { No = no; From = from; To = to; Time = time; Price = price; }
    }

    // =====================================================
    //  One booking in progress (filled step by step:
    //  seat -> passenger page -> payment page -> summary)
    // =====================================================
    class BookingInfo
    {
        // Flight + seat (set when the user presses "Book Seat")
        public string FlightNo, From, To, Time, Date, Seat;
        public int Price;
        public Point SeatPos;

        // Passenger page
        public string FirstName, LastName, Passport, Phone, Email;

        // Payment page
        public string PayMethod, PayDetail;
        public bool Paid;

        // Final
        public string Reference;

        public string FullName { get { return FirstName + " " + LastName; } }
    }

    // =====================================================
    //  Top bar shared by the booking / passenger / payment / summary / dashboard pages
    //  (logo on the left, title in the middle, Logout + Back button on the right)
    // =====================================================
    class TopBar
    {
        public GlassPanel Panel;
        public Label Logo, Title;
        public PillButton Back, Logout;

        public void Layout(int clientWidth)
        {
            Panel.SetBounds(40, 28, clientWidth - 80, 68);
            Logo.Location = new Point(26, (68 - Logo.GetPreferredSize(Size.Empty).Height) / 2);
            Back.Location = new Point(Panel.Width - Back.Width - 12, (68 - Back.Height) / 2);
            Logout.Location = new Point(Back.Left - Logout.Width - 10, (68 - Logout.Height) / 2);
            var sz = Title.GetPreferredSize(Size.Empty);
            Title.Location = new Point((Panel.Width - sz.Width) / 2, (68 - sz.Height) / 2);
        }
    }

    // =====================================================
    //  Form1
    // =====================================================
    public partial class Form1 : Form
    {
        // Background photo: put a file named "background.jpg" next to the .exe
        // (Add it to the project and set "Copy to Output Directory" = "Copy if newer").
        // If the file is missing, a drawn sunset sky is used instead.
        private const string BackgroundFileName = "background.jpg";

        private const string BrandName = "AeroBook";
        private const int Rows = 8;
        private const int BarBottom = 96;   // bottom edge of the top bar (cards are centered below it)

        private static readonly Color FieldColor = Color.FromArgb(62, 66, 88);
        private static readonly Color Accent = Color.FromArgb(41, 171, 255);
        private static readonly Color AvailableColor = Color.FromArgb(76, 175, 80);
        private static readonly Color BookedColor = Color.FromArgb(229, 57, 53);
        private static readonly Color SelectedBorder = Color.FromArgb(33, 150, 243);
        private static readonly Color CardFill = Color.FromArgb(190, 14, 22, 46);
        private static readonly Color CardBorder = Color.FromArgb(70, 255, 255, 255);
        private static readonly Color GoodGreen = Color.FromArgb(46, 160, 90);

        // Pages
        private SkyPanel pageLogin, pageBooking;
        private SkyPanel pagePassenger, pagePayment, pageSummary;
        private HomePanel pageHome;
        private Image background;

        // Navigation history (so the Back button returns to the page you came from)
        private Panel currentPage;
        private readonly Stack<Panel> navStack = new Stack<Panel>();
        private readonly List<TopBar> topBars = new List<TopBar>();

        // Login page
        private GlassPanel loginCard, boxConfirm;
        private TableLayoutPanel loginTable;
        private Label lblLoginTitle, lblLoginSub, lblConfirm, lblLoginError, lblSwitch, lblDemo;
        private TextBox txtUser, txtPass, txtConfirm;
        private CheckBox chkShow;
        private PillButton btnLogin;
        private bool signUpMode;
        private string currentUser;
        // Demo accounts kept in memory only (no database yet)
        private readonly Dictionary<string, string> users =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "admin", "1234" } };

        // Home page
        private Label navHome, navBook, navPrepare, navContact, navBlog, lblWelcome;
        private PillButton btnLogout;
        private Panel searchBox;
        private Label tabFlights, tabCar, tabHotel;
        private Label[] capLabels;
        private Button btnSearch;
        private ComboBox cmbFrom, cmbTo;
        private DateTimePicker txtDepDate, txtRetDate;   // CHANGED: real date pickers
        private NumericUpDown txtPassengers;             // CHANGED: numeric up/down
        private TextBox txtEmail;

        // Booking page
        private TopBar bookBar;
        private Label lblSummary, lblFlightTitle, lblSelectedSeat, lblStatus;
        private PillButton btnBook, btnCancel;
        private DataGridView dgvFlights;

        // Passenger page
        private GlassPanel passCard;
        private TableLayoutPanel passTable;
        private Label lblPassFlight, lblPassError;
        private TextBox txtFirst, txtLast, txtPassport, txtPhone, txtPassEmail;

        // Payment page
        private GlassPanel payCard;
        private TableLayoutPanel payTable;
        private Label lblPayOrder, lblPayTotal, lblPayError;
        private PillButton[] methodButtons;
        private Control[] methodPanels;
        private PillButton btnPay;
        private TextBox txtCardNo, txtCardName, txtExpiry, txtCvv, txtWallet;
        private int payMethod;   // 0 = card, 1 = mobile wallet, 2 = cash at airport

        // Summary page
        private GlassPanel sumCard;
        private TableLayoutPanel sumTable;
        private Label lblSumRoute;
        private Label[] sumValues;

        // The booking currently in progress
        private BookingInfo pending;

        // UI state
        private readonly Random rng = new Random();
        private readonly Dictionary<string, bool[,]> seatStates = new Dictionary<string, bool[,]>();
        private readonly string[] cities = { "Cairo", "Alexandria", "Dubai", "London", "Riyadh", "Paris", "Istanbul" };
        private string currentFlight;
        private bool loadingFlights;

        public Form1()
        {
            InitializeComponent();   // generated by Visual Studio, keep it
            BuildUI();
        }

        // The Designer file hooks this event, so the method must exist (it can stay empty)
        private void Form1_Load(object sender, EventArgs e)
        {
        }

        // =====================================================
        //  Build
        // =====================================================
        private void BuildUI()
        {
            Text = BrandName + " - Flight Reservation Simulation";
            ClientSize = new Size(1280, 780);
            MinimumSize = new Size(1200, 740);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10F);
            BackColor = Color.FromArgb(30, 34, 48);
            DoubleBuffered = true;
            KeyPreview = true;

            background = LoadBackground();

            BuildLoginPage();
            BuildHomePage();
            BuildBookingPage();
            BuildPassengerPage();
            BuildPaymentPage();
            BuildSummaryPage();
            BuildStatsPage();       // NEW: Dashboard (charts + bookings list)

            Controls.Add(pageStats);
            Controls.Add(pageSummary);
            Controls.Add(pagePayment);
            Controls.Add(pagePassenger);
            Controls.Add(pageBooking);
            Controls.Add(pageHome);
            Controls.Add(pageLogin);

            ShowPage(pageLogin);   // the app starts on the login screen

            Load += (s, e) => LayoutAll();
            Resize += (s, e) => LayoutAll();
            KeyDown += (s, e) =>
            {
                // Esc = same as the Back button
                if (e.KeyCode == Keys.Escape &&
                    (currentPage == pageBooking || currentPage == pagePassenger ||
                     currentPage == pagePayment || currentPage == pageStats))
                    GoBack();
            };
            FormClosed += (s, e) => { if (background != null) background.Dispose(); };
        }

        // Loads background.jpg from the exe folder; returns null (drawn sky) if missing or invalid
        private static Image LoadBackground()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, BackgroundFileName);
                if (File.Exists(path))
                {
                    using (var img = Image.FromFile(path))
                        return new Bitmap(img);   // copy, so the file is not locked
                }
            }
            catch
            {
                // ignore and fall back
            }
            return null;
        }

        private SkyPanel NewSky(float overlay)
        {
            return new SkyPanel { Dock = DockStyle.Fill, Photo = background, OverlayStrength = overlay };
        }

        private Label NewLabel(string text, Font font, Color color)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                BackColor = Color.Transparent,
                AutoSize = true
            };
        }

        private TextBox NewTextBox(string text)
        {
            return new TextBox
            {
                Text = text,
                BorderStyle = BorderStyle.None,
                BackColor = FieldColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5F)
            };
        }

        private ComboBox NewCombo(string[] items)
        {
            var c = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = FieldColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5F)
            };
            c.Items.AddRange(items);
            c.SelectedIndex = 0;
            return c;
        }

        private GlassPanel MakeFieldBox(Control input, Padding margin, int padTop)
        {
            var box = new GlassPanel
            {
                Radius = 14,
                Fill = FieldColor,
                Border = Color.FromArgb(70, 255, 255, 255),
                Dock = DockStyle.Fill,
                Margin = margin,
                Padding = new Padding(14, padTop, 14, 0)
            };
            input.Dock = DockStyle.Top;
            box.Controls.Add(input);
            return box;
        }

        private Label FieldLabel(string text)
        {
            return new Label
            {
                Text = text,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.BottomLeft,
                Font = new Font("Segoe UI", 10.5F),
                Padding = new Padding(4, 0, 0, 3)
            };
        }

        // ---------------- shared helpers for the booking pages ----------------

        // Creates the top bar (logo | title | Logout | Back) and adds it to the page
        private TopBar MakeTopBar(SkyPanel page, string title, string backText, string backGlyph, Action backAction)
        {
            var bar = new TopBar();
            bar.Panel = new GlassPanel { Radius = 34, Fill = Color.FromArgb(60, 255, 255, 255), Border = Color.FromArgb(50, 255, 255, 255) };
            bar.Logo = NewLabel("\u2708  " + BrandName, new Font("Segoe UI Symbol", 16F, FontStyle.Bold), Color.White);
            bar.Logo.Cursor = Cursors.Hand;
            bar.Logo.Click += (s, e) => GoHome();
            bar.Title = NewLabel(title, new Font("Segoe UI", 11F), Color.White);
            bar.Back = new PillButton { Text = backText, Glyph = backGlyph, Size = new Size(130, 46) };
            bar.Back.Click += (s, e) => backAction();
            bar.Logout = new PillButton
            {
                Text = "Logout",
                ShowIcon = false,
                Fill = Color.FromArgb(70, 255, 255, 255),
                TextColor = Color.White,
                Size = new Size(110, 46)
            };
            bar.Logout.Click += (s, e) => Logout();
            bar.Panel.Controls.AddRange(new Control[] { bar.Logo, bar.Title, bar.Logout, bar.Back });
            page.Controls.Add(bar.Panel);
            bar.Panel.BringToFront();
            topBars.Add(bar);
            return bar;
        }

        private GlassPanel NewCard(int hPad)
        {
            return new GlassPanel
            {
                Radius = 32,
                Fill = CardFill,
                Border = CardBorder,
                Padding = new Padding(36, hPad, 36, 22)
            };
        }

        private Label CardTitle(string text)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI Semibold", 20F),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter
            };
        }

        private Label CardSub(string text)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 10F),
                ForeColor = Color.FromArgb(190, 205, 230),
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.TopCenter,
                UseMnemonic = false
            };
        }

        private Label NewErrorLabel()
        {
            return new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(255, 138, 128),
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter
            };
        }

        private static void AddSpan(TableLayoutPanel t, Control c, int col, int row)
        {
            t.Controls.Add(c, col, row);
            t.SetColumnSpan(c, 2);
        }

        // Label above a field in a 2-column form (right = true for the right column)
        private Label ColLabel(string text, bool right)
        {
            var l = FieldLabel(text);
            l.Margin = new Padding(right ? 8 : 0, 0, right ? 0 : 8, 0);
            return l;
        }

        private static Padding ColBox(bool right)
        {
            return new Padding(right ? 8 : 0, 0, right ? 0 : 8, 4);
        }

        // Enter key runs the action; font is the same as the login fields
        private static void StyleInput(TextBox t, Action onEnter)
        {
            t.Font = new Font("Segoe UI", 11F);
            t.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; onEnter(); }
            };
        }

        // Only digits (+ the extra characters you allow) can be typed
        private static void Restrict(TextBox t, string extra)
        {
            t.KeyPress += (s, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && extra.IndexOf(e.KeyChar) < 0)
                    e.Handled = true;
            };
        }

        // Only letters, spaces, apostrophes and hyphens (works with Arabic letters too)
        private static void LettersOnly(TextBox t)
        {
            t.KeyPress += (s, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsLetter(e.KeyChar) && " '-".IndexOf(e.KeyChar) < 0)
                    e.Handled = true;
            };
        }

        private static string Proper(string s)
        {
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLower(CultureInfo.CurrentCulture));
        }

        // Puts a card in the middle of the area below the top bar
        private void CenterCard(GlassPanel card, TableLayoutPanel table, int width)
        {
            if (card == null || table == null) return;
            int h = card.Padding.Vertical;
            foreach (RowStyle rs in table.RowStyles) h += (int)rs.Height;
            card.Size = new Size(width, h);

            int top = BarBottom + 8;
            int area = ClientSize.Height - top;
            card.Location = new Point(
                Math.Max(0, (ClientSize.Width - width) / 2),
                top + Math.Max(0, (area - h) / 2));
        }

        // ---------------- Login page ----------------
        private void BuildLoginPage()
        {
            pageLogin = NewSky(1.3f);

            loginCard = new GlassPanel
            {
                Radius = 32,
                Fill = Color.FromArgb(190, 14, 22, 46),
                Border = Color.FromArgb(70, 255, 255, 255),
                Padding = new Padding(36, 26, 36, 22)
            };

            loginTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 14
            };
            loginTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            // 0 logo | 1 title | 2 subtitle | 3 user lbl | 4 user | 5 pass lbl | 6 pass |
            // 7 confirm lbl | 8 confirm | 9 show pw | 10 error | 11 button | 12 switch | 13 demo hint
            int[] rowHeights = { 48, 42, 30, 30, 52, 30, 52, 0, 0, 34, 28, 56, 36, 26 };
            foreach (int h in rowHeights) loginTable.RowStyles.Add(new RowStyle(SizeType.Absolute, h));

            var logo = new Label
            {
                Text = "\u2708  " + BrandName,
                Font = new Font("Segoe UI Symbol", 18F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter
            };
            lblLoginTitle = new Label
            {
                Text = "Welcome back",
                Font = new Font("Segoe UI Semibold", 20F),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter
            };
            lblLoginSub = new Label
            {
                Text = "Sign in to book your next flight",
                Font = new Font("Segoe UI", 10F),
                ForeColor = Color.FromArgb(190, 205, 230),
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.TopCenter
            };

            txtUser = NewTextBox("");
            txtPass = NewTextBox("");
            txtConfirm = NewTextBox("");
            txtPass.UseSystemPasswordChar = true;
            txtConfirm.UseSystemPasswordChar = true;
            foreach (var t in new[] { txtUser, txtPass, txtConfirm })
            {
                t.Font = new Font("Segoe UI", 11F);
                t.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SubmitLogin(); }
                };
            }

            var boxMargin = new Padding(0, 0, 0, 4);
            var boxUser = MakeFieldBox(txtUser, boxMargin, 14);
            var boxPass = MakeFieldBox(txtPass, boxMargin, 14);
            boxConfirm = MakeFieldBox(txtConfirm, boxMargin, 14);
            lblConfirm = FieldLabel("Confirm password");
            lblConfirm.Visible = false;
            boxConfirm.Visible = false;

            chkShow = new CheckBox
            {
                Text = "Show password",
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Cursor = Cursors.Hand
            };
            chkShow.CheckedChanged += (s, e) =>
            {
                txtPass.UseSystemPasswordChar = !chkShow.Checked;
                txtConfirm.UseSystemPasswordChar = !chkShow.Checked;
            };

            lblLoginError = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(255, 138, 128),
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter
            };

            btnLogin = new PillButton
            {
                Text = "Login",
                Fill = Accent,
                TextColor = Color.FromArgb(10, 20, 40),
                IconBack = Color.White,
                IconFore = Color.Black,
                Glyph = "\u2192",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 4)
            };
            btnLogin.Click += (s, e) => SubmitLogin();

            lblSwitch = new Label
            {
                Text = "Don't have an account? Sign up",
                ForeColor = Accent,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter
            };
            lblSwitch.Click += (s, e) => SetMode(!signUpMode);

            lblDemo = new Label
            {
                Text = "Demo account:  admin / 1234",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(150, 165, 195),
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter
            };

            loginTable.Controls.Add(logo, 0, 0);
            loginTable.Controls.Add(lblLoginTitle, 0, 1);
            loginTable.Controls.Add(lblLoginSub, 0, 2);
            loginTable.Controls.Add(FieldLabel("Username"), 0, 3);
            loginTable.Controls.Add(boxUser, 0, 4);
            loginTable.Controls.Add(FieldLabel("Password"), 0, 5);
            loginTable.Controls.Add(boxPass, 0, 6);
            loginTable.Controls.Add(lblConfirm, 0, 7);
            loginTable.Controls.Add(boxConfirm, 0, 8);
            loginTable.Controls.Add(chkShow, 0, 9);
            loginTable.Controls.Add(lblLoginError, 0, 10);
            loginTable.Controls.Add(btnLogin, 0, 11);
            loginTable.Controls.Add(lblSwitch, 0, 12);
            loginTable.Controls.Add(lblDemo, 0, 13);

            loginCard.Controls.Add(loginTable);
            pageLogin.Controls.Add(loginCard);
        }

        private void SetMode(bool signUp)
        {
            signUpMode = signUp;
            loginTable.SuspendLayout();
            loginTable.RowStyles[7].Height = signUp ? 30 : 0;
            loginTable.RowStyles[8].Height = signUp ? 52 : 0;
            lblConfirm.Visible = signUp;
            boxConfirm.Visible = signUp;
            lblLoginTitle.Text = signUp ? "Create account" : "Welcome back";
            lblLoginSub.Text = signUp ? "Join us and start booking flights" : "Sign in to book your next flight";
            btnLogin.Text = signUp ? "Sign up" : "Login";
            lblSwitch.Text = signUp ? "Already have an account? Login" : "Don't have an account? Sign up";
            lblDemo.Visible = !signUp;
            lblLoginError.Text = "";
            txtConfirm.Text = "";
            loginTable.ResumeLayout();
            LayoutLogin();
        }

        private void SubmitLogin()
        {
            string u = txtUser.Text.Trim();
            string p = txtPass.Text;

            if (u == "" || p == "")
            {
                lblLoginError.Text = "Please enter your username and password.";
                return;
            }

            if (signUpMode)
            {
                if (u.Length < 3) { lblLoginError.Text = "Username must be at least 3 characters."; return; }
                if (p.Length < 4) { lblLoginError.Text = "Password must be at least 4 characters."; return; }
                if (p != txtConfirm.Text) { lblLoginError.Text = "Passwords do not match."; return; }
                if (users.ContainsKey(u)) { lblLoginError.Text = "That username is already taken."; return; }
                users[u] = p;
            }
            else
            {
                string saved;
                if (!users.TryGetValue(u, out saved) || saved != p)
                {
                    lblLoginError.Text = "Wrong username or password.";
                    return;
                }
            }

            EnterApp(u);
        }

        private void EnterApp(string user)
        {
            currentUser = user;
            lblWelcome.Text = "Signed in as  " + user;
            txtUser.Text = "";
            txtPass.Text = "";
            chkShow.Checked = false;
            SetMode(false);
            navStack.Clear();
            ShowPage(pageHome, false);
        }

        // Also clears the booking in progress and the navigation history
        private void Logout()
        {
            currentUser = null;
            pending = null;
            ClearBookingForms();
            navStack.Clear();
            ShowPage(pageLogin, false);
        }

        // ---------------- Home page ----------------
        private static readonly Color HomeDeep = Color.FromArgb(28, 18, 100);
        private static readonly Color HomeInk = Color.FromArgb(24, 18, 80);

        private void ComingSoon(string name)
        {
            MessageBox.Show(name + " will be added in a later version.", BrandName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private Label NavLabel(string text, Action onClick)
        {
            var l = new Label
            {
                Text = text,
                Font = new Font("Segoe UI Semibold", 10F),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = true,
                UseMnemonic = false,
                Cursor = Cursors.Hand
            };
            l.MouseEnter += (s, e) => l.ForeColor = Color.FromArgb(150, 215, 255);
            l.MouseLeave += (s, e) => l.ForeColor = Color.White;
            l.Click += (s, e) => onClick();
            return l;
        }

        private ComboBox NewHomeCombo(string[] items)
        {
            var c = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = HomeInk,
                Font = new Font("Segoe UI Semibold", 12.5F)
            };
            c.Items.AddRange(items);
            c.SelectedIndex = 0;
            return c;
        }

        private TextBox NewHomeText(string text)
        {
            return new TextBox
            {
                Text = text,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                ForeColor = HomeInk,
                Font = new Font("Segoe UI Semibold", 12.5F)
            };
        }

        private Label TabLabel(string text, bool active)
        {
            var l = new Label
            {
                Text = text,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 9.5F),
                BackColor = active ? Color.White : HomeDeep,
                ForeColor = active ? HomeInk : Color.White,
                Cursor = Cursors.Hand
            };
            return l;
        }

        private void BuildHomePage()
        {
            pageHome = new HomePanel { Dock = DockStyle.Fill };

            // Top navigation links
            navHome = NavLabel("Home", () => { });
            navBook = NavLabel("Book a flight", ShowBooking);
            navPrepare = NavLabel("Prepare & Travel", () => ComingSoon("Prepare & Travel"));
            navContact = NavLabel("Contact", () => ComingSoon("Contact"));
            navBlog = NavLabel("Dashboard", ShowDashboard);   // CHANGED: opens the statistics dashboard

            lblWelcome = NewLabel("", new Font("Segoe UI", 10F), Color.White);
            btnLogout = new PillButton
            {
                Text = "Logout",
                ShowIcon = false,
                Fill = Color.White,
                TextColor = HomeInk,
                Size = new Size(110, 40)
            };
            btnLogout.Click += (s, e) => Logout();

            // Tabs above the search box
            tabFlights = TabLabel("Flights", true);
            tabCar = TabLabel("Rental car", false);
            tabHotel = TabLabel("Hotel", false);
            tabCar.Click += (s, e) => ComingSoon("Rental car");
            tabHotel.Click += (s, e) => ComingSoon("Hotel");

            // White search box with six fields
            searchBox = new Panel { BackColor = Color.White };
            string[] caps = { "FROM", "TO", "DEPARTURE", "RETURN", "PASSENGERS", "EMAIL" };
            capLabels = new Label[6];
            for (int i = 0; i < 6; i++)
            {
                capLabels[i] = new Label
                {
                    Text = caps[i],
                    Font = new Font("Segoe UI Semibold", 8F),
                    ForeColor = Color.FromArgb(130, 132, 160),
                    BackColor = Color.White,
                    AutoSize = true
                };
            }

            var cityItems = new[] { "Any" }.Concat(cities).ToArray();
            cmbFrom = NewHomeCombo(cityItems);
            cmbTo = NewHomeCombo(cityItems);
            txtDepDate = NewHomeDate(DateTime.Today.AddDays(7));    // CHANGED
            txtRetDate = NewHomeDate(DateTime.Today.AddDays(14));   // CHANGED
            txtPassengers = NewHomePax();                            // CHANGED
            txtEmail = NewHomeText("");
            AddHint(txtEmail, "Your Email", HomeInk);

            foreach (var c in capLabels) searchBox.Controls.Add(c);
            searchBox.Controls.AddRange(new Control[] { cmbFrom, cmbTo, txtDepDate, txtRetDate, txtPassengers, txtEmail });
            searchBox.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(222, 224, 238)))
                    for (int i = 1; i < 6; i++)
                    {
                        int x = searchBox.Width * i / 6;
                        e.Graphics.DrawLine(pen, x, 12, x, searchBox.Height - 12);
                    }
            };

            // Dark "Search flight" bar
            btnSearch = new Button
            {
                Text = "Search flight",
                FlatStyle = FlatStyle.Flat,
                BackColor = HomeDeep,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 11F),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            btnSearch.FlatAppearance.BorderSize = 0;
            btnSearch.FlatAppearance.MouseOverBackColor = Color.FromArgb(44, 30, 130);
            btnSearch.FlatAppearance.MouseDownBackColor = Color.FromArgb(60, 44, 150);
            btnSearch.Click += (s, e) => ShowBooking();
            WireHomeEvents();   // NEW: events + tooltips (after btnSearch exists)

            pageHome.Controls.AddRange(new Control[]
            {
                navHome, navBook, navPrepare, navContact, navBlog, lblWelcome, btnLogout,
                tabFlights, tabCar, tabHotel, searchBox, btnSearch
            });
        }

        private void AddHint(TextBox tb, string hint, Color typedColor)
        {
            Color hintColor = Color.FromArgb(150, 152, 178);
            tb.Text = hint;
            tb.ForeColor = hintColor;
            tb.Enter += (s, e) =>
            {
                if (tb.ForeColor == hintColor) { tb.Text = ""; tb.ForeColor = typedColor; }
            };
            tb.Leave += (s, e) =>
            {
                if (tb.Text.Trim() == "") { tb.Text = hint; tb.ForeColor = hintColor; }
            };
        }

        private void AddField(TableLayoutPanel t, int col, string label, Control input)
        {
            var lbl = new Label
            {
                Text = label,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10.5F),
                Padding = new Padding(4, 0, 0, 0)
            };
            t.Controls.Add(lbl, col, 0);
            t.Controls.Add(MakeFieldBox(input, new Padding(0, 0, 12, 0), 12), col, 1);
        }

        // ---------------- Booking page ----------------
        private void BuildBookingPage()
        {
            pageBooking = NewSky(1.5f);

            // Content area: flights (left) | seats (right)
            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(40, 114, 40, 32)
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.Controls.Add(BuildFlightsPanel(), 0, 0);
            content.Controls.Add(BuildSeatsPanel(), 1, 0);
            pageBooking.Controls.Add(content);

            // Top bar with a Back button (goes to the previous page).
            // Click the AeroBook logo to jump straight to Home.
            bookBar = MakeTopBar(pageBooking, "", "Back", "\u2190", GoBack);
            lblSummary = bookBar.Title;
        }

        private Control BuildFlightsPanel()
        {
            var panel = new GlassPanel
            {
                Dock = DockStyle.Fill,
                Radius = 28,
                Fill = Color.FromArgb(165, 24, 28, 44),
                Padding = new Padding(20, 16, 20, 20),
                Margin = new Padding(0, 0, 10, 0)
            };

            dgvFlights = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                BackgroundColor = Color.FromArgb(28, 32, 48),
                GridColor = Color.FromArgb(52, 58, 80),
                EnableHeadersVisualStyles = false,
                Font = new Font("Segoe UI", 10.5F)
            };
            dgvFlights.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(20, 24, 38);
            dgvFlights.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(150, 215, 255);
            dgvFlights.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 10.5F);
            dgvFlights.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(20, 24, 38);
            dgvFlights.ColumnHeadersHeight = 38;
            dgvFlights.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dgvFlights.RowTemplate.Height = 36;
            dgvFlights.DefaultCellStyle.BackColor = Color.FromArgb(28, 32, 48);
            dgvFlights.DefaultCellStyle.ForeColor = Color.White;
            dgvFlights.DefaultCellStyle.SelectionBackColor = Color.FromArgb(33, 150, 243);
            dgvFlights.DefaultCellStyle.SelectionForeColor = Color.White;

            dgvFlights.Columns.Add("FlightNo", "Flight");
            dgvFlights.Columns.Add("From", "From");
            dgvFlights.Columns.Add("To", "To");
            dgvFlights.Columns.Add("Time", "Time");
            dgvFlights.Columns.Add("Price", "Price");
            typeof(DataGridView).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(dgvFlights, true, null);
            dgvFlights.SelectionChanged += (s, e) => { if (!loadingFlights) OnFlightChanged(); };

            var title = new Label
            {
                Text = "Available Flights",
                Dock = DockStyle.Top,
                Height = 40,
                Font = new Font("Segoe UI Semibold", 14F),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };

            panel.Controls.Add(dgvFlights);   // Fill first
            panel.Controls.Add(title);        // then Top
            return panel;
        }

        private Control BuildSeatsPanel()
        {
            var panel = new GlassPanel
            {
                Dock = DockStyle.Fill,
                Radius = 28,
                Fill = Color.FromArgb(165, 24, 28, 44),
                Padding = new Padding(20, 14, 20, 14),
                Margin = new Padding(10, 0, 0, 0)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 6
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            lblFlightTitle = new Label
            {
                Dock = DockStyle.Fill,
                Text = "No flight selected",
                Font = new Font("Segoe UI Semibold", 14F),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            layout.Controls.Add(lblFlightTitle, 0, 0);
            layout.Controls.Add(new Legend { Dock = DockStyle.Fill }, 0, 1);

            // CHANGED: the seats are now drawn with GDI+ (SeatMap in Visuals.cs)
            seatMap = new SeatMap { Dock = DockStyle.Fill, Rows = Rows };
            seatMap.SeatClicked += SeatMap_SeatClicked;
            layout.Controls.Add(seatMap, 0, 2);

            lblSelectedSeat = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Selected seat: none",
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            layout.Controls.Add(lblSelectedSeat, 0, 3);

            var buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                RowCount = 1
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            btnBook = new PillButton
            {
                Text = "Book Seat",
                Fill = Color.FromArgb(46, 160, 90),
                TextColor = Color.White,
                IconBack = Color.White,
                IconFore = Color.FromArgb(46, 160, 90),
                Glyph = "\u2713",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 8, 4)
            };
            btnCancel = new PillButton
            {
                Text = "Cancel Booking",
                Fill = Color.FromArgb(214, 66, 66),
                TextColor = Color.White,
                IconBack = Color.White,
                IconFore = Color.FromArgb(214, 66, 66),
                Glyph = "\u2715",
                Dock = DockStyle.Fill,
                Margin = new Padding(8, 4, 0, 4)
            };
            btnBook.Click += (s, e) => BookSeat();
            btnCancel.Click += (s, e) => CancelSeat();
            buttons.Controls.Add(btnBook, 0, 0);
            buttons.Controls.Add(btnCancel, 1, 0);
            layout.Controls.Add(buttons, 0, 4);

            lblStatus = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Select a flight, then choose a seat.",
                ForeColor = Color.FromArgb(190, 205, 230),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9.5F)
            };
            layout.Controls.Add(lblStatus, 0, 5);

            panel.Controls.Add(layout);
            return panel;
        }

        // ---------------- Passenger page (step 1 of 3) ----------------
        private void BuildPassengerPage()
        {
            pagePassenger = NewSky(1.4f);
            MakeTopBar(pagePassenger, "Step 1 of 3  \u00B7  Passenger details", "Back", "\u2190", GoBack);

            passCard = NewCard(24);
            passTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                RowCount = 10
            };
            passTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            passTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            // 0 title | 1 flight info | 2 first/last labels | 3 first/last | 4 passport/phone labels |
            // 5 passport/phone | 6 email label | 7 email | 8 error | 9 button
            int[] heights = { 46, 30, 30, 52, 30, 52, 30, 52, 28, 56 };
            foreach (int h in heights) passTable.RowStyles.Add(new RowStyle(SizeType.Absolute, h));

            txtFirst = NewTextBox("");
            txtLast = NewTextBox("");
            txtPassport = NewTextBox("");
            txtPhone = NewTextBox("");
            txtPassEmail = NewTextBox("");
            foreach (var t in new[] { txtFirst, txtLast, txtPassport, txtPhone, txtPassEmail })
                StyleInput(t, SubmitPassenger);
            txtFirst.MaxLength = 30;
            txtLast.MaxLength = 30;
            txtPassport.MaxLength = 9;
            txtPassport.CharacterCasing = CharacterCasing.Upper;
            txtPhone.MaxLength = 18;
            txtPassEmail.MaxLength = 60;
            LettersOnly(txtFirst);
            LettersOnly(txtLast);
            Restrict(txtPhone, "+- ");
            txtPassport.KeyPress += (s, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsLetterOrDigit(e.KeyChar)) e.Handled = true;
            };

            lblPassFlight = CardSub("");
            lblPassError = NewErrorLabel();

            var btnNext = new PillButton
            {
                Text = "Continue to payment",
                Fill = Accent,
                TextColor = Color.FromArgb(10, 20, 40),
                IconBack = Color.White,
                IconFore = Color.Black,
                Glyph = "\u2192",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 4)
            };
            btnNext.Click += (s, e) => SubmitPassenger();

            AddSpan(passTable, CardTitle("Passenger details"), 0, 0);
            AddSpan(passTable, lblPassFlight, 0, 1);
            passTable.Controls.Add(ColLabel("First name", false), 0, 2);
            passTable.Controls.Add(ColLabel("Last name", true), 1, 2);
            passTable.Controls.Add(MakeFieldBox(txtFirst, ColBox(false), 14), 0, 3);
            passTable.Controls.Add(MakeFieldBox(txtLast, ColBox(true), 14), 1, 3);
            passTable.Controls.Add(ColLabel("Passport number", false), 0, 4);
            passTable.Controls.Add(ColLabel("Phone number", true), 1, 4);
            passTable.Controls.Add(MakeFieldBox(txtPassport, ColBox(false), 14), 0, 5);
            passTable.Controls.Add(MakeFieldBox(txtPhone, ColBox(true), 14), 1, 5);
            AddSpan(passTable, FieldLabel("Email (optional)"), 0, 6);
            AddSpan(passTable, MakeFieldBox(txtPassEmail, new Padding(0, 0, 0, 4), 14), 0, 7);
            AddSpan(passTable, lblPassError, 0, 8);
            AddSpan(passTable, btnNext, 0, 9);

            passCard.Controls.Add(passTable);
            pagePassenger.Controls.Add(passCard);
        }

        private void PassError(string message, Control focus)
        {
            lblPassError.Text = message;
            focus.Focus();
        }

        private void SubmitPassenger()
        {
            if (pending == null) return;

            string first = txtFirst.Text.Trim(), last = txtLast.Text.Trim();
            string passport = txtPassport.Text.Trim(), phone = txtPhone.Text.Trim(), email = txtPassEmail.Text.Trim();
            string namePattern = @"^\p{L}[\p{L}\s'\-]+$";

            if (!Regex.IsMatch(first, namePattern)) { PassError("Please enter a valid first name.", txtFirst); return; }
            if (!Regex.IsMatch(last, namePattern)) { PassError("Please enter a valid last name.", txtLast); return; }
            if (!Regex.IsMatch(passport, "^[A-Za-z0-9]{6,9}$")) { PassError("Passport number must be 6-9 letters or digits.", txtPassport); return; }

            int digits = phone.Count(c => char.IsDigit(c));
            if (digits < 7 || digits > 15) { PassError("Please enter a valid phone number (7-15 digits).", txtPhone); return; }

            if (email != "" && !Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                PassError("The email address is not valid (or leave it empty).", txtPassEmail);
                return;
            }

            pending.FirstName = Proper(first);
            pending.LastName = Proper(last);
            pending.Passport = passport.ToUpperInvariant();
            pending.Phone = phone;
            pending.Email = email;

            lblPassError.Text = "";
            OpenPaymentPage();
        }

        // ---------------- Payment page (step 2 of 3) ----------------
        private void BuildPaymentPage()
        {
            pagePayment = NewSky(1.4f);
            MakeTopBar(pagePayment, "Step 2 of 3  \u00B7  Payment", "Back", "\u2190", GoBack);

            payCard = NewCard(24);
            payTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 8
            };
            payTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            // 0 title | 1 order info | 2 total | 3 "Payment method" | 4 method buttons |
            // 5 method details | 6 error | 7 pay button
            int[] heights = { 46, 28, 38, 30, 50, 246, 28, 56 };
            foreach (int h in heights) payTable.RowStyles.Add(new RowStyle(SizeType.Absolute, h));

            lblPayOrder = CardSub("");
            lblPayTotal = new Label
            {
                Text = "",
                Font = new Font("Segoe UI Semibold", 15F),
                ForeColor = Accent,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter
            };
            lblPayError = NewErrorLabel();

            // --- Method buttons (Card | Mobile wallet | Cash at airport)
            var methodRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 3,
                RowCount = 1,
                Margin = Padding.Empty
            };
            for (int i = 0; i < 3; i++) methodRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            methodRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            string[] names = { "Card", "Mobile wallet", "Cash at airport" };
            methodButtons = new PillButton[3];
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var b = new PillButton
                {
                    Text = names[i],
                    ShowIcon = false,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(i == 0 ? 0 : 4, 4, i == 2 ? 0 : 4, 4),
                    Font = new Font("Segoe UI Semibold", 10F)
                };
                b.Click += (s, e) => SelectMethod(idx);
                methodButtons[i] = b;
                methodRow.Controls.Add(b, i, 0);
            }

            // --- Method details: three panels on top of each other, only one visible
            var host = new GlassPanel
            {
                Fill = Color.Transparent,
                Border = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            methodPanels = new Control[] { BuildCardPanel(), BuildWalletPanel(), BuildCashPanel() };
            foreach (var p in methodPanels) host.Controls.Add(p);

            btnPay = new PillButton
            {
                Text = "Confirm & Pay",
                Fill = GoodGreen,
                TextColor = Color.White,
                IconBack = Color.White,
                IconFore = GoodGreen,
                Glyph = "\u2713",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 4)
            };
            btnPay.Click += (s, e) => SubmitPayment();

            payTable.Controls.Add(CardTitle("Payment"), 0, 0);
            payTable.Controls.Add(lblPayOrder, 0, 1);
            payTable.Controls.Add(lblPayTotal, 0, 2);
            payTable.Controls.Add(FieldLabel("Payment method"), 0, 3);
            payTable.Controls.Add(methodRow, 0, 4);
            payTable.Controls.Add(host, 0, 5);
            payTable.Controls.Add(lblPayError, 0, 6);
            payTable.Controls.Add(btnPay, 0, 7);

            payCard.Controls.Add(payTable);
            pagePayment.Controls.Add(payCard);

            SelectMethod(0);
        }

        private Control BuildCardPanel()
        {
            var p = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                RowCount = 6,
                Margin = Padding.Empty
            };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            foreach (int h in new[] { 30, 52, 30, 52, 30, 52 }) p.RowStyles.Add(new RowStyle(SizeType.Absolute, h));

            txtCardNo = NewTextBox("");
            txtCardName = NewTextBox("");
            txtExpiry = NewTextBox("");
            txtCvv = NewTextBox("");
            foreach (var t in new[] { txtCardNo, txtCardName, txtExpiry, txtCvv }) StyleInput(t, SubmitPayment);
            txtCardNo.MaxLength = 19;
            txtCardName.MaxLength = 40;
            txtExpiry.MaxLength = 5;
            txtCvv.MaxLength = 4;
            txtCvv.UseSystemPasswordChar = true;
            Restrict(txtCardNo, " ");
            Restrict(txtExpiry, "/");
            Restrict(txtCvv, "");
            LettersOnly(txtCardName);

            AddSpan(p, FieldLabel("Card number"), 0, 0);
            AddSpan(p, MakeFieldBox(txtCardNo, new Padding(0, 0, 0, 4), 14), 0, 1);
            AddSpan(p, FieldLabel("Name on card"), 0, 2);
            AddSpan(p, MakeFieldBox(txtCardName, new Padding(0, 0, 0, 4), 14), 0, 3);
            p.Controls.Add(ColLabel("Expiry (MM/YY)", false), 0, 4);
            p.Controls.Add(ColLabel("CVV", true), 1, 4);
            p.Controls.Add(MakeFieldBox(txtExpiry, ColBox(false), 14), 0, 5);
            p.Controls.Add(MakeFieldBox(txtCvv, ColBox(true), 14), 1, 5);
            return p;
        }

        private Control BuildWalletPanel()
        {
            var p = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 3,
                Margin = Padding.Empty,
                Visible = false
            };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int h in new[] { 30, 52, 50 }) p.RowStyles.Add(new RowStyle(SizeType.Absolute, h));

            txtWallet = NewTextBox("");
            StyleInput(txtWallet, SubmitPayment);
            txtWallet.MaxLength = 18;
            Restrict(txtWallet, "+- ");

            p.Controls.Add(FieldLabel("Wallet phone number"), 0, 0);
            p.Controls.Add(MakeFieldBox(txtWallet, new Padding(0, 0, 0, 4), 14), 0, 1);
            var note = CardSub("A payment request will be sent to this number.");
            note.TextAlign = ContentAlignment.MiddleCenter;
            p.Controls.Add(note, 0, 2);
            return p;
        }

        private Control BuildCashPanel()
        {
            var note = new Label
            {
                Text = "Pay in cash at the airport check-in counter.\nPlease arrive at least 3 hours before departure.",
                Font = new Font("Segoe UI", 11F),
                ForeColor = Color.FromArgb(210, 220, 240),
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false
            };
            return note;
        }

        private void SelectMethod(int i)
        {
            payMethod = i;
            for (int k = 0; k < 3; k++)
            {
                bool on = k == i;
                methodButtons[k].Fill = on ? Accent : Color.FromArgb(70, 255, 255, 255);
                methodButtons[k].TextColor = on ? Color.FromArgb(10, 20, 40) : Color.White;
                methodButtons[k].Invalidate();
                methodPanels[k].Visible = on;
            }
            btnPay.Text = i == 2 ? "Confirm booking" : "Confirm & Pay";
            lblPayError.Text = "";
        }

        private void OpenPaymentPage()
        {
            lblPayOrder.Text = string.Format("{0}  \u00B7  {1} \u2192 {2}  \u00B7  Seat {3}",
                pending.FlightNo, pending.From, pending.To, pending.Seat);
            lblPayTotal.Text = "Total:  $" + pending.Price;
            if (txtWallet.Text == "") txtWallet.Text = pending.Phone;
            SelectMethod(payMethod);
            ShowPage(pagePayment);
        }

        private void PayError(string message, Control focus)
        {
            lblPayError.Text = message;
            focus.Focus();
        }

        // Demo only: nothing is sent anywhere, the data is just checked and shown in the summary
        private void SubmitPayment()
        {
            if (pending == null) return;

            if (payMethod == 0)
            {
                string digits = new string(txtCardNo.Text.Where(c => char.IsDigit(c)).ToArray());
                if (digits.Length < 13 || digits.Length > 19) { PayError("Please enter a valid card number.", txtCardNo); return; }
                if (txtCardName.Text.Trim().Length < 3) { PayError("Please enter the name on the card.", txtCardName); return; }

                Match m = Regex.Match(txtExpiry.Text.Trim(), @"^(0[1-9]|1[0-2])/(\d{2})$");
                if (!m.Success) { PayError("Expiry date must look like 08/28.", txtExpiry); return; }
                int month = int.Parse(m.Groups[1].Value);
                int year = 2000 + int.Parse(m.Groups[2].Value);
                DateTime today = DateTime.Today;
                if (year < today.Year || (year == today.Year && month < today.Month))
                {
                    PayError("This card has expired.", txtExpiry);
                    return;
                }

                if (!Regex.IsMatch(txtCvv.Text.Trim(), @"^\d{3,4}$")) { PayError("CVV must be 3 or 4 digits.", txtCvv); return; }

                pending.PayMethod = "Credit / Debit card";
                pending.PayDetail = "ending " + digits.Substring(digits.Length - 4);
                pending.Paid = true;
            }
            else if (payMethod == 1)
            {
                int d = txtWallet.Text.Count(c => char.IsDigit(c));
                if (d < 10 || d > 15) { PayError("Please enter a valid wallet phone number.", txtWallet); return; }

                pending.PayMethod = "Mobile wallet";
                pending.PayDetail = txtWallet.Text.Trim();
                pending.Paid = true;
            }
            else
            {
                pending.PayMethod = "Cash at airport";
                pending.PayDetail = "";
                pending.Paid = false;
            }

            lblPayError.Text = "";
            CompleteBooking();
        }

        private string NewReference()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var sb = new System.Text.StringBuilder("AB-");
            for (int i = 0; i < 6; i++) sb.Append(chars[rng.Next(chars.Length)]);
            return sb.ToString();
        }

        // Empties the passenger + payment fields (used after a booking and on logout)
        private void ClearBookingForms()
        {
            if (txtFirst == null) return;
            foreach (var t in new[] { txtFirst, txtLast, txtPassport, txtPhone, txtPassEmail,
                                      txtCardNo, txtCardName, txtExpiry, txtCvv, txtWallet })
                t.Text = "";
            lblPassError.Text = "";
            lblPayError.Text = "";
            SelectMethod(0);
        }

        // ---------------- Summary page (step 3 of 3) ----------------
        private void BuildSummaryPage()
        {
            pageSummary = NewSky(1.4f);
            MakeTopBar(pageSummary, "Step 3 of 3  \u00B7  Booking summary", "Home", "\u2302", GoHome);

            sumCard = NewCard(22);

            string[] caps = { "Booking reference", "Passenger", "Passport", "Phone", "Email", "Flight",
                              "Departure date", "Departure time", "Seat", "Payment", "Total" };

            sumTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                RowCount = 3 + caps.Length + 1
            };
            sumTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            sumTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            // 0 header | 1 route | 2 spacer | 3.. details | last = button
            sumTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            sumTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            sumTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));
            for (int i = 0; i < caps.Length; i++) sumTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            sumTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

            var header = new Label
            {
                Text = "\u2713  Booking confirmed",
                Font = new Font("Segoe UI Symbol", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(102, 214, 130),
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter
            };
            lblSumRoute = new Label
            {
                Text = "",
                Font = new Font("Segoe UI Symbol", 20F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter,
                UseMnemonic = false
            };
            AddSpan(sumTable, header, 0, 0);
            AddSpan(sumTable, lblSumRoute, 0, 1);

            sumValues = new Label[caps.Length];
            for (int i = 0; i < caps.Length; i++)
            {
                var cap = new Label
                {
                    Text = caps[i],
                    Font = new Font("Segoe UI", 10F),
                    ForeColor = Color.FromArgb(160, 175, 205),
                    BackColor = Color.Transparent,
                    Dock = DockStyle.Fill,
                    Margin = Padding.Empty,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                var val = new Label
                {
                    Text = "",
                    Font = new Font("Segoe UI Semibold", 10.5F),
                    ForeColor = Color.White,
                    BackColor = Color.Transparent,
                    Dock = DockStyle.Fill,
                    Margin = Padding.Empty,
                    TextAlign = ContentAlignment.MiddleRight,
                    AutoEllipsis = true,
                    UseMnemonic = false
                };
                sumTable.Controls.Add(cap, 0, 3 + i);
                sumTable.Controls.Add(val, 1, 3 + i);
                sumValues[i] = val;
            }
            sumValues[0].ForeColor = Accent;

            var btnAnother = new PillButton
            {
                Text = "Book another flight",
                Fill = Accent,
                TextColor = Color.FromArgb(10, 20, 40),
                IconBack = Color.White,
                IconFore = Color.Black,
                Glyph = "\u2192",
                Dock = DockStyle.Fill,
                Margin = new Padding(60, 6, 60, 4)
            };
            btnAnother.Click += (s, e) => GoHome();
            AddSpan(sumTable, btnAnother, 0, 3 + caps.Length);

            sumCard.Controls.Add(sumTable);
            pageSummary.Controls.Add(sumCard);
        }

        private void FillSummary(BookingInfo b)
        {
            lblSumRoute.Text = b.From + "   \u2708   " + b.To;
            sumValues[0].Text = b.Reference;
            sumValues[1].Text = b.FullName;
            sumValues[2].Text = b.Passport;
            sumValues[3].Text = b.Phone;
            sumValues[4].Text = b.Email == "" ? "\u2014" : b.Email;
            sumValues[5].Text = b.FlightNo;
            sumValues[6].Text = b.Date == "" ? "\u2014" : b.Date;
            sumValues[7].Text = b.Time;
            sumValues[8].Text = b.Seat;
            sumValues[9].Text = b.PayDetail == "" ? b.PayMethod : b.PayMethod + " (" + b.PayDetail + ")";
            sumValues[10].Text = "$" + b.Price + (b.Paid ? "  \u00B7  Paid" : "  \u00B7  Pay at airport");
        }

        // =====================================================
        //  Layout (positions for the free-form pages)
        // =====================================================
        private void LayoutAll()
        {
            LayoutLogin();
            LayoutHome();
            LayoutBars();
            CenterCard(passCard, passTable, 560);
            CenterCard(payCard, payTable, 560);
            CenterCard(sumCard, sumTable, 560);
        }

        private void LayoutLogin()
        {
            if (loginCard == null) return;
            int h = loginCard.Padding.Vertical;
            foreach (RowStyle rs in loginTable.RowStyles) h += (int)rs.Height;
            loginCard.Size = new Size(440, h);
            loginCard.Location = new Point(
                Math.Max(0, (ClientSize.Width - loginCard.Width) / 2),
                Math.Max(0, (ClientSize.Height - loginCard.Height) / 2));
        }

        private void LayoutHome()
        {
            if (searchBox == null) return;
            int W = ClientSize.Width, H = ClientSize.Height;
            int padX = HomePanel.PadX;

            // Top navigation
            int navY = (int)(H * 0.055f);
            Label[] nav = { navHome, navBook, navPrepare, navContact, navBlog };
            int x = padX;
            foreach (var l in nav)
            {
                var sz = l.GetPreferredSize(Size.Empty);
                l.Location = new Point(x, navY + (40 - sz.Height) / 2);
                x += sz.Width + 52;
            }
            btnLogout.Location = new Point(W - padX - btnLogout.Width, navY);
            var ws = lblWelcome.GetPreferredSize(Size.Empty);
            lblWelcome.Location = new Point(btnLogout.Left - ws.Width - 16, navY + (40 - ws.Height) / 2);

            // Search area (tabs, white box, dark bar)
            int sx = padX, sw = W - padX * 2;
            int tabH = 36, boxH = 92, barH = 46, bottom = H - 44;
            int barTop = bottom - barH, boxTop = barTop - boxH, tabTop = boxTop - tabH;

            tabFlights.SetBounds(sx, tabTop, 132, tabH);
            tabCar.SetBounds(sx + 132, tabTop, 132, tabH);
            tabHotel.SetBounds(sx + 264, tabTop, 132, tabH);
            searchBox.SetBounds(sx, boxTop, sw, boxH);
            btnSearch.SetBounds(sx, barTop, sw, barH);

            int colW = sw / 6;
            Control[] vals = { cmbFrom, cmbTo, txtDepDate, txtRetDate, txtPassengers, txtEmail };
            for (int i = 0; i < 6; i++)
            {
                capLabels[i].Location = new Point(i * colW + 18, 20);
                vals[i].Left = i * colW + 18;
                vals[i].Top = 46;
                vals[i].Width = colW - 34;
            }
            searchBox.Invalidate();
        }

        // Lays out the top bar of every page that has one
        private void LayoutBars()
        {
            foreach (var bar in topBars) bar.Layout(ClientSize.Width);
        }

        // =====================================================
        //  Navigation
        // =====================================================
        // Remembers the page you leave, so GoBack() can return to it.
        // recordHistory = false when going "back" or when the history should not grow.
        private void ShowPage(Panel page, bool recordHistory = true)
        {
            if (recordHistory && currentPage != null && currentPage != page && currentPage != pageLogin)
                navStack.Push(currentPage);
            currentPage = page;

            pageLogin.Visible = page == pageLogin;
            pageHome.Visible = page == pageHome;
            pageBooking.Visible = page == pageBooking;
            pagePassenger.Visible = page == pagePassenger;
            pagePayment.Visible = page == pagePayment;
            pageSummary.Visible = page == pageSummary;
            pageStats.Visible = page == pageStats;      // NEW
            page.BringToFront();
            LayoutAll();

            if (page == pageLogin) txtUser.Focus();
            else if (page == pagePassenger) txtFirst.Focus();
        }

        // Back to the page you came from
        private void GoBack()
        {
            if (navStack.Count == 0) return;
            ShowPage(navStack.Pop(), false);
        }

        // Straight to the home page
        private void GoHome()
        {
            if (currentUser == null) return;
            navStack.Clear();
            ShowPage(pageHome, false);
        }

        private void ShowBooking()
        {
            string from = cmbFrom.SelectedItem.ToString();
            string to = cmbTo.SelectedItem.ToString();
            int pax;
            if (!int.TryParse(txtPassengers.Text.Trim(), out pax) || pax < 1) pax = 1;

            lblSummary.Text = string.Format("{0}  \u2192  {1}   \u00B7   {2} passenger{3}",
                from == "Any" ? "Anywhere" : from, to == "Any" ? "Anywhere" : to, pax, pax > 1 ? "s" : "");

            ShowPage(pageBooking);
            LoadFlights(from, to);
        }

        private void GroupClicked(object sender, EventArgs e)
        {
            MessageBox.Show("Group booking will be added in a later version.", BrandName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // =====================================================
        //  Flights (UI only, no backend yet)
        //  The seat map logic (OnFlightChanged, BookSeat, CancelSeat, ...) is in Form1.Features.cs
        // =====================================================
        // City coordinates (latitude, longitude) used to calculate distance and price for any route
        private static readonly Dictionary<string, PointF> cityPos = new Dictionary<string, PointF>
        {
            { "Cairo", new PointF(30.04f, 31.24f) },
            { "Alexandria", new PointF(31.20f, 29.92f) },
            { "Dubai", new PointF(25.20f, 55.27f) },
            { "London", new PointF(51.51f, -0.13f) },
            { "Riyadh", new PointF(24.71f, 46.68f) },
            { "Paris", new PointF(48.86f, 2.35f) },
            { "Istanbul", new PointF(41.01f, 28.98f) }
        };

        private static double DistanceKm(string a, string b)
        {
            PointF p = cityPos[a], q = cityPos[b];
            double dLat = (q.X - p.X) * Math.PI / 180.0;
            double dLon = (q.Y - p.Y) * Math.PI / 180.0;
            double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(p.X * Math.PI / 180.0) * Math.Cos(q.X * Math.PI / 180.0) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2 * 6371.0 * Math.Asin(Math.Sqrt(h));
        }

        // Builds the flights of ONE route (e.g. Riyadh -> London). Same route = same flights every time.
        private List<FlightInfo> RouteFlights(string from, string to, int count)
        {
            var list = new List<FlightInfo>();
            int id = Array.IndexOf(cities, from) * 10 + Array.IndexOf(cities, to);   // unique per route
            double km = DistanceKm(from, to);
            int basePrice = (int)(Math.Round((60 + km * 0.085) / 5.0) * 5);

            int[] hours = { 6, 10, 14, 19 };
            int[] mins = { 0, 15, 30, 45 };
            int[] percent = { 100, 112, 125, 140 };

            for (int i = 0; i < count && i < 4; i++)
            {
                string no = "MS" + (100 + id * 10 + i);
                string time = string.Format("{0:00}:{1:00}", hours[i] + id % 3, mins[(id + i) % 4]);
                int price = (int)(Math.Round(basePrice * percent[i] / 100.0 / 5.0) * 5);
                list.Add(new FlightInfo(no, from, to, time, price));
            }
            return list;
        }

        // Exact route -> only that route. "Any" -> a few flights from each matching route.
        private List<FlightInfo> BuildFlights(string from, string to)
        {
            int perRoute = (from != "Any" && to != "Any") ? 4 : (from == "Any" && to == "Any") ? 1 : 2;
            var list = new List<FlightInfo>();
            foreach (string o in (from == "Any" ? cities : new[] { from }))
                foreach (string d in (to == "Any" ? cities : new[] { to }))
                {
                    if (o == d) continue;
                    list.AddRange(RouteFlights(o, d, perRoute));
                }
            return list;
        }

        private void LoadFlights(string from, string to)
        {
            string message = null;
            List<FlightInfo> list;
            if (from != "Any" && from == to)
            {
                list = new List<FlightInfo>();
                message = "Departure and arrival cities are the same. Please choose different cities.";
            }
            else
            {
                list = BuildFlights(from, to);
            }

            loadingFlights = true;
            dgvFlights.Rows.Clear();
            foreach (var f in list)
                dgvFlights.Rows.Add(f.No, f.From, f.To, f.Time, "$" + f.Price);
            if (dgvFlights.Rows.Count > 0)
            {
                dgvFlights.ClearSelection();
                dgvFlights.Rows[0].Selected = true;
                dgvFlights.CurrentCell = dgvFlights.Rows[0].Cells[0];
            }
            loadingFlights = false;

            OnFlightChanged();
            if (message != null) lblStatus.Text = message;
        }

        private bool[,] GetSeatState(string flightNo)
        {
            bool[,] st;
            if (!seatStates.TryGetValue(flightNo, out st))
            {
                st = new bool[Rows, 6];
                for (int r = 0; r < Rows; r++)
                    for (int c = 0; c < 6; c++)
                        st[r, c] = rng.Next(100) < 25;
                seatStates[flightNo] = st;
            }
            return st;
        }
    }
}
