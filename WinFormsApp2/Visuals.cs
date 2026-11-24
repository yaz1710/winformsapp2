using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinFormsApp2
{
    // Easing used by the chart animations
    static class Anim
    {
        public static float Ease(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }
    }

    // =====================================================
    //  SeatMap: the plane + all the seats drawn with GDI+
    //  (replaces the 48 Buttons). Hit-testing, hover, click,
    //  pulse animation on the selected seat, arrow-key navigation.
    // =====================================================
    class SeatMap : Control
    {
        public int Rows { get; set; } = 8;
        public event Action<Point> SeatClicked;

        private const float Aisle = 34f;
        private const string Letters = "ABCDEF";

        private bool[,] booked;
        private Point? selected;
        private Point? hover;
        private float phase;
        private readonly System.Windows.Forms.Timer pulse = new System.Windows.Forms.Timer { Interval = 40 };

        public SeatMap()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            TabStop = true;
            pulse.Tick += (s, e) => { phase += 0.25f; Invalidate(); };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) pulse.Dispose();
            base.Dispose(disposing);
        }

        public Point? Selected { get { return selected; } }

        public static string SeatName(Point p)
        {
            return (p.X + 1).ToString() + Letters[p.Y];
        }

        // Shows the seats of one flight (the array is shared with the form, so changes show after Invalidate)
        public void LoadSeats(bool[,] states)
        {
            booked = states;
            SetSelected(null);
        }

        public void SetSelected(Point? p)
        {
            selected = p;
            if (p.HasValue) pulse.Start(); else pulse.Stop();
            Invalidate();
        }

        // ---------- geometry ----------
        private RectangleF BodyRect()
        {
            float bw = Math.Min(Width * 0.72f, 380f);
            return new RectangleF((Width - bw) / 2f, 6f, bw, Height - 12f);
        }

        private static float NoseHeight(RectangleF b)
        {
            return Math.Min(70f, b.Height * 0.16f);
        }

        private RectangleF SeatRect(int r, int c)
        {
            var b = BodyRect();
            float left = b.X + 18f, right = b.Right - 18f;
            float top = b.Y + NoseHeight(b) + 26f, bottom = b.Bottom - 16f;
            float sw = (right - left - Aisle) / 6f;
            float sh = (bottom - top) / Rows;
            float x = left + c * sw + (c >= 3 ? Aisle : 0f);
            return new RectangleF(x + 3f, top + r * sh + 3f, sw - 6f, sh - 6f);
        }

        // Which seat is under the mouse? (null = none)
        private Point? HitTest(Point pt)
        {
            if (booked == null) return null;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < 6; c++)
                    if (SeatRect(r, c).Contains(pt)) return new Point(r, c);
            return null;
        }

        // ---------- events ----------
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var h = HitTest(e.Location);
            if (h != hover)
            {
                hover = h;
                Cursor = h.HasValue ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover.HasValue)
            {
                hover = null;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            var h = HitTest(e.Location);
            if (h.HasValue) Pick(h.Value);
        }

        private void Pick(Point p)
        {
            SetSelected(p);
            var handler = SeatClicked;
            if (handler != null) handler(p);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (booked == null) return;

            int r = selected.HasValue ? selected.Value.X : 0;
            int c = selected.HasValue ? selected.Value.Y : 0;
            if (selected.HasValue)
            {
                if (e.KeyCode == Keys.Left) c--;
                else if (e.KeyCode == Keys.Right) c++;
                else if (e.KeyCode == Keys.Up) r--;
                else if (e.KeyCode == Keys.Down) r++;
                else return;
            }
            else if (e.KeyCode != Keys.Left && e.KeyCode != Keys.Right &&
                     e.KeyCode != Keys.Up && e.KeyCode != Keys.Down) return;

            r = Math.Max(0, Math.Min(Rows - 1, r));
            c = Math.Max(0, Math.Min(5, c));
            e.Handled = true;
            Pick(new Point(r, c));
        }

        // ---------- drawing ----------
        private static int Cl(int v) { return Math.Max(0, Math.Min(255, v)); }

        private static Color Shade(Color c, int d)
        {
            return Color.FromArgb(c.A, Cl(c.R + d), Cl(c.G + d), Cl(c.B + d));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (booked == null || Width < 120 || Height < 160) return;

            var b = BodyRect();
            float nose = NoseHeight(b);

            // Wings
            float wing = Math.Min(80f, b.X - 4f);
            if (wing > 10f)
            {
                float y1 = b.Y + b.Height * 0.38f, hg = b.Height * 0.26f;
                using (var wb = new SolidBrush(Color.FromArgb(80, 120, 150, 210)))
                {
                    g.FillPolygon(wb, new[]
                    {
                        new PointF(b.X, y1), new PointF(b.X - wing, y1 + hg * 0.8f),
                        new PointF(b.X - wing, y1 + hg), new PointF(b.X, y1 + hg * 0.55f)
                    });
                    g.FillPolygon(wb, new[]
                    {
                        new PointF(b.Right, y1), new PointF(b.Right + wing, y1 + hg * 0.8f),
                        new PointF(b.Right + wing, y1 + hg), new PointF(b.Right, y1 + hg * 0.55f)
                    });
                }
            }

            // Fuselage (nose = half ellipse on top, rounded tail)
            using (var body = new GraphicsPath())
            {
                body.AddArc(b.X, b.Y, b.Width, nose * 2f, 180f, 180f);
                body.AddLine(b.Right, b.Y + nose, b.Right, b.Bottom - 16f);
                body.AddArc(b.Right - 32f, b.Bottom - 32f, 32f, 32f, 0f, 90f);
                body.AddLine(b.Right - 16f, b.Bottom, b.X + 16f, b.Bottom);
                body.AddArc(b.X, b.Bottom - 32f, 32f, 32f, 90f, 90f);
                body.CloseFigure();

                using (var fill = new LinearGradientBrush(b, Color.FromArgb(150, 40, 46, 74),
                                                          Color.FromArgb(150, 22, 26, 46), LinearGradientMode.Horizontal))
                using (var pen = new Pen(Color.FromArgb(110, 255, 255, 255), 2f))
                {
                    g.FillPath(fill, body);
                    g.DrawPath(pen, body);
                }
            }

            // Cockpit window
            using (var wb = new SolidBrush(Color.FromArgb(110, 140, 200, 255)))
                g.FillEllipse(wb, b.X + b.Width / 2f - 24f, b.Y + 12f, 48f, 16f);

            // Column letters (A B C   D E F) and row numbers in the aisle
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var dim = new SolidBrush(Color.FromArgb(180, 195, 220)))
            {
                for (int c = 0; c < 6; c++)
                {
                    var r0 = SeatRect(0, c);
                    g.DrawString(Letters[c].ToString(), Font, dim, new RectangleF(r0.X, r0.Y - 24f, r0.Width, 22f), sf);
                }
                for (int r = 0; r < Rows; r++)
                {
                    var rr = SeatRect(r, 2);
                    g.DrawString((r + 1).ToString(), Font, dim, new RectangleF(rr.Right + 3f, rr.Y, Aisle, rr.Height), sf);
                }
            }

            // Seats
            var green = Color.FromArgb(76, 175, 80);
            var red = Color.FromArgb(229, 57, 53);
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                for (int r = 0; r < Rows; r++)
                {
                    for (int c = 0; c < 6; c++)
                    {
                        var rc = SeatRect(r, c);
                        if (rc.Width < 8f || rc.Height < 8f) continue;

                        bool isBooked = booked[r, c];
                        bool isSel = selected.HasValue && selected.Value.X == r && selected.Value.Y == c;
                        bool isHover = hover.HasValue && hover.Value.X == r && hover.Value.Y == c;
                        Color baseCol = isBooked ? red : green;
                        float rad = Math.Min(8f, Math.Min(rc.Width, rc.Height) / 2.5f);

                        using (var path = Gfx.RoundRect(rc, rad))
                        using (var gb = new LinearGradientBrush(rc, Shade(baseCol, isHover ? 55 : 25),
                                                                Shade(baseCol, isHover ? -5 : -30), 90f))
                        {
                            g.FillPath(gb, path);
                        }

                        if (isSel)
                        {
                            float p = (float)(Math.Sin(phase) * 0.5 + 0.5);
                            var glow = RectangleF.Inflate(rc, 2f + 3f * p, 2f + 3f * p);
                            using (var gp = Gfx.RoundRect(glow, rad + 3f))
                            using (var pen = new Pen(Color.FromArgb((int)(90 + 130 * p), 33, 150, 243), 3f))
                                g.DrawPath(pen, gp);
                        }

                        g.DrawString((r + 1).ToString() + Letters[c], Font, Brushes.White, rc, sf);
                    }
                }
            }

            // Hover bubble: "3C · Aisle · Available"
            if (hover.HasValue)
            {
                var h = hover.Value;
                var rc = SeatRect(h.X, h.Y);
                string kind = (h.Y == 0 || h.Y == 5) ? "Window" : (h.Y == 2 || h.Y == 3) ? "Aisle" : "Middle";
                string text = SeatName(h) + "  \u00B7  " + kind + "  \u00B7  " + (booked[h.X, h.Y] ? "Booked" : "Available");
                using (var f = new Font("Segoe UI", 9F))
                {
                    var sz = g.MeasureString(text, f);
                    float bwid = sz.Width + 16f, bhei = sz.Height + 6f;
                    float bx = Math.Max(2f, Math.Min(Width - bwid - 2f, rc.X + rc.Width / 2f - bwid / 2f));
                    float by = Math.Max(2f, rc.Y - bhei - 4f);
                    var bubble = new RectangleF(bx, by, bwid, bhei);
                    using (var p = Gfx.RoundRect(bubble, 8f))
                    using (var fill = new SolidBrush(Color.FromArgb(235, 12, 16, 32)))
                    using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255)))
                    {
                        g.FillPath(fill, p);
                        g.DrawPath(pen, p);
                    }
                    g.DrawString(text, f, Brushes.White, bx + 8f, by + 3f);
                }
            }
        }
    }

    // =====================================================
    //  DonutChart: booked vs available seats (animated)
    // =====================================================
    class DonutChart : Control
    {
        public int Booked { get; private set; }
        public int Total { get; private set; }

        private float progress = 1f;
        private readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer { Interval = 16 };

        public DonutChart()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 10F);
            anim.Tick += (s, e) =>
            {
                progress += 0.05f;
                if (progress >= 1f) { progress = 1f; anim.Stop(); }
                Invalidate();
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) anim.Dispose();
            base.Dispose(disposing);
        }

        public void SetData(int booked, int total)
        {
            Booked = booked;
            Total = total;
            progress = 0f;
            anim.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (Width < 80 || Height < 100) return;

            float legendH = 28f;
            float d = Math.Min(Width - 20f, Height - legendH - 10f);
            float pw = Math.Max(10f, d * 0.15f);
            var ring = new RectangleF((Width - d) / 2f + pw / 2f, 4f + pw / 2f, d - pw, d - pw);
            float t = Anim.Ease(progress);
            float frac = Total > 0 ? (float)Booked / Total : 0f;

            using (var pen = new Pen(Total > 0 ? Color.FromArgb(76, 175, 80) : Color.FromArgb(80, 255, 255, 255), pw))
                g.DrawEllipse(pen, ring);

            float sweep = 360f * frac * t;
            if (sweep > 0.5f)
                using (var pen = new Pen(Color.FromArgb(229, 57, 53), pw))
                    g.DrawArc(pen, ring, -90f, sweep);

            // Center text
            int pct = (int)Math.Round(frac * 100f * t);
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                using (var f = new Font("Segoe UI Semibold", d * 0.2f, FontStyle.Bold, GraphicsUnit.Pixel))
                    g.DrawString(pct + "%", f, Brushes.White,
                        new RectangleF(ring.X, ring.Y - d * 0.04f, ring.Width, ring.Height), sf);
                using (var f = new Font("Segoe UI", d * 0.08f, GraphicsUnit.Pixel))
                using (var b = new SolidBrush(Color.FromArgb(170, 185, 215)))
                    g.DrawString("booked", f, b,
                        new RectangleF(ring.X, ring.Y + d * 0.15f, ring.Width, ring.Height), sf);
            }

            // Legend
            string t1 = "Booked " + Booked, t2 = "Available " + (Total - Booked);
            float w1 = g.MeasureString(t1, Font).Width, w2 = g.MeasureString(t2, Font).Width;
            float total = 14f + w1 + 24f + 14f + w2;
            float x = (Width - total) / 2f;
            float y = Height - legendH + 4f;
            using (var b = new SolidBrush(Color.FromArgb(229, 57, 53))) g.FillEllipse(b, x, y + 4f, 10f, 10f);
            g.DrawString(t1, Font, Brushes.White, x + 14f, y);
            x += 14f + w1 + 24f;
            using (var b = new SolidBrush(Color.FromArgb(76, 175, 80))) g.FillEllipse(b, x, y + 4f, 10f, 10f);
            g.DrawString(t2, Font, Brushes.White, x + 14f, y);
        }
    }

    // =====================================================
    //  BarChart: occupancy % per flight (animated bars)
    // =====================================================
    class BarChart : Control
    {
        public string Highlight { get; set; }

        private readonly List<string> labels = new List<string>();
        private readonly List<float> values = new List<float>();
        private float progress = 1f;
        private readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer { Interval = 16 };

        public BarChart()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = new Font("Segoe UI", 8.5F);
            anim.Tick += (s, e) =>
            {
                progress += 0.045f;
                if (progress >= 1f) { progress = 1f; anim.Stop(); }
                Invalidate();
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) anim.Dispose();
            base.Dispose(disposing);
        }

        public void SetData(IList<string> names, IList<float> percents)
        {
            labels.Clear();
            values.Clear();
            for (int i = 0; i < names.Count; i++) { labels.Add(names[i]); values.Add(percents[i]); }
            progress = 0f;
            anim.Start();
        }

        private static Color ColorFor(float v)
        {
            if (v < 50f) return Color.FromArgb(76, 175, 80);
            if (v < 75f) return Color.FromArgb(255, 167, 38);
            return Color.FromArgb(229, 57, 53);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var plot = new RectangleF(42f, 22f, Width - 42f - 12f, Height - 22f - 34f);
            if (plot.Width < 60f || plot.Height < 60f) return;

            var dim = Color.FromArgb(170, 185, 215);
            using (var grid = new Pen(Color.FromArgb(45, 255, 255, 255)))
            using (var tb = new SolidBrush(dim))
            using (var right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
            {
                for (int i = 0; i <= 4; i++)
                {
                    float y = plot.Bottom - plot.Height * i / 4f;
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    g.DrawString((i * 25) + "%", Font, tb, new RectangleF(0f, y - 9f, 36f, 18f), right);
                }
            }

            if (values.Count == 0)
            {
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var tb = new SolidBrush(dim))
                    g.DrawString("No flights to show yet", Font, tb, plot, sf);
                return;
            }

            float t = Anim.Ease(progress);
            float slot = plot.Width / values.Count;
            float bw = Math.Min(46f, slot * 0.6f);

            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                for (int i = 0; i < values.Count; i++)
                {
                    float full = Math.Max(0f, Math.Min(100f, values[i]));
                    float h = plot.Height * full / 100f * t;
                    float x = plot.Left + slot * i + (slot - bw) / 2f;
                    var bar = new RectangleF(x, plot.Bottom - h, bw, h);
                    Color col = ColorFor(full);

                    if (h > 2f)
                    {
                        using (var path = Gfx.RoundRect(bar, Math.Min(6f, h / 2.2f)))
                        using (var gb = new LinearGradientBrush(bar, ControlPaint.Light(col, 0.25f), col, 90f))
                            g.FillPath(gb, path);
                    }

                    // value above the bar
                    if (t > 0.6f)
                    {
                        float ty = Math.Max(0f, bar.Y - 18f);
                        g.DrawString((int)Math.Round(full) + "%", Font, Brushes.White,
                            new RectangleF(x - 10f, ty, bw + 20f, 16f), center);
                    }

                    // flight number under the bar
                    bool hl = Highlight != null && labels[i] == Highlight;
                    using (var f = new Font("Segoe UI", 8.5F, hl ? FontStyle.Bold : FontStyle.Regular))
                    using (var b = new SolidBrush(hl ? Color.FromArgb(41, 171, 255) : dim))
                        g.DrawString(labels[i], f, b,
                            new RectangleF(plot.Left + slot * i, plot.Bottom + 6f, slot, 18f), center);
                }
            }
        }
    }
}
