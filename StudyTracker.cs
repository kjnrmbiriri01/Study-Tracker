using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

class StudyTask
{
    public string Title; public int Target; public bool Done; public string Due = "";   // Due = yyyy-MM-dd or empty
    public DateTime? DueDate { get { DateTime d; return DateTime.TryParseExact(Due ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? (DateTime?)d : null; } }
    public int DaysLeft { get { var d = DueDate; return d.HasValue ? (int)(d.Value.Date - DateTime.Today).TotalDays : int.MaxValue; } }
    public string DueText { get { var d = DueDate; if (!d.HasValue) return ""; int n = DaysLeft; return n < 0 ? "Overdue by " + (-n) + " day(s)" : n == 0 ? "Due today" : n == 1 ? "Due tomorrow" : "Due in " + n + " days"; } }
}
class BookProg { public string FilePath; public int Page, Total; public long Opened; }
class Slot { public int Day, Minutes; public string Time = "18:00", Subject = ""; }   // Day: 0 = Monday ... 6 = Sunday
class Sess { public string Date, Subject; public int Minutes, Pages; }

static class App
{
    public static string ExeDir { get { return Path.GetDirectoryName(Application.ExecutablePath); } }
    public static string DataDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StudyTracker"); } }
}

// ===== Colors: MedEduNotes dark teal (#0c1f22) with green accents, cards and borders in the studytracker.co.in style =====
static class Theme
{
    public static bool Dark; public static int Pal;
    public static Color Base = ColorTranslator.FromHtml("#0c1f22");
    public static Color Bg, Card, Text, Muted, Accent, Accent2, Accent3, Border;
    public static string[] PalNames = { "Ocean", "Rose", "Lavender", "Sunny" };
    // user font preferences: baked-in sizes are the "Small" baseline; these apply family/scale on top
    public static string Family = "Segoe UI"; public static float FontScale = 1f;
    static Dictionary<string, Font> fcache = new Dictionary<string, Font>();
    public static Font F(float size, FontStyle st)   // for custom-painted text
    {
        string k = Family + "|" + (size * FontScale) + "|" + (int)st; Font f;
        if (!fcache.TryGetValue(k, out f)) { f = new Font(Family, Math.Max(7f, size * FontScale), st); fcache[k] = f; }
        return f;
    }
    public static Font F(float size) { return F(size, FontStyle.Regular); }
    static void Collect(Control c, List<Control> all, bool self) { if (self) all.Add(c); foreach (Control k in c.Controls) Collect(k, all, true); }
    public static void ScaleFonts(Control root, float ratio, bool includeRoot)   // snapshot first, so nested controls are never scaled twice
    {
        var all = new List<Control>(); Collect(root, all, includeRoot); var old = all.Select(c => c.Font).ToList();
        for (int i = 0; i < all.Count; i++) { try { all[i].Font = new Font(old[i].FontFamily, Math.Max(7f, Math.Min(26f, old[i].Size * ratio)), old[i].Style); } catch { } }
    }
    public static void ScaleFonts(Control root, float ratio) { ScaleFonts(root, ratio, true); }
    public static void SetFamily(Control root, string fam, bool includeRoot)
    {
        var all = new List<Control>(); Collect(root, all, includeRoot); var old = all.Select(c => c.Font).ToList();
        for (int i = 0; i < all.Count; i++) { try { all[i].Font = new Font(fam, old[i].Size, old[i].Style); } catch { } }
    }
    public static void SetFamily(Control root, string fam) { SetFamily(root, fam, true); }
    public static void ApplyUserFont(Control root, bool includeRoot)   // for controls created after start-up
    {
        if (Family != "Segoe UI") SetFamily(root, Family, includeRoot);
        if (Math.Abs(FontScale - 1f) > 0.01f) ScaleFonts(root, FontScale, includeRoot);
    }
    static string[][] pals = {
        new string[] { "#1FB5A6", "#FF7B8A", "#FFC15E" },
        new string[] { "#E8588D", "#8A6CF0", "#FFB86B" },
        new string[] { "#8A6CF0", "#E8588D", "#3CC8B4" },
        new string[] { "#F5A524", "#EF6F6C", "#1FB5A6" } };

    public static void Apply(bool dark, int pal)
    {
        Dark = dark; Pal = pal % pals.Length;
        Bg = ColorTranslator.FromHtml(dark ? "#0c1f22" : "#eaf7f5"); Card = ColorTranslator.FromHtml(dark ? "#14343a" : "#ffffff");
        Text = ColorTranslator.FromHtml(dark ? "#e6f5f3" : "#0c1f22"); Muted = ColorTranslator.FromHtml(dark ? "#8fb5b8" : "#5b7a7d");
        Border = ColorTranslator.FromHtml(dark ? "#25555e" : "#c9e2df");
        Accent = ColorTranslator.FromHtml(pals[Pal][0]); Accent2 = ColorTranslator.FromHtml(pals[Pal][1]); Accent3 = ColorTranslator.FromHtml(pals[Pal][2]);
    }
    public static Color Mix(Color a, Color b, double t) { return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }
    public static void FooterFix(Form f)   // dialogs: footer strip stays at the back, buttons on top
    {
        foreach (Control c in f.Controls.Cast<Control>().ToList()) if (c is Button) c.BringToFront();
    }
    public static Color BackBehind(Control c)
    {
        for (Control p = c.Parent; p != null; p = p.Parent) { if (p is global::Card) return Card; if (p.BackColor.A == 255) return p.BackColor; }
        return Bg;
    }
    static MethodInfo ipb = typeof(Control).GetMethod("InvokePaintBackground", BindingFlags.NonPublic | BindingFlags.Instance), ip = typeof(Control).GetMethod("InvokePaint", BindingFlags.NonPublic | BindingFlags.Instance);
    public static void PaintParent(Control c, PaintEventArgs e)   // lets rounded controls show whatever is behind them (cards, gradients)
    {
        if (c.Parent == null || ipb == null) { e.Graphics.Clear(Bg); return; }
        var g = e.Graphics; var st = g.Save(); g.TranslateTransform(-c.Left, -c.Top);
        var pe = new PaintEventArgs(g, new Rectangle(0, 0, c.Parent.Width, c.Parent.Height));
        try { ipb.Invoke(c, new object[] { c.Parent, pe }); if (ip != null) ip.Invoke(c, new object[] { c.Parent, pe }); } catch { g.Restore(st); g.Clear(Bg); return; }
        g.Restore(st);
    }
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string ids);
    public static void Modern(Control k)   // thin, theme-aware scrollbars and dropdowns (Windows 10/11)
    {
        if (!Dark && (k is ComboBox || k is TextBox)) return;
        string name = (k is ComboBox || k is TextBox) ? "DarkMode_CFD" : (Dark ? "DarkMode_Explorer" : "Explorer");
        Action go = delegate { try { SetWindowTheme(k.Handle, name, null); } catch { } };
        if (k.IsHandleCreated) go(); else k.HandleCreated += delegate { go(); };
    }
    public static Button Btn(string text, int x, int y, int w, string role)
    {
        var b = new PillButton { Text = text, Left = x, Top = y, Width = w, Height = 32, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Tag = role, Font = new Font("Segoe UI", 9f) };
        b.FlatAppearance.BorderSize = 0; return b;
    }
    public static GraphicsPath Round(Rectangle r, int d)
    {
        var p = new GraphicsPath(); p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }
    public static void Skin(Control c)
    {
        foreach (Control k in c.Controls)
        {
            string tag = k.Tag as string;
            if (k is Button)
            {
                if (tag != "nav")
                {
                    var b = (Button)k;   // a = main action, b = soft accent (secondary / destructive), c = neutral
                    if (tag == "a") { b.BackColor = Accent; b.ForeColor = Color.White; }
                    else if (tag == "b") { b.BackColor = Mix(Card, Accent2, 0.22); b.ForeColor = Text; }
                    else { b.BackColor = Mix(Card, Text, Dark ? 0.10 : 0.07); b.ForeColor = Text; }
                    var pb2 = b as PillButton; if (pb2 != null) pb2.Edge = tag == "b" ? Accent2 : Color.Empty;
                }
            }
            else if (k is LinkLabel) { var l = (LinkLabel)k; l.LinkColor = Accent; l.ActiveLinkColor = Accent2; l.BackColor = Color.Transparent; }
            else if (k is Label) { k.BackColor = Color.Transparent; if (tag != "white") k.ForeColor = tag == "muted" ? Muted : tag == "title" ? Accent : tag == "sub" ? Accent2 : Text; }
            else if (k is CheckBox || k is RadioButton) { k.ForeColor = Text; if (!(k is SwitchBox) && !(k is RadioBox)) k.BackColor = Color.Transparent; }
            else if (k is TextBox) { k.BackColor = Card; k.ForeColor = Text; ((TextBox)k).BorderStyle = tag == "flat" ? BorderStyle.None : BorderStyle.FixedSingle; }
            else if (k is NumericUpDown) { k.BackColor = Card; k.ForeColor = Text; }
            else if (k is ComboBox) { var cb = (ComboBox)k; cb.BackColor = Card; cb.ForeColor = Text; cb.FlatStyle = FlatStyle.Flat; }
            else if (k is ListView) { k.BackColor = Card; k.ForeColor = Text; ((ListView)k).BorderStyle = BorderStyle.None; }
            else if (k is ListBox) { k.BackColor = Card; k.ForeColor = Text; ((ListBox)k).BorderStyle = tag == "flat" ? BorderStyle.None : BorderStyle.FixedSingle; }
            else if (k is Panel)
            {
                if (tag == "banner") { } else if (tag == "line") k.BackColor = Accent; else if (tag == "card") k.BackColor = Card; else k.BackColor = Bg;
            }
            if (k is ListView || k is ListBox || k is ComboBox || k is TextBox || k is FlowLayoutPanel || (k is Panel && ((Panel)k).AutoScroll)) Modern(k);
            if (k.Controls.Count > 0) Skin(k);
        }
    }
    public static Bitmap Logo(int s)
    {
        string f = Path.Combine(App.ExeDir, "logo.png");
        var bmp = new Bitmap(s, s);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (File.Exists(f)) { try { using (var img = Image.FromFile(f)) g.DrawImage(img, 0, 0, s, s); return bmp; } catch { } }
            float u = s / 100f;
            using (var br = new LinearGradientBrush(new Rectangle(0, 0, s, s), Accent, Base, 55f)) g.FillEllipse(br, 0, 0, s - 1, s - 1);
            g.FillPolygon(Brushes.White, new PointF[] { new PointF(50 * u, 42 * u), new PointF(22 * u, 35 * u), new PointF(22 * u, 74 * u), new PointF(50 * u, 81 * u) });
            using (var br = new SolidBrush(Color.FromArgb(232, 244, 243))) g.FillPolygon(br, new PointF[] { new PointF(50 * u, 42 * u), new PointF(78 * u, 35 * u), new PointF(78 * u, 74 * u), new PointF(50 * u, 81 * u) });
            using (var p = new Pen(Accent, 2 * u)) g.DrawLine(p, 50 * u, 42 * u, 50 * u, 81 * u);
            using (var br = new SolidBrush(Accent2)) { g.FillRectangle(br, 44 * u, 13 * u, 12 * u, 22 * u); g.FillRectangle(br, 36 * u, 18 * u, 28 * u, 8 * u); }
        }
        return bmp;
    }
}

// ===== Rounded, bordered card panel (site-style) =====
// ===== Windows 11 style controls =====
static class Icons   // simple vector icons drawn in the text colour (no icon font needed)
{
    public static void Draw(Graphics g, string kind, int x, int y, int s, Color c)
    {
        var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias; float u = s / 16f;
        Func<float, float, PointF> P = (px, py) => new PointF(x + px * u, y + py * u);
        using (var pen = new Pen(c, Math.Max(1.3f, 1.5f * u)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        using (var br = new SolidBrush(c))
        {
            switch (kind)
            {
                case "home": g.DrawLines(pen, new[] { P(1.5f, 8), P(8, 2), P(14.5f, 8) }); g.DrawLines(pen, new[] { P(3.5f, 7), P(3.5f, 14), P(12.5f, 14), P(12.5f, 7) }); break;
                case "list": for (int i = 0; i < 3; i++) { float yy = 3 + i * 5; g.FillEllipse(br, x + 1.5f * u, y + (yy - 1) * u, 2 * u, 2 * u); g.DrawLine(pen, P(6, yy), P(14.5f, yy)); } break;
                case "timer": g.DrawEllipse(pen, x + 2 * u, y + 3 * u, 12 * u, 12 * u); g.DrawLine(pen, P(8, 9), P(8, 5.5f)); g.DrawLine(pen, P(8, 9), P(10.5f, 10.5f)); g.DrawLine(pen, P(6, 1), P(10, 1)); break;
                case "chart": g.DrawLine(pen, P(3, 14), P(3, 9)); g.DrawLine(pen, P(8, 14), P(8, 3)); g.DrawLine(pen, P(13, 14), P(13, 6)); break;
                case "folder": g.DrawLines(pen, new[] { P(1.5f, 4), P(6, 4), P(7.5f, 5.5f), P(14.5f, 5.5f), P(14.5f, 13), P(1.5f, 13), P(1.5f, 4) }); break;
                case "gear":
                    g.DrawEllipse(pen, x + 5 * u, y + 5 * u, 6 * u, 6 * u);
                    for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a); g.DrawLine(pen, P(8 + ca * 5, 8 + sa * 5), P(8 + ca * 7, 8 + sa * 7)); }
                    break;
                case "info": g.DrawEllipse(pen, x + 1.5f * u, y + 1.5f * u, 13 * u, 13 * u); g.DrawLine(pen, P(8, 7), P(8, 11.5f)); g.FillEllipse(br, x + 7 * u, y + 4 * u, 2 * u, 2 * u); break;
                case "plus": g.DrawLine(pen, P(8, 3), P(8, 13)); g.DrawLine(pen, P(3, 8), P(13, 8)); break;
                case "calendar": g.DrawRectangle(pen, x + 2 * u, y + 3 * u, 12 * u, 11 * u); g.DrawLine(pen, P(2, 6.5f), P(14, 6.5f)); g.DrawLine(pen, P(5.5f, 1.5f), P(5.5f, 4.5f)); g.DrawLine(pen, P(10.5f, 1.5f), P(10.5f, 4.5f)); break;
                case "refresh": g.DrawArc(pen, x + 2.5f * u, y + 2.5f * u, 11 * u, 11 * u, 20, 290); g.DrawLines(pen, new[] { P(13.5f, 1.5f), P(13.5f, 5), P(10, 5) }); break;
                case "download": g.DrawLine(pen, P(8, 2), P(8, 10)); g.DrawLines(pen, new[] { P(4.5f, 7), P(8, 10.5f), P(11.5f, 7) }); g.DrawLine(pen, P(3, 14), P(13, 14)); break;
            }
        }
        g.SmoothingMode = old;
    }
}

class PillButton : Button   // compact Windows-style button with crisp edges, subtle border, hover / pressed states and optional icon
{
    public string Icon; public Action<Graphics> Overlay; public Color Edge = Color.Empty; bool hover, down;
    public PillButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width > 4 && Height > 4) using (var gp = Theme.Round(new Rectangle(0, 0, Width, Height), 6)) Region = new Region(gp);   // rounded corners by clipping: no blending with the background, so no stray dark pixels
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }   // everything inside the rounded shape is painted in OnPaint
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; Color fill = BackColor;
        if (!Enabled) fill = Theme.Mix(fill, Theme.Bg, 0.55);
        else if (down) fill = FlatAppearance.MouseDownBackColor.IsEmpty ? Theme.Mix(fill, Color.Black, 0.16) : FlatAppearance.MouseDownBackColor;
        else if (hover) fill = FlatAppearance.MouseOverBackColor.IsEmpty ? Theme.Mix(fill, Theme.Dark ? Color.White : Color.Black, Theme.Dark ? 0.12 : 0.07) : FlatAppearance.MouseOverBackColor;
        // Flat button like version 6.1: one solid opaque fill, no outline of any colour (the rounded corners come from the control's Region).
        g.Clear(fill);
        Color fg = Enabled ? ForeColor : Theme.Muted; int ic = string.IsNullOrEmpty(Icon) ? 0 : 16, gap = ic > 0 ? 7 : 0;
        int tw = TextRenderer.MeasureText(g, Text, Font, new Size(Width, Height), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
        int x0 = Math.Max(6, (Width - (ic + gap + tw)) / 2);
        if (ic > 0) Icons.Draw(g, Icon, x0, (Height - ic) / 2, ic, fg);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(x0 + ic + gap, 0, Math.Max(10, Width - x0 - ic - gap - 4), Height), fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) using (var gp = Theme.Round(Rectangle.Inflate(new Rectangle(0, 0, Width - 1, Height - 1), -2, -2), 6)) using (var pen = new Pen(Theme.Accent, 1f)) g.DrawPath(pen, gp);
        if (Overlay != null) Overlay(g);
    }
}

class SwitchBox : CheckBox   // Windows 11 check box: rounded square with a tick
{
    public SwitchBox() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); Cursor = Cursors.Hand; }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Theme.BackBehind(this)); g.SmoothingMode = SmoothingMode.AntiAlias; int d = 20, y = (Height - d) / 2; var box = new Rectangle(1, y, d - 1, d - 1);
        using (var gp = Theme.Round(box, 8))
        {
            if (Checked) { using (var br = new SolidBrush(Theme.Accent)) g.FillPath(br, gp); }
            else using (var pen = new Pen(Theme.Muted, 1.2f)) g.DrawPath(pen, gp);
        }
        if (Checked) using (var pen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            g.DrawLines(pen, new PointF[] { new PointF(5.5f, y + 10f), new PointF(8.5f, y + 13f), new PointF(14.5f, y + 6.5f) });
        TextRenderer.DrawText(g, Text, Font, new Rectangle(d + 10, 0, Math.Max(10, Width - d - 10), Height), Enabled ? ForeColor : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

class RadioBox : RadioButton   // Windows 11 radio button
{
    public RadioBox() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); Cursor = Cursors.Hand; }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Theme.BackBehind(this)); g.SmoothingMode = SmoothingMode.AntiAlias; int d = 18, y = (Height - d) / 2;
        using (var pen = new Pen(Checked ? Theme.Accent : Theme.Muted, 1.5f)) g.DrawEllipse(pen, 1, y, d, d);
        if (Checked) using (var br = new SolidBrush(Theme.Accent)) g.FillEllipse(br, 6, y + 5, 8, 8);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(d + 12, 0, Math.Max(10, Width - d - 12), Height), Enabled ? ForeColor : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

class RoundBar : Control   // thin rounded progress bar
{
    int val; public int Value { get { return val; } set { val = Math.Max(0, Math.Min(100, value)); Invalidate(); } }
    public RoundBar() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Theme.BackBehind(this)); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; int h = Math.Min(Math.Max(6, Height - 8), 12);
        var tr = new Rectangle(0, (Height - h) / 2, Width - 1, h);
        using (var gp = Theme.Round(tr, h)) using (var br = new SolidBrush(Theme.Border)) g.FillPath(br, gp);
        if (val > 0) { var fr = new Rectangle(tr.X, tr.Y, Math.Max(h, tr.Width * val / 100), h); using (var gp = Theme.Round(fr, h)) using (var br = new LinearGradientBrush(fr, Theme.Accent, Theme.Accent2, 0f)) g.FillPath(br, gp); }
    }
}

class Card : Panel
{
    public Card() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), 16))
        {
            using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
            using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, p);
        }
        base.OnPaint(e);
    }
}

// ===== Soft in-memory bell shared by the timers =====
static class Sound
{
    public static void Chime()
    {
        try
        {
            int sr = 22050; var pcm = new List<short>(); double[] notes = { 784, 1047, 1319 };
            foreach (double f in notes) for (int i = 0; i < sr * 0.5; i++) { double tt = i / (double)sr; pcm.Add((short)(Math.Sin(2 * Math.PI * f * tt) * Math.Exp(-5 * tt) * 5000)); }
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(new char[] { 'R', 'I', 'F', 'F' }); w.Write(36 + pcm.Count * 2); w.Write(new char[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sr); w.Write(sr * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new char[] { 'd', 'a', 't', 'a' }); w.Write(pcm.Count * 2); foreach (var s in pcm) w.Write(s);
            ms.Position = 0; var player = new SoundPlayer(ms); player.Play();
        }
        catch { }
    }
}

// ===== Install / uninstall =====
static class Installer
{
    const string Key = @"Software\StudyTracker";
    const string Unin = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\StudyTracker";
    public static string InstallDir() { using (var k = Registry.CurrentUser.OpenSubKey(Key)) { return k == null ? null : k.GetValue("InstallDir") as string; } }
    public static string DefaultDir()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (string.IsNullOrEmpty(pf)) pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return Path.Combine(pf, "Study Tracker");
    }
    static bool Same(string a, string b) { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
    public static bool RunningInstalled() { var d = InstallDir(); return d != null && Same(d, App.ExeDir); }
    static string DesktopLnk() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Study Tracker.lnk"); }
    static string MenuDir() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Study Tracker"); }

    static void MakeIco(string path)
    {
        using (var bmp = Theme.Logo(256))
        {
            var ms = new MemoryStream(); bmp.Save(ms, ImageFormat.Png); byte[] png = ms.ToArray();
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32); w.Write(png.Length); w.Write(22); w.Write(png);
            }
        }
    }
    static void Shortcut(string lnk, string target, string icon)
    {
        Type t = Type.GetTypeFromProgID("WScript.Shell"); object sh = Activator.CreateInstance(t);
        object l = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk }); Type lt = l.GetType();
        lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, l, new object[] { target });
        lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, l, new object[] { Path.GetDirectoryName(target) });
        lt.InvokeMember("Description", BindingFlags.SetProperty, null, l, new object[] { "Study Tracker by 3Mkay Derado" });
        lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, l, new object[] { icon });
        lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, l, null);
    }
    public static bool CanWrite(string dir)
    {
        try { Directory.CreateDirectory(dir); string f = Path.Combine(dir, "~w.tmp"); File.WriteAllText(f, "x"); File.Delete(f); return true; } catch { return false; }
    }
    public static bool RunBat(List<string> lines, bool elevate, bool wait)
    {
        string bat = Path.Combine(Path.GetTempPath(), "st_" + Guid.NewGuid().ToString("N") + ".bat");
        lines.Insert(0, "@echo off"); lines.Add("del \"%~f0\"");
        File.WriteAllLines(bat, lines.ToArray(), Encoding.Default);
        try
        {
            var psi = new ProcessStartInfo(bat) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
            if (elevate) psi.Verb = "runas";
            var p = Process.Start(psi); if (wait) p.WaitForExit(); return true;
        }
        catch { return false; }
    }

    public static bool Install(string dir, bool desk, bool menu, out string err)
    {
        err = null; string src = Application.ExecutablePath, exe = Path.Combine(dir, "StudyTracker.exe"), ico = Path.Combine(dir, "StudyTracker.ico");
        string tmpIco = Path.Combine(Path.GetTempPath(), "StudyTracker.ico"); string logo = Path.Combine(App.ExeDir, "logo.png");
        try { MakeIco(tmpIco); } catch (Exception e) { err = "Could not prepare icon: " + e.Message; return false; }
        try
        {
            Directory.CreateDirectory(dir); File.Copy(src, exe, true); File.Copy(tmpIco, ico, true);
            if (File.Exists(logo)) File.Copy(logo, Path.Combine(dir, "logo.png"), true);
        }
        catch
        {
            var l = new List<string> { "mkdir \"" + dir + "\" 2>nul", "copy /y \"" + src + "\" \"" + exe + "\" >nul", "copy /y \"" + tmpIco + "\" \"" + ico + "\" >nul" };
            if (File.Exists(logo)) l.Add("copy /y \"" + logo + "\" \"" + Path.Combine(dir, "logo.png") + "\" >nul");
            RunBat(l, true, true);
        }
        if (!File.Exists(exe)) { err = "Installation was cancelled or the folder is not writable."; return false; }
        using (var k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue("InstallDir", dir);
        using (var k = Registry.CurrentUser.CreateSubKey(Unin))
        {
            k.SetValue("DisplayName", "Study Tracker"); k.SetValue("Publisher", "3Mkay Derado"); k.SetValue("InstallLocation", dir);
            k.SetValue("DisplayIcon", ico); k.SetValue("UninstallString", "\"" + exe + "\" /uninstall"); k.SetValue("NoModify", 1); k.SetValue("NoRepair", 1);
        }
        try
        {
            if (desk) Shortcut(DesktopLnk(), exe, ico);
            if (menu) { Directory.CreateDirectory(MenuDir()); Shortcut(Path.Combine(MenuDir(), "Study Tracker.lnk"), exe, ico); }
        }
        catch (Exception e) { err = "Installed, but a shortcut could not be created: " + e.Message; }
        return true;
    }

    public static bool Uninstall(IWin32Window owner)
    {
        string dir = InstallDir();
        if (dir == null || !Same(dir, App.ExeDir)) { MessageBox.Show(owner, "Study Tracker is not installed (you are running it without installing), so there is nothing to uninstall.", "Uninstall"); return false; }
        if (MessageBox.Show(owner, "Remove Study Tracker and its shortcuts from this computer?", "Uninstall", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
        bool wipe = MessageBox.Show(owner, "Also delete your saved study data (tasks and history)?\n\nChoose No to keep it in case you reinstall.", "Uninstall", MessageBoxButtons.YesNo) == DialogResult.Yes;
        try { File.Delete(DesktopLnk()); } catch { }
        try { File.Delete(Path.Combine(MenuDir(), "Study Tracker.lnk")); Directory.Delete(MenuDir()); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(Unin, false); Registry.CurrentUser.DeleteSubKeyTree(Key, false); } catch { }
        if (wipe) { try { Directory.Delete(App.DataDir, true); } catch { } }
        var l = new List<string> { "ping 127.0.0.1 -n 3 >nul",
            "del /f /q \"" + Path.Combine(dir, "StudyTracker.exe") + "\"", "del /f /q \"" + Path.Combine(dir, "StudyTracker.ico") + "\"",
            "del /f /q \"" + Path.Combine(dir, "logo.png") + "\"", "rmdir \"" + dir + "\"" };
        RunBat(l, !CanWrite(dir), false);
        MessageBox.Show(owner, "Study Tracker has been uninstalled. Thanks for using it!", "Uninstall");
        return true;
    }
}

// ===== GitHub updater: checks the last commit date, downloads the repo zip to a temp folder, builds/installs, cleans up =====
static class Updater
{
    public const string Version = "6.11";
    // Optional anonymous usage count via GoatCounter. Leave empty to disable everything.
    // Put your free GoatCounter site code here, e.g. "studytracker" for https://studytracker.goatcounter.com
    public static string StatsCode = "3mkayderado";
    public static string LastPingInfo = "no count sent yet", LastCountInfo = "";
    public static bool SendPing(string path)
    {
        if (StatsCode.Length == 0) { LastPingInfo = "counter not configured"; return false; }
        try
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            var req = (HttpWebRequest)WebRequest.Create("https://" + StatsCode + ".goatcounter.com/count?p=" + Uri.EscapeDataString(path) + "&t=" + Uri.EscapeDataString("Study Tracker v" + Version) + "&rnd=" + DateTime.UtcNow.Ticks);
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) StudyTracker/" + Version;   // identifies the app, but looks like a normal client so it is not treated as a crawler
            req.Accept = "image/gif,image/*;q=0.8"; req.Headers[HttpRequestHeader.AcceptLanguage] = "en"; req.Timeout = 8000; req.ReadWriteTimeout = 8000;
            using (var r = (HttpWebResponse)req.GetResponse())
            {
                bool ok = (int)r.StatusCode < 400; LastPingInfo = (ok ? "sent OK" : "rejected") + " (HTTP " + (int)r.StatusCode + ") at " + DateTime.Now.ToString("HH:mm"); return ok;
            }
        }
        catch (Exception e) { LastPingInfo = "failed: " + e.Message; return false; }
    }
    // reads a public GoatCounter number (needs "Allow adding visitor counts on your website" in its settings); null when unavailable
    public static string GetCount(string path, string query)
    {
        if (StatsCode.Length == 0) return null;
        try
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            var req = (HttpWebRequest)WebRequest.Create("https://" + StatsCode + ".goatcounter.com/counter/" + Uri.EscapeDataString(path) + ".json" + (query.Length > 0 ? "?" + query : ""));
            req.UserAgent = "StudyTracker/" + Version; req.Timeout = 8000; req.ReadWriteTimeout = 8000;
            string json;
            using (var r = (HttpWebResponse)req.GetResponse()) using (var sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) json = sr.ReadToEnd();
            var m = Regex.Match(json, "\"count_unique\"\\s*:\\s*\"?([\\d,\\.]+)");
            if (!m.Success) m = Regex.Match(json, "\"count\"\\s*:\\s*\"?([\\d,\\.]+)");
            if (!m.Success) { LastCountInfo = "unexpected reply: " + (json.Length > 50 ? json.Substring(0, 50) : json).Replace("\n", " "); return null; }
            return m.Groups[1].Value;
        }
        catch (WebException e) { var hr = e.Response as HttpWebResponse; LastCountInfo = hr != null ? "HTTP " + (int)hr.StatusCode : e.Status.ToString(); return (hr != null && hr.StatusCode == HttpStatusCode.NotFound) ? "0" : null; }
        catch (Exception e) { LastCountInfo = e.Message; return null; }
    }
    const string Owner = "kjnrmbiriri01", Repo = "Study-Tracker";
    public static string TempRoot { get { return Path.Combine(Path.GetTempPath(), "StudyTrackerUpdate"); } }
    static WebClient Client()
    {
        try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
        var wc = new WebClient(); wc.Headers[HttpRequestHeader.UserAgent] = "StudyTracker-Updater"; wc.Encoding = Encoding.UTF8; return wc;
    }
    public static bool GetLatest(out DateTime utc, out string err)
    {
        utc = DateTime.MinValue; err = null;
        try
        {
            using (var wc = Client())
            {
                string json = wc.DownloadString("https://api.github.com/repos/" + Owner + "/" + Repo + "/commits?per_page=1");
                var m = Regex.Match(json, "\"committer\"\\s*:\\s*\\{[^{}]*?\"date\"\\s*:\\s*\"([^\"]+)\"");
                if (!m.Success || !DateTime.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc))
                { err = "Unexpected reply from GitHub."; return false; }
                return true;
            }
        }
        catch (WebException e)
        {
            var r = e.Response as HttpWebResponse;
            err = (r != null && r.StatusCode == HttpStatusCode.NotFound) ? "Repository not found (it must be public)." : e.Message; return false;
        }
        catch (Exception e) { err = e.Message; return false; }
    }
    public static void Cleanup() { try { Directory.Delete(TempRoot, true); } catch { } }
    public static string Prepare(Action<string> say)
    {
        Cleanup(); Directory.CreateDirectory(TempRoot);
        string zip = Path.Combine(TempRoot, "repo.zip"), ext = Path.Combine(TempRoot, "files");
        say("Downloading the update from GitHub...");
        using (var wc = Client()) wc.DownloadFile("https://github.com/" + Owner + "/" + Repo + "/archive/HEAD.zip", zip);
        say("Unpacking...");
        ZipFile.ExtractToDirectory(zip, ext);
        string cs = Directory.GetFiles(ext, "StudyTracker.cs", SearchOption.AllDirectories).FirstOrDefault();
        string ready = Directory.GetFiles(ext, "StudyTracker.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (cs == null && ready == null) throw new Exception("The repository has no StudyTracker.cs or StudyTracker.exe.");
        if (cs != null)
        {
            say("Building the new version...");
            string win = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
            string csc = Path.Combine(win, @"Microsoft.NET\Framework64\v4.0.30319\csc.exe");
            if (!File.Exists(csc)) csc = Path.Combine(win, @"Microsoft.NET\Framework\v4.0.30319\csc.exe");
            string outExe = Path.Combine(TempRoot, "StudyTracker.exe");
            string args = "/nologo /target:winexe /out:\"" + outExe + "\" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll \"" + cs + "\"";
            string log = "";
            try
            {
                var psi = new ProcessStartInfo(csc, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, WorkingDirectory = Path.GetDirectoryName(cs) };
                using (var p = Process.Start(psi)) { log = p.StandardOutput.ReadToEnd(); p.WaitForExit(); }
            }
            catch (Exception e) { log = e.Message; }
            if (File.Exists(outExe)) return outExe;
            if (ready == null) throw new Exception("The new version could not be built.\n" + log);
        }
        return ready;
    }
    public static bool Apply(string newExe)
    {
        string target = Application.ExecutablePath, dir = App.ExeDir;
        var l = new List<string> {
            "ping 127.0.0.1 -n 4 >nul",
            "copy /y \"" + newExe + "\" \"" + target + "\" >nul 2>&1",
            "if errorlevel 1 (ping 127.0.0.1 -n 6 >nul & copy /y \"" + newExe + "\" \"" + target + "\" >nul 2>&1)",
            "explorer.exe \"" + target + "\"",
            "ping 127.0.0.1 -n 3 >nul",
            "rmdir /s /q \"" + TempRoot + "\"" };
        return Installer.RunBat(l, !Installer.CanWrite(dir), false);
    }
}

class SetupForm : Form
{
    public int Choice; public string Dir; public bool Desk = true, StartMenu = true;
    public SetupForm()
    {
        Text = "Study Tracker Setup"; ClientSize = new Size(600, 400); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 9); BackColor = Theme.Bg;
        var ban = new Panel { Dock = DockStyle.Top, Height = 90, Tag = "banner" };
        ban.Paint += (s, e) => { using (var gb = new LinearGradientBrush(ban.ClientRectangle, Theme.Base, Theme.Accent, 0f)) e.Graphics.FillRectangle(gb, ban.ClientRectangle); };
        var pic = new PictureBox { Left = 18, Top = 15, Width = 60, Height = 60, Image = Theme.Logo(60), BackColor = Color.Transparent };
        var h = new Label { Left = 92, Top = 16, Width = 480, Height = 34, Text = "Welcome to Study Tracker", Font = new Font("Segoe UI", 15, FontStyle.Bold), Tag = "white", ForeColor = Color.White };
        var h2 = new Label { Left = 94, Top = 54, Width = 480, Height = 24, Text = "Created by 3Mkay Derado", Tag = "white", ForeColor = Color.FromArgb(210, 245, 240) };
        ban.Controls.AddRange(new Control[] { pic, h, h2 });
        var l1 = new Label { Left = 24, Top = 110, Width = 550, Height = 24, Text = "Where should Study Tracker be installed?", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        var tb = new TextBox { Left = 24, Top = 140, Width = 440, Text = Installer.DefaultDir() };
        var br2 = Theme.Btn("Browse...", 474, 137, 100, "c");
        var cd = new SwitchBox { Left = 24, Top = 192, Width = 400, Text = "Create a desktop shortcut", Checked = true };
        var cm = new SwitchBox { Left = 24, Top = 222, Width = 400, Text = "Add to the Start menu", Checked = true };
        var note = new Label { Left = 24, Top = 262, Width = 550, Height = 40, Tag = "muted", Text = "Installing to Program Files will ask Windows for permission once. Your study data is kept separately in your user folder." };
        var bi = Theme.Btn("Install", 24, 330, 120, "a"); bi.Height = 38;
        var br = Theme.Btn("Run without installing", 154, 330, 200, "b"); br.Height = 38;
        var bc = Theme.Btn("Cancel", 474, 330, 100, "c"); bc.Height = 38;
        Controls.AddRange(new Control[] { ban, l1, tb, br2, cd, cm, note, bi, br, bc });
        br2.Click += delegate
        {
            using (var f = new FolderBrowserDialog())
            { f.Description = "Choose the folder to install into"; if (f.ShowDialog() == DialogResult.OK) { string p = f.SelectedPath; tb.Text = p.ToLower().Contains("study tracker") ? p : Path.Combine(p, "Study Tracker"); } }
        };
        bi.Click += delegate { if (tb.Text.Trim() == "") return; Dir = tb.Text.Trim(); Desk = cd.Checked; StartMenu = cm.Checked; Choice = 1; Close(); };
        br.Click += delegate { Choice = 2; Close(); }; bc.Click += delegate { Choice = 0; Close(); };
        Theme.Skin(this);
    }
}

// ===== Pop-up timer: rounded, draggable, compact dashboard =====
class MiniTimer : Form
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, int w, int l);
    public event Action<int, bool> Finished;
    int total, left, elapsed; bool paused, ended; public bool Muted;
    Label lbl, title; Panel clock; Button bPause, bPin, bMute, bStop, bClose; PictureBox badge;
    Timer t = new Timer { Interval = 1000 };
    public MiniTimer(string task, int minutes, bool muted)
    {
        Muted = muted; total = left = Math.Max(1, minutes) * 60;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; Text = "Timer";   // no Windows title bar: the header below is part of the timer
        ClientSize = new Size(350, 205); MinimumSize = new Size(300, 195);
        try { Icon = Icon.FromHandle(Theme.Logo(32).GetHicon()); } catch { }
        BackColor = Color.FromArgb(15, 20, 29); Font = new Font("Segoe UI", 9);
        var wa = Screen.PrimaryScreen.WorkingArea; StartPosition = FormStartPosition.Manual; Location = new Point(wa.Right - Width - 14, wa.Top + 90);
        Shown += delegate { try { int darkCaption = 1; DwmSetWindowAttribute(Handle, 20, ref darkCaption, 4); DwmSetWindowAttribute(Handle, 19, ref darkCaption, 4); } catch { } };
        title = new Label { Text = string.IsNullOrEmpty(task) ? "Timer" : task, ForeColor = Color.White, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
        clock = new Panel { BackColor = BackColor }; clock.Paint += DrawClock;
        lbl = new Label { TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Segoe UI", 16, FontStyle.Bold), BackColor = Color.Transparent };
        clock.Controls.Add(lbl);
        bPause = Mk("Ⅱ  Pause", 0, 0, 0, 0, Color.FromArgb(255, 100, 35));
        bStop = Mk("Finish", 0, 0, 0, 0, Color.FromArgb(36, 46, 62));
        bPin = Mk("Pin", 0, 0, 0, 0, Color.FromArgb(36, 46, 62));
        bMute = Mk(Muted ? "Muted" : "Sound", 0, 0, 0, 0, Color.FromArgb(36, 46, 62));
        badge = new PictureBox { Image = Theme.Logo(18), SizeMode = PictureBoxSizeMode.Zoom, BackColor = BackColor };
        bClose = Mk("X", 0, 0, 0, 0, BackColor); bClose.ForeColor = Color.FromArgb(170, 182, 200);
        bClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 52, 52); bClose.FlatAppearance.MouseDownBackColor = Color.FromArgb(160, 40, 40);
        bClose.Click += delegate { Done(false); };
        Controls.AddRange(new Control[] { title, badge, bClose, clock, bPause, bStop, bPin, bMute });
        Resize += delegate { LayoutTimer(); Region = new Region(Theme.Round(new Rectangle(0, 0, Width, Height), 18)); }; LayoutTimer(); Region = new Region(Theme.Round(new Rectangle(0, 0, Width, Height), 18));
        MouseDown += Drag; badge.MouseDown += Drag; title.MouseDown += Drag; clock.MouseDown += Drag; lbl.MouseDown += Drag;
        Show2();
        t.Tick += delegate { if (paused || ended) return; left--; elapsed++; Show2(); if (left <= 0) Done(true); };
        t.Start();
        bPause.Click += delegate { paused = !paused; bPause.Text = paused ? "▶  Start" : "Ⅱ  Pause"; };
        bPin.Click += delegate { TopMost = !TopMost; bPin.Text = TopMost ? "Pinned" : "Pin"; bPin.BackColor = TopMost ? Theme.Accent : Color.FromArgb(36, 46, 62); };
        bMute.Click += delegate { Muted = !Muted; bMute.Text = Muted ? "Muted" : "Sound"; };
        bStop.Click += delegate { Done(false); };
        FormClosing += delegate { if (!ended) { ended = true; t.Stop(); if (Finished != null) Finished((int)Math.Round(elapsed / 60.0), false); } };
    }
    void LayoutTimer()
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        badge.SetBounds(12, 6, 18, 18); title.SetBounds(36, 4, w - 36 - 44, 22); bClose.SetBounds(w - 40, 0, 40, 28);
        int d = Math.Max(72, Math.Min(88, w - 24));
        clock.SetBounds((w - d) / 2, 29, d, d); lbl.SetBounds(0, 0, d, d);
        int y = clock.Bottom + 2, gap = 5, bottom = h - y - 8;
        int pauseW = Math.Min(150, w - 24);
        bPause.SetBounds((w - pauseW) / 2, y, pauseW, 30);
        int smallY = y + 34, avail = w - 28, smallW = (avail - gap * 2) / 3;
        bStop.SetBounds(14, smallY, smallW, 25);
        bPin.SetBounds(14 + smallW + gap, smallY, smallW, 25);
        bMute.SetBounds(14 + (smallW + gap) * 2, smallY, smallW, 25);
    }
    void DrawClock(object sender, PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(6, 6, clock.Width - 12, clock.Height - 12);
        using (var p = new Pen(Color.FromArgb(35, 44, 60), 7)) g.DrawArc(p, r, 0, 360);
        float pct = total > 0 ? Math.Max(0, Math.Min(1, elapsed / (float)total)) : 0;
        using (var p = new Pen(Color.FromArgb(255, 100, 35), 7) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(p, r, -90, 360 * pct);
    }
    protected override void WndProc(ref Message m)   // borderless window: keep edge/corner resizing
    {
        if (m.Msg == 0x84)   // WM_NCHITTEST
        {
            base.WndProc(ref m);
            if ((int)m.Result == 1)   // HTCLIENT
            {
                int lp = unchecked((int)(long)m.LParam); var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                int g = 8; bool l = p.X < g, r = p.X > ClientSize.Width - g, t = p.Y < g, b = p.Y > ClientSize.Height - g;
                if (l && t) m.Result = (IntPtr)13; else if (r && t) m.Result = (IntPtr)14; else if (l && b) m.Result = (IntPtr)16; else if (r && b) m.Result = (IntPtr)17;
                else if (l) m.Result = (IntPtr)10; else if (r) m.Result = (IntPtr)11; else if (t) m.Result = (IntPtr)12; else if (b) m.Result = (IntPtr)15;
            }
            return;
        }
        base.WndProc(ref m);
    }
    void Drag(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); } }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = Theme.Round(new Rectangle(1, 1, Width - 3, Height - 3), 18)) using (var pen = new Pen(Color.FromArgb(39, 49, 66), 1)) e.Graphics.DrawPath(pen, p);
    }
    Button Mk(string s, int x, int y, int w, int h, Color c)
    {
        var b = new PillButton { Text = s, Left = x, Top = y, Width = w, Height = h, FlatStyle = FlatStyle.Flat, BackColor = c, ForeColor = Color.White, Cursor = Cursors.Hand, Font = new Font("Segoe UI Semibold", 9) };
        b.FlatAppearance.BorderSize = 0; return b;
    }
    void Show2() { lbl.Text = left <= 0 ? "00:00" : (left / 60).ToString("00") + ":" + (left % 60).ToString("00"); if (clock != null) clock.Invalidate(); }
    void Done(bool completed)
    {
        if (ended) return; ended = true; t.Stop();
        if (completed) { lbl.Text = "Done!"; lbl.Font = new Font("Segoe UI", 13, FontStyle.Bold); if (!Muted) Sound.Chime(); }
        if (Finished != null) Finished((int)Math.Round(elapsed / 60.0), completed);
        if (completed) { var c = new Timer { Interval = 6000 }; c.Tick += delegate { c.Stop(); c.Dispose(); Close(); }; c.Start(); } else Close();
    }
}

// ===== Minutes stepper: rounded  [-]  60 min  [+]  =====
class Stepper : Card
{
    public int Min = 0, Max = 9999, Step = 5; int val = 60; string suf = "min"; Label lbl; Button bm, bp;
    public Stepper()
    {
        Width = 128; Height = 36;
        bm = Theme.Btn("-", 3, 3, 30, "a"); bp = Theme.Btn("+", 95, 3, 30, "a"); bm.Height = bp.Height = 30;
        lbl = new Label { Top = 7, Height = 22, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        Controls.AddRange(new Control[] { bm, bp, lbl });
        bm.Click += delegate { Value = val - Step; }; bp.Click += delegate { Value = val + Step; };
        Resize += delegate { Lay(); }; Lay(); Value = val;
    }
    void Lay() { bm.Left = 3; bp.Left = Width - 33; lbl.Left = 34; lbl.Width = Math.Max(20, Width - 68); }
    public event EventHandler Changed;
    bool clock;
    public bool Clock { get { return clock; } set { clock = value; lbl.Text = Shown(); } }   // value = minutes since midnight
    string Shown() { return clock ? (val / 60).ToString("00") + ":" + (val % 60).ToString("00") : val + " " + suf; }
    public int Value { get { return val; } set { int old = val; val = clock ? ((value % 1440) + 1440) % 1440 : Math.Max(Min, Math.Min(Max, value)); lbl.Text = Shown(); if (val != old && Changed != null) Changed(this, EventArgs.Empty); } }
    public string Suffix { get { return suf; } set { suf = value; lbl.Text = Shown(); } }
}

// ===== Modern pop-up used for names, "log time" and the reading timer =====
class AskForm : Form
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
    TextBox tb; Stepper sm, sp;
    public string Answer { get { return tb == null ? "" : tb.Text; } }
    public int Minutes { get { return sm == null ? 0 : sm.Value; } }
    public int Pages { get { return sp == null ? 0 : sp.Value; } }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); try { int v = Theme.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref v, 4); DwmSetWindowAttribute(Handle, 19, ref v, 4); } catch { } }
    void Init(string title, string sub, int h)
    {
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        ClientSize = new Size(440, h); BackColor = Theme.Card; Font = new Font("Segoe UI", 9); Text = title;
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 68 });   // footer strip (colour comes from Theme.Skin)
        Controls.Add(new Label { Left = 24, Top = 16, Width = 392, Height = 28, Text = title, Font = new Font("Segoe UI Semibold", 13), AutoEllipsis = true });
        Controls.Add(new Label { Left = 25, Top = 46, Width = 392, Height = 20, Text = sub, Tag = "muted", AutoEllipsis = true });
    }
    void Buttons(int h, string okText = "Save")
    {
        var ok = Theme.Btn(okText, 24, h - 50, 100, "a"); ok.Height = 32; ok.DialogResult = DialogResult.OK;
        var no = Theme.Btn("Cancel", 132, h - 50, 100, "c"); no.Height = 32; no.DialogResult = DialogResult.Cancel;
        Controls.Add(ok); Controls.Add(no); AcceptButton = ok; CancelButton = no; Theme.Skin(this); Theme.ApplyUserFont(this, true); Theme.FooterFix(this);
    }
    public AskForm(string title, string label, string def)   // text answer
    {
        Init(title, "", 236);
        Controls.Add(new Label { Left = 24, Top = 86, Width = 392, Height = 22, Text = label, Tag = "muted" });
        var card = new Card { Left = 24, Top = 112, Width = 392, Height = 48 };
        tb = new TextBox { Left = 14, Top = 11, Width = 364, Text = def, Tag = "flat", Font = new Font("Segoe UI", 11.5f) };
        card.Controls.Add(tb); Controls.Add(card); Buttons(236);
        Shown += delegate { tb.Focus(); tb.SelectAll(); };
    }
    public bool NoDate;
    public string DateResult { get { return NoDate ? "" : DateTime.Today.AddDays(Minutes).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); } }
    public AskForm(string title, string sub, DateTime? cur, bool dateMode)   // pick a date: "days from today" + quick chips
    {
        int h = 322; Init(title, sub, h);
        Controls.Add(new Label { Left = 24, Top = 86, Width = 392, Height = 22, Text = "How many days from today?", Tag = "muted" });
        int start = cur.HasValue ? Math.Max(0, (int)(cur.Value.Date - DateTime.Today).TotalDays) : 7;
        sm = new Stepper { Left = 24, Top = 112, Width = 190, Min = 0, Max = 730, Step = 1, Suffix = "days", Value = start }; Controls.Add(sm);
        var when = new Label { Left = 226, Top = 119, Width = 200, Height = 24, Tag = "title", Font = new Font("Segoe UI", 10, FontStyle.Bold) }; Controls.Add(when);
        Action upd = delegate { when.Text = DateTime.Today.AddDays(sm.Value).ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture); };
        sm.Changed += delegate { upd(); }; upd();
        int[] q = { 0, 1, 3, 7, 14, 30 }; string[] qn = { "Today", "Tomorrow", "3 days", "1 week", "2 weeks", "1 month" };
        for (int i = 0; i < q.Length; i++) { int v = q[i]; var c = Theme.Btn(qn[i], 24 + i * 66, 166, 62, "a"); c.Height = 30; c.Click += delegate { sm.Value = v; }; Controls.Add(c); }
        var nd = Theme.Btn("No date", 240, h - 50, 100, "c"); nd.Height = 32; nd.Click += delegate { NoDate = true; DialogResult = DialogResult.OK; Close(); }; Controls.Add(nd);
        Buttons(h);
    }
    public AskForm(string title, string sub, int page, int total)   // reading progress: current page + total pages
    {
        int h = 322; Init(title, sub, h);
        Controls.Add(new Label { Left = 24, Top = 86, Width = 392, Height = 22, Text = "Current page", Tag = "muted" });
        sm = new Stepper { Left = 24, Top = 112, Width = 190, Min = 0, Max = 20000, Step = 1, Suffix = "", Value = page }; Controls.Add(sm);
        int[] inc = { 1, 5, 10, 25 };
        for (int i = 0; i < inc.Length; i++) { int v = inc[i]; var c = Theme.Btn("+" + v, 226 + i * 52, 114, 48, "a"); c.Height = 32; c.Click += delegate { sm.Value = sm.Value + v; }; Controls.Add(c); }
        Controls.Add(new Label { Left = 24, Top = 172, Width = 392, Height = 22, Text = "Total pages in this file", Tag = "muted" });
        sp = new Stepper { Left = 24, Top = 198, Width = 190, Min = 0, Max = 20000, Step = 10, Suffix = "", Value = total }; Controls.Add(sp);
        Buttons(h);
    }
    public AskForm(string title, string sub, string label, int def, bool pages)   // minutes (+ optional pages)
    {
        int h = pages ? 346 : 272; Init(title, sub, h);
        Controls.Add(new Label { Left = 24, Top = 86, Width = 392, Height = 22, Text = label, Tag = "muted" });
        sm = new Stepper { Left = 24, Top = 112, Width = 200, Min = 1, Max = 600, Step = 5, Value = def }; Controls.Add(sm);
        int[] q = { 15, 25, 30, 45, 60, 90 };
        for (int i = 0; i < q.Length; i++) { int v = q[i]; var c = Theme.Btn(v + "m", 24 + i * 66, 160, 60, "a"); c.Height = 30; c.Click += delegate { sm.Value = v; }; Controls.Add(c); }
        if (pages)
        {
            Controls.Add(new Label { Left = 24, Top = 208, Width = 392, Height = 22, Text = "Pages read (optional)", Tag = "muted" });
            sp = new Stepper { Left = 24, Top = 234, Width = 200, Min = 0, Max = 5000, Step = 1, Suffix = "pages", Value = 0 }; Controls.Add(sp);
        }
        Buttons(h, pages ? "Save" : "Start");
    }
}

// ===== Weekly timetable: seven rows, one chip per planned study block =====
class TimetableCard : Card
{
    public List<Slot> Slots = new List<Slot>(); public Func<Slot, int> State; public Action<int> OnDay; public Action<Slot> OnSlot;   // State: 0 planned, 1 done, 2 missed
    List<KeyValuePair<Rectangle, Slot>> hits = new List<KeyValuePair<Rectangle, Slot>>();
    static string[] DN = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
    const int Head = 26;
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e); if (e.Button != MouseButtons.Left) return;
        foreach (var h in hits) if (h.Key.Contains(e.Location)) { if (OnSlot != null) OnSlot(h.Value); return; }
        int rh = Math.Max(1, (Height - Head - 8) / 7), day = (e.Y - Head) / rh;
        if (e.Y >= Head && day >= 0 && day < 7 && OnDay != null) OnDay(day);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; hits.Clear();
        TextRenderer.DrawText(g, "Weekly timetable  -  click a day to add a block   (green = done, red = missed)", Theme.F(8f, FontStyle.Bold), new Point(14, 6), Theme.Muted);
        int rh = (Height - Head - 8) / 7, today = ((int)DateTime.Today.DayOfWeek + 6) % 7;
        for (int d = 0; d < 7; d++)
        {
            int y = Head + d * rh; var row = new Rectangle(8, y, Width - 17, rh - 3);
            using (var gp = Theme.Round(row, 10))
            {
                if (d == today) using (var br = new SolidBrush(Color.FromArgb(28, Theme.Accent))) g.FillPath(br, gp);
                using (var pen = new Pen(d == today ? Theme.Accent : Theme.Border)) g.DrawPath(pen, gp);
            }
            TextRenderer.DrawText(g, DN[d], Theme.F(8.5f, FontStyle.Bold), new Rectangle(14, y, 40, rh - 3), d == today ? Theme.Accent : Theme.Text, TextFormatFlags.VerticalCenter);
            int x = 56; var list = Slots.Where(q => q.Day == d).OrderBy(q => q.Time).ToList();
            if (list.Count == 0) TextRenderer.DrawText(g, "free", Theme.F(8f), new Rectangle(x, y, 80, rh - 3), Theme.Muted, TextFormatFlags.VerticalCenter);
            for (int i = 0; i < list.Count; i++)
            {
                var sl = list[i]; string subj = sl.Subject.Length > 14 ? sl.Subject.Substring(0, 13) + "..." : sl.Subject;
                string txt = sl.Time + " " + subj + " " + sl.Minutes + "m"; int w = TextRenderer.MeasureText(g, txt, Theme.F(8f)).Width + 14;
                if (x + w > Width - 16) { TextRenderer.DrawText(g, "+" + (list.Count - i), Theme.F(8f, FontStyle.Bold), new Rectangle(x, y, 40, rh - 3), Theme.Muted, TextFormatFlags.VerticalCenter); break; }
                var chip = new Rectangle(x, y + 3, w, rh - 9); int st = State != null ? State(sl) : 0;
                Color c = st == 1 ? Color.SeaGreen : st == 2 ? Color.FromArgb(230, 90, 90) : Theme.Accent;
                using (var gp = Theme.Round(chip, 12)) { using (var br = new SolidBrush(Color.FromArgb(st == 1 ? 80 : 45, c))) g.FillPath(br, gp); using (var pen = new Pen(c)) g.DrawPath(pen, gp); }
                TextRenderer.DrawText(g, txt, Theme.F(8f), chip, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                hits.Add(new KeyValuePair<Rectangle, Slot>(chip, sl)); x += w + 6;
            }
        }
    }
}

// ===== Add / edit one study block =====
class SlotForm : Form
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); try { int v = Theme.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref v, 4); DwmSetWindowAttribute(Handle, 19, ref v, 4); } catch { } }
    public Slot Result; public bool Deleted;
    public SlotForm(string day, Slot cur, List<string> subjects, bool exists)
    {
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        int h = 316; ClientSize = new Size(440, h); BackColor = Theme.Card; Font = new Font("Segoe UI", 9); Text = "Study block";
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 68 });   // footer strip (colour comes from Theme.Skin)
        Controls.Add(new Label { Left = 24, Top = 16, Width = 392, Height = 28, Text = "Study block", Font = new Font("Segoe UI Semibold", 13), AutoEllipsis = true });
        Controls.Add(new Label { Left = 25, Top = 46, Width = 392, Height = 20, Text = day, Tag = "muted", AutoEllipsis = true });
        Controls.Add(new Label { Left = 24, Top = 86, Width = 392, Height = 22, Text = "What will you study?", Tag = "muted" });
        var cb = new ComboBox { Left = 24, Top = 112, Width = 392, DropDownStyle = ComboBoxStyle.DropDown, Font = new Font("Segoe UI", 10) };
        foreach (var t in subjects) cb.Items.Add(t);
        cb.Text = cur.Subject; Controls.Add(cb);
        Controls.Add(new Label { Left = 24, Top = 160, Width = 190, Height = 22, Text = "Start time", Tag = "muted" });
        Controls.Add(new Label { Left = 226, Top = 160, Width = 190, Height = 22, Text = "Length", Tag = "muted" });
        int mins = 18 * 60, hh, mm; var parts = (cur.Time ?? "18:00").Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out hh) && int.TryParse(parts[1], out mm)) mins = hh * 60 + mm;
        var st = new Stepper { Left = 24, Top = 186, Width = 190, Clock = true, Step = 15, Value = mins }; Controls.Add(st);
        var sd = new Stepper { Left = 226, Top = 186, Width = 190, Min = 5, Max = 600, Step = 5, Suffix = "min", Value = Math.Max(5, cur.Minutes) }; Controls.Add(sd);
        var ok = Theme.Btn("Save", 24, h - 50, 100, "a"); ok.Height = 32;
        ok.Click += delegate
        {
            string subj = cb.Text.Trim(); if (subj.Length == 0) { MessageBox.Show(this, "Type or choose what you will study."); return; }
            Result = new Slot { Day = cur.Day, Subject = subj, Minutes = sd.Value, Time = (st.Value / 60).ToString("00") + ":" + (st.Value % 60).ToString("00") };
            DialogResult = DialogResult.OK; Close();
        };
        var no = Theme.Btn("Cancel", 132, h - 50, 100, "c"); no.Height = 32; no.DialogResult = DialogResult.Cancel;
        Controls.Add(ok); Controls.Add(no); AcceptButton = ok; CancelButton = no;
        if (exists) { var del = Theme.Btn("Delete", 240, h - 50, 100, "b"); del.Height = 32; del.Click += delegate { Deleted = true; DialogResult = DialogResult.OK; Close(); }; Controls.Add(del); }
        Theme.Skin(this); Theme.ApplyUserFont(this, true); Theme.FooterFix(this);
    }
}

// ===== Simple read-only report window (weekly summary) =====
class InfoForm : Form
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); try { int v = Theme.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref v, 4); DwmSetWindowAttribute(Handle, 19, ref v, 4); } catch { } }
    public InfoForm(string title, string sub, List<string[]> rows)
    {
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        int h = 90 + rows.Count * 52 + 90; ClientSize = new Size(460, h); BackColor = Theme.Card; Font = new Font("Segoe UI", 9); Text = title;
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 68 });   // footer strip (colour comes from Theme.Skin)
        Controls.Add(new Label { Left = 24, Top = 16, Width = 392, Height = 28, Text = title, Font = new Font("Segoe UI Semibold", 13), AutoEllipsis = true });
        Controls.Add(new Label { Left = 25, Top = 46, Width = 392, Height = 20, Text = sub, Tag = "muted", AutoEllipsis = true });
        var card = new Card { Left = 24, Top = 86, Width = 412, Height = rows.Count * 52 + 10 };
        for (int i = 0; i < rows.Count; i++)
        {
            card.Controls.Add(new Label { Left = 16, Top = 10 + i * 52, Width = 380, Height = 18, Text = rows[i][0], Tag = "muted" });
            card.Controls.Add(new Label { Left = 16, Top = 28 + i * 52, Width = 380, Height = 24, Text = rows[i][1], Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) });
        }
        Controls.Add(card);
        var ok = Theme.Btn("Close", 24, h - 50, 100, "a"); ok.Height = 32; ok.DialogResult = DialogResult.OK; Controls.Add(ok); AcceptButton = ok; CancelButton = ok;
        Theme.Skin(this); Theme.ApplyUserFont(this, true); Theme.FooterFix(this);
    }
}

// ===== Compact task row for the Home tab =====
class TaskRow : Card
{
    public StudyTask T; public int Studied; public Action<TaskRow> OnToggle;
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (new Rectangle(8, 6, 44, 44).Contains(e.Location) && OnToggle != null) OnToggle(this); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var c = new Rectangle(16, 16, 22, 22);
        if (T.Done) { using (var br = new SolidBrush(Theme.Accent)) g.FillEllipse(br, c); using (var pen = new Pen(Color.White, 2.5f)) g.DrawLines(pen, new Point[] { new Point(21, 27), new Point(26, 32), new Point(33, 22) }); }
        else using (var pen = new Pen(Theme.Border, 2f)) g.DrawEllipse(pen, c);
        var fl = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter; int tw = Math.Max(60, Width - 290);
        TextRenderer.DrawText(g, T.Title, Theme.F(9.5f, FontStyle.Bold), new Rectangle(52, 7, tw, 22), T.Done ? Theme.Muted : Theme.Text, fl);
        TextRenderer.DrawText(g, (T.Target > 0 ? Studied + " / " + T.Target : Studied.ToString()) + " min studied" + (T.Done || T.DueText.Length == 0 ? "" : "   |   " + T.DueText), Theme.F(8.5f), new Rectangle(52, 29, tw, 18), (!T.Done && T.DaysLeft < 0) ? Color.FromArgb(230, 90, 90) : Theme.Muted, fl);
        Color cc = T.Done ? Theme.Accent : (T.DaysLeft < 0 ? Color.FromArgb(230, 90, 90) : Theme.Accent3); var chip = new Rectangle(Width - 192, 15, 84, 26);
        using (var gp = Theme.Round(chip, 26)) { using (var br = new SolidBrush(Color.FromArgb(50, cc))) g.FillPath(br, gp); using (var pen = new Pen(cc)) g.DrawPath(pen, gp); }
        TextRenderer.DrawText(g, T.Done ? "Completed" : (T.DaysLeft < 0 ? "Overdue" : "To do"), Theme.F(8.5f), chip, cc, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

// ===== One study task shown as a rounded, bordered card =====
class TaskCard : Card
{
    public StudyTask T; public int Studied; public bool Selected, Flash; public Action<TaskCard> OnSelect, OnToggle;
    static Font fT { get { return Theme.F(10f, FontStyle.Bold); } } static Font fS { get { return Theme.F(8.5f); } }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (new Rectangle(12, 12, 38, 38).Contains(e.Location)) { if (OnToggle != null) OnToggle(this); } else if (OnSelect != null) OnSelect(this);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Flash) using (var gp = Theme.Round(new Rectangle(1, 1, Width - 3, Height - 3), 16)) using (var br = new SolidBrush(Color.FromArgb(45, Theme.Accent))) g.FillPath(br, gp);
        if (Selected) using (var gp = Theme.Round(new Rectangle(1, 1, Width - 3, Height - 3), 16)) using (var pen = new Pen(Theme.Accent, 2f)) g.DrawPath(pen, gp);
        var c = new Rectangle(18, 18, 24, 24);
        if (T.Done) { using (var br = new SolidBrush(Theme.Accent)) g.FillEllipse(br, c); using (var pen = new Pen(Color.White, 2.5f)) g.DrawLines(pen, new Point[] { new Point(24, 30), new Point(29, 35), new Point(37, 24) }); }
        else using (var pen = new Pen(Theme.Border, 2f)) g.DrawEllipse(pen, c);
        int tw = Math.Max(60, Width - 300);
        var fl = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
        TextRenderer.DrawText(g, T.Title, fT, new Rectangle(56, 10, tw, 26), T.Done ? Theme.Muted : Theme.Text, fl);
        TextRenderer.DrawText(g, "Studied " + Studied + " of " + T.Target + " min" + (T.Done ? "  -  completed" : ""), fS, new Rectangle(56, 36, tw, 20), Theme.Muted, fl);
        string dueTxt = T.Done ? "" : T.DueText;
        if (dueTxt.Length > 0) TextRenderer.DrawText(g, dueTxt, fS, new Rectangle(56, 36, tw, 20), T.DaysLeft < 0 ? Color.FromArgb(230, 90, 90) : T.DaysLeft <= 2 ? Theme.Accent3 : Theme.Muted, fl | TextFormatFlags.Right);
        int pct = T.Target > 0 ? Math.Min(100, Studied * 100 / T.Target) : (Studied > 0 ? 100 : 0);
        var tr = new Rectangle(56, 66, tw, 10);
        using (var gp = Theme.Round(tr, 10)) using (var br = new SolidBrush(Theme.Border)) g.FillPath(br, gp);
        if (pct > 0) { var fr = new Rectangle(56, 66, Math.Max(10, tw * pct / 100), 10); using (var gp = Theme.Round(fr, 10)) using (var br = new LinearGradientBrush(fr, Theme.Accent, Theme.Accent2, 0f)) g.FillPath(br, gp); }
        TextRenderer.DrawText(g, pct + "%", fS, new Rectangle(56 + tw + 8, 60, 50, 22), Theme.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
    }
}

class MainForm : Form
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int m, int w, string l);
    const int PAGE_HOME = 0, PAGE_PLAN = 1, PAGE_FOCUS = 2, PAGE_STATS = 3, PAGE_FILES = 4, PAGE_SETTINGS = 5, PAGE_ABOUT = 6, PAGES = 7;
    string dataFile, root = "", userName = ""; int goal = 60, cur = 0, homeFilter = 0; float uiFontSize = 9f; string uiFontFamily = "Segoe UI"; bool loading = true, muted = false, onboardingDone = false, minimizeDuringTimer = true; int scanGeneration = 0;
    List<StudyTask> tasks = new List<StudyTask>(); List<Sess> sessions = new List<Sess>(); List<string[]> index = new List<string[]>();
    Panel banner, content; Panel[] pages = new Panel[PAGES]; Button[] nav = new Button[PAGES]; PictureBox pic; Button bDark, bPal;
    string examName = "", examDate = "", lastSummary = "", lastDaily = ""; bool remDaily, remBreak, remTray, exiting, trayHinted; int remHour = 18, remMinute = 0, remBreakMin = 45; double breakAcc; NotifyIcon tray; Timer remTimer; Dictionary<string, BookProg> books = new Dictionary<string, BookProg>(); string newDue = ""; Button bDue; List<Slot> slots = new List<Slot>();
    bool statsOptIn = true, statsAsked, installSent, statsBusy; Label lblUsers; Timer usersTimer; string uWeek, uMonth, uTotal; int usersTick; DateTime lastPing = DateTime.MinValue; bool updateAvailable, blink, busy, autoMode = true; DateTime stamp = DateTime.MinValue, lastCheck = DateTime.MinValue, latest = DateTime.MinValue;
    Timer dotTimer, hourTimer; Label lblVer, lblUpdInfo, lblUpdStatus; Button bCheck, bInstall; ProgressBar prog; RadioButton rbAuto, rbManual;
    TextBox txtTitle; Stepper numTarget; FlowLayoutPanel taskFlow, homeFlow; Card homeSum; Button[] homeFilters = new Button[3]; StudyTask selTask; Label lblPlanSummary; Label lblDayInfo; ListBox lbDay; DateTime selDay = DateTime.Today; Label lblStatus, lblHello, lblQuote, lblInst; MiniTimer mini; StudyTask miniTask;
    Label lblSummary; RoundBar bar; Panel chart; ListBox lbSubjects; Stepper numGoal; Label lblRoot; TextBox txtSearch; ListView lvFiles;
    // focus timer (Pomodoro)
    Panel ring; Label lblRingTime, lblRingMode, lblFocusStatus; Button bStart, bReset, bSound; Stepper numFocusMin; ComboBox cbSub; CheckBox chkLog;
    int fleft = 25 * 60, ftotal = 25 * 60, focusCount; string fmode = "focus"; bool frunning; Timer fTimer; Button bFocus, bShort, bLong;
    // streak calendar
    Panel heat; DateTime viewMonth; Label lblStreak; Button bPrev, bNext, bThisMonth;
    string[] quotes = { "Small steps every day add up.", "Read ahead, stay ahead.", "Future doctor in the making!", "Consistency beats cramming.", "One more page. You've got this!",
        "You're doing amazing, keep going!", "Glow up your grades, one page at a time.", "Rest is part of studying too." };

    static string Today() { return DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
    static string Key(DateTime d) { return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
    static string Clean(string s) { return (s ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " "); }

    public MainForm()
    {
        Text = "Study Tracker"; Width = 980; Height = 720; MinimumSize = new Size(880, 640); Font = new Font("Segoe UI", 9); StartPosition = FormStartPosition.CenterScreen;
        dataFile = Path.Combine(App.DataDir, "study_data.txt"); bool dark = true; int pal = 0; Load_(ref dark, ref pal); if (!statsAsked) statsOptIn = true; Theme.Apply(dark, pal);
        viewMonth = DateTime.Today;
        try { Icon = Icon.FromHandle(Theme.Logo(64).GetHicon()); } catch { }
        content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14) }; Controls.Add(content); BuildFooter();
        for (int i = 0; i < PAGES; i++) { pages[i] = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg }; content.Controls.Add(pages[i]); }
        var navBar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Bg };
        string[] names = { "Home", "Study Plan", "Focus Timer", "Stats", "Find Files", "Settings", "About" }; string[] navIcons = { "home", "list", "timer", "chart", "folder", "gear", "info" };
        for (int i = 0; i < PAGES; i++) { int k = i; nav[i] = Theme.Btn(names[i], 8 + i * 118, 8, 114, "nav"); ((PillButton)nav[i]).Icon = navIcons[i]; nav[i].Height = 34; nav[i].Click += delegate { ShowPage(k); }; navBar.Controls.Add(nav[i]); }
        ((PillButton)nav[PAGE_SETTINGS]).Overlay = delegate(Graphics g) { NavDot(null, new PaintEventArgs(g, Rectangle.Empty)); }; Controls.Add(navBar);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, Tag = "line" });
        BuildBanner(); BuildHome(pages[PAGE_HOME]); BuildPlan(pages[PAGE_PLAN]); BuildFocus(pages[PAGE_FOCUS]); BuildStats(pages[PAGE_STATS]); BuildFiles(pages[PAGE_FILES]); BuildSettings(pages[PAGE_SETTINGS]); BuildAbout(pages[PAGE_ABOUT]);
        if (stamp == DateTime.MinValue) stamp = DateTime.UtcNow; ApplyFontPrefs(); loading = false; Recolor(); RefreshTasks(); RefreshStats(); RefreshStreak(); Scan(); Greet(); ShowPage(0); StartTimers();
        Shown += delegate
        {
            if (userName == "") { var n = Prompt("Welcome!", "Hi! What should we call you?", ""); userName = string.IsNullOrWhiteSpace(n) ? "Student" : n.Trim(); Save(); Greet(); }
            if (!onboardingDone)
            {
                var answer = MessageBox.Show("STEP 1 OF 3 — YOUR NOTES\n\nDo you have one main folder that contains your study notes, PDFs, books, and lecture files?\n\nChoose Yes to select it now, or No to set it up later in Find Files.", "Welcome to Study Tracker", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
                if (answer == DialogResult.Cancel) { onboardingDone = true; Save(); }
                else
                {
                    if (answer == DialogResult.Yes)
                    {
                        using (var f = new FolderBrowserDialog())
                        {
                            f.Description = "Choose your main study-notes folder. Study Tracker will index file names for searching; it will not move or change your files.";
                            f.ShowNewFolderButton = false;
                            if (f.ShowDialog(this) == DialogResult.OK) { root = f.SelectedPath; Save(); Scan(); }
                        }
                    }
                    MessageBox.Show("STEP 2 OF 3 — PLAN YOUR WORK\n\nOpen Study Plan to add subjects or tasks and set study targets. Use the task cards to start a reading timer or log study time.", "Quick tutorial", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    MessageBox.Show("STEP 3 OF 3 — TRACK YOUR PROGRESS\n\nHome separates All tasks, To do, and Completed. Focus Timer runs Pomodoro sessions. Stats shows your progress. Find Files searches your chosen notes folder. Settings lets you change your name, theme, fonts, updates, and timer behavior.\n\nYou can change your notes folder later in Find Files.", "Quick tutorial", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    onboardingDone = true; Save();
                }
            }
            AutoCheck();
            RefreshUsers();
            MaybeWeekly();
        };
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); DarkBar(); }
    static int Bgr(Color c) { return c.R | (c.G << 8) | (c.B << 16); }
    void DarkBar()
    {
        try { int v = Theme.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref v, 4); DwmSetWindowAttribute(Handle, 19, ref v, 4); } catch { }
        try { int cap = Bgr(Theme.Base), txt = Bgr(Color.White), bor = Bgr(Theme.Border); DwmSetWindowAttribute(Handle, 35, ref cap, 4); DwmSetWindowAttribute(Handle, 36, ref txt, 4); DwmSetWindowAttribute(Handle, 34, ref bor, 4); } catch { }   // Windows 11: caption colour = the banner colour
    }

    void Recolor()
    {
        BackColor = Theme.Bg; Theme.Skin(this); pic.Image = Theme.Logo(54);
        bDark.Text = Theme.Dark ? "Light mode" : "Dark mode"; bPal.Text = "Colors: " + Theme.PalNames[Theme.Pal];
        banner.Invalidate(); if (chart != null) chart.Invalidate(); if (ring != null) ring.Invalidate(); if (heat != null) heat.Invalidate();
        RefreshTasks(); UpdateUI(); ShowPage(cur); DarkBar(); Save();
    }
    void ShowPage(int i)
    {
        cur = i;
        for (int j = 0; j < PAGES; j++) { pages[j].Visible = j == i; nav[j].BackColor = j == i ? Theme.Accent : Theme.Card; nav[j].ForeColor = j == i ? Color.White : Theme.Text; }
        if (i == PAGE_HOME) RefreshHome(); if (i == PAGE_STATS) { RefreshStats(); RefreshStreak(); } if (i == PAGE_FILES) txtSearch.Focus();
    }

    // ---------- banner ----------
    void BuildBanner()
    {
        banner = new Panel { Dock = DockStyle.Top, Height = 84, Tag = "banner" };
        banner.Paint += (s, e) => { using (var br = new LinearGradientBrush(banner.ClientRectangle, Theme.Base, Theme.Accent, 0f)) e.Graphics.FillRectangle(br, banner.ClientRectangle); };
        pic = new PictureBox { Left = 16, Top = 14, Width = 54, Height = 54, BackColor = Color.Transparent };
        lblHello = new Label { Left = 82, Top = 10, Width = 520, Height = 34, ForeColor = Color.White, Tag = "white", Font = new Font("Segoe UI", 14, FontStyle.Bold) };
        lblQuote = new Label { Left = 84, Top = 48, Width = 520, Height = 24, ForeColor = Color.FromArgb(215, 245, 240), Tag = "white", Font = new Font("Segoe UI", 9, FontStyle.Italic) };
        var bName = Theme.Btn("Edit name", 0, 10, 110, "c"); var b2 = Theme.Btn("Dark mode", 0, 46, 110, "b"); bDark = b2; bPal = Theme.Btn("Colors", 0, 46, 150, "a");
        foreach (var b in new Button[] { bName, bDark, bPal }) { b.Anchor = AnchorStyles.Top | AnchorStyles.Right; b.Height = 28; }
        bName.Left = ClientSize.Width - 130; bDark.Left = ClientSize.Width - 130; bDark.Top = 46; bPal.Left = ClientSize.Width - 290; bPal.Top = 46; bName.Top = 10;
        bName.Click += delegate { var n = Prompt("Your name", "What should I call you?", userName); if (!string.IsNullOrWhiteSpace(n)) { userName = n.Trim(); Save(); Greet(); } };
        bDark.Click += delegate { Theme.Apply(!Theme.Dark, Theme.Pal); Recolor(); };
        bPal.Click += delegate { Theme.Apply(Theme.Dark, Theme.Pal + 1); Recolor(); };
        banner.Controls.AddRange(new Control[] { pic, lblHello, lblQuote, bName, bDark, bPal }); Controls.Add(banner);
    }
    void Greet()
    {
        int h = DateTime.Now.Hour; string g = h < 12 ? "Good morning" : h < 17 ? "Good afternoon" : "Good evening";
        lblHello.Text = g + ", " + (userName == "" ? "friend" : userName) + "!"; lblQuote.Text = quotes[DateTime.Today.DayOfYear % quotes.Length];
    }

    // ---------- data ----------
    void Load_(ref bool dark, ref int pal)
    {
        string old = Path.Combine(App.ExeDir, "study_data.txt");
        try { if (!File.Exists(dataFile) && File.Exists(old)) { Directory.CreateDirectory(App.DataDir); File.Copy(old, dataFile); } } catch { }
        if (!File.Exists(dataFile)) return;
        foreach (var line in File.ReadAllLines(dataFile))
        {
            var p = line.Split('\t'); int a, b; long tk; float fs;
            if (p[0] == "R" && p.Length > 1) root = p[1];
            else if (p[0] == "N" && p.Length > 1) userName = p[1];
            else if (p[0] == "M" && p.Length > 1) muted = p[1] == "1";
            else if (p[0] == "D" && p.Length > 1) dark = p[1] == "1";
            else if (p[0] == "A" && p.Length > 1 && int.TryParse(p[1], out a)) pal = a;
            else if (p[0] == "G" && p.Length > 1 && int.TryParse(p[1], out a)) goal = a;
            else if (p[0] == "F" && p.Length > 1 && int.TryParse(p[1], out a)) focusCount = a;
            else if (p[0] == "U" && p.Length > 1 && long.TryParse(p[1], out tk) && tk >= 0) stamp = new DateTime(tk, DateTimeKind.Utc);
            else if (p[0] == "K" && p.Length > 1 && long.TryParse(p[1], out tk) && tk >= 0) lastCheck = new DateTime(tk, DateTimeKind.Utc);
            else if (p[0] == "L" && p.Length > 1 && long.TryParse(p[1], out tk) && tk >= 0) latest = new DateTime(tk, DateTimeKind.Utc);
            else if (p[0] == "Z" && p.Length > 1) autoMode = p[1] == "1";
            else if (p[0] == "Q" && p.Length > 1 && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out fs)) uiFontSize = Math.Max(8f, Math.Min(16f, fs));
            else if (p[0] == "V" && p.Length > 1 && new string[] { "Segoe UI", "Arial", "Calibri", "Consolas" }.Contains(p[1])) uiFontFamily = p[1];
            else if (p[0] == "W" && p.Length > 1) onboardingDone = p[1] == "1";
            else if (p[0] == "X" && p.Length > 1) minimizeDuringTimer = p[1] == "1";
            else if (p[0] == "Y" && p.Length > 1) statsOptIn = p[1] == "1";
            else if (p[0] == "H" && p.Length > 1) statsAsked = p[1] == "1";
            else if (p[0] == "O" && p.Length > 1) installSent = p[1] == "1";
            else if (p[0] == "J" && p.Length > 1 && long.TryParse(p[1], out tk) && tk >= 0) lastPing = new DateTime(tk, DateTimeKind.Utc);
            else if (p[0] == "E" && p.Length > 2) { examName = p[1]; examDate = p[2]; }
            else if (p[0] == "I" && p.Length > 1) lastSummary = p[1];
            else if (p[0] == "P" && p.Length > 4 && int.TryParse(p[1], out a) && int.TryParse(p[3], out b)) slots.Add(new Slot { Day = a, Time = p[2], Minutes = b, Subject = p[4] });
            else if (p[0] == "B" && p.Length > 4 && int.TryParse(p[2], out a) && int.TryParse(p[3], out b) && long.TryParse(p[4], out tk)) books[p[1]] = new BookProg { FilePath = p[1], Page = a, Total = b, Opened = tk };
            else if (p[0] == "C" && p.Length > 7)
            {
                remDaily = p[1] == "1"; if (int.TryParse(p[2], out a)) remHour = a; if (int.TryParse(p[3], out a)) remMinute = a;
                remBreak = p[4] == "1"; if (int.TryParse(p[5], out a)) remBreakMin = a; remTray = p[6] == "1"; lastDaily = p[7];
            }
            else if (p[0] == "T" && p.Length > 3 && int.TryParse(p[2], out a)) tasks.Add(new StudyTask { Title = p[1], Target = a, Done = p[3] == "1", Due = p.Length > 4 ? p[4] : "" });
            else if (p[0] == "S" && p.Length > 4 && int.TryParse(p[3], out a) && int.TryParse(p[4], out b)) sessions.Add(new Sess { Date = p[1], Subject = p[2], Minutes = a, Pages = b });
        }
    }
    void Save()
    {
        if (loading) return;
        var l = new List<string> { "R\t" + root, "N\t" + Clean(userName), "M\t" + (muted ? "1" : "0"), "D\t" + (Theme.Dark ? "1" : "0"), "A\t" + Theme.Pal, "G\t" + goal, "F\t" + focusCount, "U\t" + stamp.Ticks, "K\t" + lastCheck.Ticks, "L\t" + latest.Ticks, "Z\t" + (autoMode ? "1" : "0"), "Q\t" + uiFontSize.ToString(CultureInfo.InvariantCulture), "V\t" + uiFontFamily, "W\t" + (onboardingDone ? "1" : "0"), "X\t" + (minimizeDuringTimer ? "1" : "0") };
        l.Add("Y\t" + (statsOptIn ? "1" : "0")); l.Add("H\t" + (statsAsked ? "1" : "0")); l.Add("O\t" + (installSent ? "1" : "0")); l.Add("J\t" + lastPing.Ticks);
        l.Add("E\t" + Clean(examName) + "\t" + examDate); l.Add("I\t" + lastSummary);
        foreach (var sl in slots) l.Add("P\t" + sl.Day + "\t" + sl.Time + "\t" + sl.Minutes + "\t" + Clean(sl.Subject));
        l.Add("C\t" + (remDaily ? "1" : "0") + "\t" + remHour + "\t" + remMinute + "\t" + (remBreak ? "1" : "0") + "\t" + remBreakMin + "\t" + (remTray ? "1" : "0") + "\t" + lastDaily);
        foreach (var bk in books.Values) l.Add("B\t" + bk.FilePath + "\t" + bk.Page + "\t" + bk.Total + "\t" + bk.Opened);
        foreach (var t in tasks) l.Add("T\t" + Clean(t.Title) + "\t" + t.Target + "\t" + (t.Done ? "1" : "0") + "\t" + t.Due);
        foreach (var s in sessions) l.Add("S\t" + s.Date + "\t" + Clean(s.Subject) + "\t" + s.Minutes + "\t" + s.Pages);
        try { Directory.CreateDirectory(App.DataDir); File.WriteAllLines(dataFile, l.ToArray()); } catch (Exception e) { MessageBox.Show("Could not save: " + e.Message); }
    }
    int Studied(string title) { return sessions.Where(s => s.Subject == title).Sum(s => s.Minutes); }

    // ---------- home: all tasks / to do / completed ----------
    int HomeW() { return Math.Max(320, homeFlow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6); }
    void BuildHome(Control p)
    {
        homeFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        homeFlow.Resize += delegate { int w = HomeW(); foreach (Control c in homeFlow.Controls) c.Width = w; };
        p.Controls.Add(homeFlow);
        var row = new Panel { Dock = DockStyle.Top, Height = 48 };
        string[] n = { "All tasks", "To do", "Completed" };
        for (int i = 0; i < 3; i++) { int k = i; homeFilters[i] = Theme.Btn(n[i], i * 142, 6, 134, "nav"); homeFilters[i].Height = 34; homeFilters[i].Click += delegate { homeFilter = k; RefreshHome(); }; row.Controls.Add(homeFilters[i]); }
        var add = Theme.Btn("+ New task", 436, 6, 120, "b"); add.Height = 34; ((PillButton)add).Icon = "plus"; add.Text = "New task"; add.Click += delegate { ShowPage(PAGE_PLAN); txtTitle.Focus(); };
        var exam = Theme.Btn("Exam date", 564, 6, 110, "c"); exam.Height = 34; ((PillButton)exam).Icon = "calendar";
        exam.Click += delegate
        {
            string nm = Prompt("Exam countdown", "What is your exam called? (e.g. Anatomy final)", examName); if (nm == null) return;
            string d = AskDate("Exam date", string.IsNullOrWhiteSpace(nm) ? "Pick the day" : nm.Trim(), examDate); if (d == null) return;
            examName = nm.Trim(); examDate = d; Save(); homeSum.Invalidate();
        };
        row.Controls.Add(add); row.Controls.Add(exam); p.Controls.Add(row);
        p.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10 });
        homeSum = new Card { Dock = DockStyle.Top, Height = 96 }; homeSum.Paint += DrawHomeSum; p.Controls.Add(homeSum);
    }
    void DrawHomeSum(object sender, PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        int all = tasks.Count, done = tasks.Count(x => x.Done), pct = all == 0 ? 0 : done * 100 / all;
        TextRenderer.DrawText(g, all == 0 ? "No tasks yet" : done + " of " + all + " tasks completed", Theme.F(11.5f, FontStyle.Bold), new Point(18, 12), Theme.Text);
        int today = sessions.Where(x => x.Date == Today()).Sum(x => x.Minutes);
        TextRenderer.DrawText(g, "Studied today: " + today + " min", Theme.F(8.5f), new Rectangle(homeSum.Width - 240, 14, 224, 20), Theme.Muted, TextFormatFlags.Right);
        string ex = ExamText(); if (ex.Length > 0) TextRenderer.DrawText(g, ex, Theme.F(9.5f, FontStyle.Bold), new Point(18, 38), Theme.Accent2);
        var tr = new Rectangle(18, 68, Math.Max(40, homeSum.Width - 36 - 52), 12);
        using (var gp = Theme.Round(tr, 12)) using (var br = new SolidBrush(Theme.Border)) g.FillPath(br, gp);
        if (pct > 0) { var fr = new Rectangle(tr.X, tr.Y, Math.Max(12, tr.Width * pct / 100), 12); using (var gp = Theme.Round(fr, 12)) using (var br = new LinearGradientBrush(fr, Theme.Accent, Theme.Accent2, 0f)) g.FillPath(br, gp); }
        TextRenderer.DrawText(g, pct + "%", Theme.F(8.5f), new Rectangle(tr.Right + 6, 64, 44, 20), Theme.Muted);
    }
    string AskDate(string title, string sub, string cur)
    {
        DateTime d; DateTime? c = DateTime.TryParseExact(cur ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? (DateTime?)d : null;
        using (var f = new AskForm(title, sub, c, true)) return f.ShowDialog(this) == DialogResult.OK ? f.DateResult : null;
    }
    string DueButtonText(string key) { DateTime d; return DateTime.TryParseExact(key ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? "Due: " + d.ToString("ddd d MMM", CultureInfo.CurrentCulture) : "Due: none"; }
    ContextMenuStrip DueMenu(StudyTask t)
    {
        var m = new ContextMenuStrip();
        m.Items.Add("Set due date...", null, delegate { string d = AskDate("Due date", t.Title, t.Due); if (d != null) { t.Due = d; Save(); RefreshTasks(); RefreshHome(); } });
        m.Items.Add("Clear due date", null, delegate { t.Due = ""; Save(); RefreshTasks(); RefreshHome(); });
        return m;
    }
    string ExamText()
    {
        DateTime d; if (!DateTime.TryParseExact(examDate ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return "";
        int n = (int)(d.Date - DateTime.Today).TotalDays; string nm = string.IsNullOrEmpty(examName) ? "Exam" : examName;
        return n < 0 ? nm + " was " + (-n) + " day(s) ago" : n == 0 ? nm + " is today. Good luck!" : nm + ": " + n + " day(s) to go";
    }
    void RefreshHome()
    {
        if (homeFlow == null) return;
        int all = tasks.Count, done = tasks.Count(x => x.Done);
        string[] n = { "All tasks (" + all + ")", "To do (" + (all - done) + ")", "Completed (" + done + ")" };
        for (int i = 0; i < 3; i++) { homeFilters[i].Text = n[i]; homeFilters[i].BackColor = i == homeFilter ? Theme.Accent : Theme.Card; homeFilters[i].ForeColor = i == homeFilter ? Color.White : Theme.Text; }
        homeFlow.SuspendLayout();
        foreach (Control c in homeFlow.Controls.Cast<Control>().ToList()) { homeFlow.Controls.Remove(c); c.Dispose(); }
        var list = tasks.Where(x => homeFilter == 0 || (homeFilter == 1 && !x.Done) || (homeFilter == 2 && x.Done)).OrderBy(x => x.Done).ThenBy(x => x.DaysLeft).ToList();
        if (list.Count == 0) homeFlow.Controls.Add(new Label { Width = HomeW(), Height = 80, Tag = "muted", TextAlign = ContentAlignment.MiddleCenter,
            Text = tasks.Count == 0 ? "No tasks yet. Press + New task to add your first one." : homeFilter == 1 ? "Nothing left to do. Nice work!" : "Nothing completed yet. Click a task's circle to move it here." });
        int w = HomeW();
        foreach (var t in list)
        {
            var r = new TaskRow { T = t, Studied = Studied(t.Title), Width = w, Height = 56, Margin = new Padding(0, 0, 0, 8) };
            var open = Theme.Btn("Open", w - 96, 11, 80, "a"); open.Height = 32; open.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            open.Click += delegate { ShowTask(t); };
            r.Controls.Add(open); r.ContextMenuStrip = DueMenu(t);
            r.OnToggle = k => { k.T.Done = !k.T.Done; Save(); RefreshTasks(); };
            homeFlow.Controls.Add(r);
        }
        homeFlow.ResumeLayout(); Theme.Skin(homeFlow); Theme.ApplyUserFont(homeFlow, false); if (homeSum != null) homeSum.Invalidate();
    }

    // ---------- plan (task cards) ----------
    int CardW() { return Math.Max(320, taskFlow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6); }
    void BuildPlan(Control p)
    {
        taskFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        taskFlow.Resize += delegate { int w = CardW(); foreach (Control c in taskFlow.Controls) c.Width = w; };
        p.Controls.Add(taskFlow);
        lblStatus = new Label { Dock = DockStyle.Bottom, Height = 30, Tag = "muted", TextAlign = ContentAlignment.MiddleLeft, Text = "Click a card to select it (click empty space to deselect), or press Start for a pop-up reading timer. Right-click a card to set a due date." };
        p.Controls.Add(lblStatus);
        lblPlanSummary = new Label { Dock = DockStyle.Top, Height = 34, Tag = "muted", TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        p.Controls.Add(lblPlanSummary);
        var holder = new Panel { Dock = DockStyle.Top, Height = 68, Padding = new Padding(0, 0, 0, 12) };
        var bar2 = new Card { Dock = DockStyle.Fill };
        txtTitle = new TextBox { Left = 20, Top = 15, Width = 300, Font = new Font("Segoe UI", 10.5f), Tag = "flat" };
        numTarget = new Stepper { Top = 10 };
        bDue = Theme.Btn("Due: none", 0, 11, 130, "c"); bDue.Height = 34; ((PillButton)bDue).Icon = "calendar";
                var bAdd = Theme.Btn("Add task", 0, 10, 110, "a"); bAdd.Height = 36; ((PillButton)bAdd).Icon = "plus";
        bar2.Controls.AddRange(new Control[] { txtTitle, numTarget, bDue, bAdd });
        Action lay = delegate { int w = bar2.Width; bAdd.Left = w - 126; numTarget.Left = w - 262; bDue.Left = w - 400; txtTitle.Width = Math.Max(100, w - 400 - 34); };
        bar2.Resize += delegate { lay(); }; lay();
        holder.Controls.Add(bar2); p.Controls.Add(holder);
        foreach (Control c0 in new Control[] { taskFlow, p, holder, bar2, lblPlanSummary, lblStatus }) c0.MouseDown += delegate { ClearSel(); };
        txtTitle.HandleCreated += delegate { SendMessage(txtTitle.Handle, 0x1501, 1, "What are you studying today?"); };
        bAdd.Click += delegate { AddTask(); };
        bDue.Click += delegate { string d = AskDate("Due date", "When is this task due?", newDue); if (d != null) { newDue = d; bDue.Text = DueButtonText(d); } };
        txtTitle.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { AddTask(); e.SuppressKeyPress = true; } };
    }
    void ShowTask(StudyTask t)
    {
        selTask = t; ShowPage(PAGE_PLAN); RefreshTasks();
        BeginInvoke(new Action(delegate   // after the list has been laid out
        {
            foreach (Control o in taskFlow.Controls)
            {
                var tc = o as TaskCard; if (tc == null || tc.T != t) continue;
                int absTop = tc.Top - taskFlow.AutoScrollPosition.Y;                 // card position inside the whole list
                taskFlow.AutoScrollPosition = new Point(0, Math.Max(0, absTop - 8));  // scroll so it sits at the top
                tc.Flash = true; tc.Invalidate();
                var tm = new Timer { Interval = 1500 };
                tm.Tick += delegate { tm.Stop(); tm.Dispose(); tc.Flash = false; if (!tc.IsDisposed) tc.Invalidate(); };
                tm.Start(); break;
            }
        }));
    }
    void ClearSel()
    {
        selTask = null;
        foreach (Control o in taskFlow.Controls) { var tc = o as TaskCard; if (tc != null && tc.Selected) { tc.Selected = false; tc.Invalidate(); } }
    }
    TaskCard MakeCard(StudyTask t)
    {
        int w = CardW();
        var c = new TaskCard { T = t, Studied = Studied(t.Title), Width = w, Height = 88, Margin = new Padding(0, 0, 0, 10), Selected = t == selTask };
        var bs = Theme.Btn("Start", w - 226, 14, 90, "a"); var bl = Theme.Btn("Log", w - 130, 14, 74, "c"); var bx = Theme.Btn("X", w - 48, 14, 34, "b");
        bs.Height = bl.Height = bx.Height = 30; bs.Anchor = bl.Anchor = bx.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        c.Controls.AddRange(new Control[] { bs, bl, bx }); c.ContextMenuStrip = DueMenu(t);
        bs.Click += delegate { selTask = t; StartReading(); };
        bl.Click += delegate { selTask = t; LogTime(t); };
        bx.Click += delegate { if (MessageBox.Show("Delete this task? Logged sessions are kept.", "Delete", MessageBoxButtons.YesNo) == DialogResult.Yes) { tasks.Remove(t); if (selTask == t) selTask = null; Save(); RefreshTasks(); } };
        c.OnSelect = k => { selTask = k.T; foreach (Control o in taskFlow.Controls) { var tc = o as TaskCard; if (tc != null) { tc.Selected = tc.T == selTask; tc.Invalidate(); } } };
        c.OnToggle = k => { k.T.Done = !k.T.Done; Save(); RefreshTasks(); };
        return c;
    }
    void LogTime(StudyTask t)
    {
        int mi, pgs; if (!AskMinutes("Log study time", t.Title, "How long did you study?", 30, true, out mi, out pgs)) return;
        AddSession(t, mi, pgs);
    }
    void StartReading()
    {
        if (mini != null && !mini.IsDisposed) { mini.Show(); mini.TopMost = true; return; }
        var t = Selected(); if (t == null) return;
        int mins, pgs0; if (!AskMinutes("Reading timer", t.Title, "How many minutes will you read?", 25, false, out mins, out pgs0)) return;
        miniTask = t; mini = new MiniTimer(t.Title, mins, muted);
        mini.Finished += (done, completed) =>
        {
            muted = mini.Muted; if (minimizeDuringTimer && WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Save(); if (done >= 1) AddSession(miniTask, done, 0);
            lblStatus.Text = completed ? "Great job, " + userName + "! Logged " + done + " min on \"" + miniTask.Title + "\"." : (done >= 1 ? "Logged " + done + " min." : "Session ended (under 1 minute, not logged).");
            mini = null;
        };
        mini.Show(); if (minimizeDuringTimer) WindowState = FormWindowState.Minimized; lblStatus.Text = "Timer running on the side of your screen. Focus time!";
    }
    StudyTask Selected() { if (selTask == null || !tasks.Contains(selTask)) { MessageBox.Show("Select a task first - click one of the cards."); return null; } return selTask; }
    void AddTask()
    {
        var t = txtTitle.Text.Trim(); if (t == "") return;
        tasks.Add(new StudyTask { Title = t, Target = (int)numTarget.Value, Due = newDue }); newDue = ""; bDue.Text = "Due: none"; txtTitle.Text = ""; Save(); RefreshTasks(); FillCombo();
    }
    void AddSession(StudyTask t, int mins, int pages)
    {
        sessions.Add(new Sess { Date = Today(), Subject = t.Title, Minutes = mins, Pages = pages });
        if (t.Target > 0 && Studied(t.Title) >= t.Target) t.Done = true;
        Save(); RefreshTasks(); RefreshStats(); RefreshStreak();
    }
    void RefreshTasks()
    {
        if (taskFlow == null) return;
        taskFlow.SuspendLayout();
        foreach (Control c in taskFlow.Controls.Cast<Control>().ToList()) { taskFlow.Controls.Remove(c); c.Dispose(); }
        if (tasks.Count == 0) { var el = new Label { Width = CardW(), Height = 90, Tag = "muted", TextAlign = ContentAlignment.MiddleCenter, Text = "Nothing planned yet.\nType what you're studying above and press Add task." }; el.MouseDown += delegate { ClearSel(); }; taskFlow.Controls.Add(el); }
        foreach (var t in tasks) taskFlow.Controls.Add(MakeCard(t));
        taskFlow.ResumeLayout(); Theme.Skin(taskFlow); Theme.ApplyUserFont(taskFlow, false);
        lblPlanSummary.Text = tasks.Count + " task(s)   |   " + tasks.Count(x => x.Done) + " done   |   " + sessions.Where(x => x.Date == Today()).Sum(x => x.Minutes) + " min studied today";
        FillCombo(); RefreshHome();
    }

    // ---------- focus timer (Pomodoro) ----------
    void BuildFocus(Control p)
    {
        ring = new Card { Width = 250, Height = 250 }; ring.Paint += DrawRing;
        lblRingMode = new Label { Left = 0, Top = 78, Width = ring.Width, Height = 22, TextAlign = ContentAlignment.MiddleCenter, Tag = "sub", Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), BackColor = Color.Transparent };
        lblRingTime = new Label { Left = 0, Top = 104, Width = ring.Width, Height = 50, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Text, Font = new Font("Consolas", 26, FontStyle.Bold), BackColor = Color.Transparent };
        lblRingMode.Parent = ring; lblRingTime.Parent = ring;
        bFocus = Theme.Btn("Deep Focus  25", 0, 6, 150, "a"); bShort = Theme.Btn("Short Break  5", 0, 6, 150, "c"); bLong = Theme.Btn("Long Break  15", 0, 6, 150, "b");
        bFocus.Height = bShort.Height = bLong.Height = 34;
        bStart = Theme.Btn("Start", 0, 0, 150, "a"); bStart.Height = 40; bStart.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        bReset = Theme.Btn("Reset", 0, 0, 100, "b"); bReset.Height = 40;
        bSound = Theme.Btn(muted ? "Sound: Off" : "Sound: On", 0, 0, 110, "c"); bSound.Height = 40;
        lblFocusStatus = new Label { Width = 700, Height = 40, TextAlign = ContentAlignment.MiddleCenter, Tag = "muted", Font = new Font("Segoe UI", 9, FontStyle.Italic), Text = "Completed focus blocks: 0" };
        chkLog = new SwitchBox { Width = 220, Text = "Auto-log focus blocks to:", Checked = true };
        cbSub = new ComboBox { Width = 270, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9) };
        var lblCustom = new Label { Width = 70, Text = "Custom:", Tag = "muted" };
        numFocusMin = new Stepper { Width = 140, Min = 1, Max = 180, Step = 5, Suffix = "min", Value = 25 };
        var bSet = Theme.Btn("Set", 0, 0, 56, "c"); bSet.Height = 28;
        p.Controls.AddRange(new Control[] { bFocus, bShort, bLong, ring, bStart, bReset, bSound, lblFocusStatus, chkLog, cbSub, lblCustom, numFocusMin, bSet });
        Action lay = delegate
        {
            int cx = p.Width / 2;
            bFocus.Left = cx - 233; bShort.Left = cx - 75; bLong.Left = cx + 83;
            ring.Left = cx - 125; ring.Top = 54;
            bStart.SetBounds(cx - 188, 318, 150, 40); bReset.SetBounds(cx - 30, 318, 100, 40); bSound.SetBounds(cx + 78, 318, 110, 40);
            lblFocusStatus.SetBounds(cx - 350, 366, 700, 40);
            chkLog.SetBounds(cx - 250, 418, 220, 26); cbSub.SetBounds(cx - 24, 416, 270, 28);
            lblCustom.SetBounds(cx - 130, 458, 70, 24); numFocusMin.SetBounds(cx - 56, 450, 140, 36); bSet.SetBounds(cx + 92, 452, 56, 32);
        };
        p.Resize += delegate { lay(); }; lay();
        bFocus.Click += delegate { SetMode("focus", 25); };
        bShort.Click += delegate { SetMode("short", 5); };
        bLong.Click += delegate { SetMode("long", 15); };
        bSet.Click += delegate { SetMode("focus", (int)numFocusMin.Value); };
        numFocusMin.Changed += delegate { if (!frunning && fmode == "focus") SetMode("focus", (int)numFocusMin.Value); };
        bStart.Click += delegate { ToggleRun(); };
        bReset.Click += delegate { frunning = false; fleft = ftotal; bStart.Text = "Start"; UpdateRing(); };
        bSound.Click += delegate { muted = !muted; bSound.Text = muted ? "Sound: Off" : "Sound: On"; Save(); };
        fTimer = new Timer { Interval = 1000 };
        fTimer.Tick += delegate
        {
            if (!frunning) return;
            fleft--; UpdateRing(); if (fleft > 0 && fleft <= 5 && !muted) System.Media.SystemSounds.Beep.Play();
            if (fleft <= 0) FocusDone();
        };
        fTimer.Start();
        SetMode("focus", 25);
        FillCombo();
    }
    void FillCombo()
    {
        if (cbSub == null) return;
        string keep = cbSub.Text; cbSub.Items.Clear();
        foreach (var t in tasks) cbSub.Items.Add(t.Title);
        if (tasks.Count == 0) cbSub.Items.Add("Focus session");
        int idx = cbSub.Items.IndexOf(keep); cbSub.SelectedIndex = idx >= 0 ? idx : 0;
    }
    void SetMode(string m, int mins)
    {
        fmode = m; frunning = false; bStart.Text = "Start";
        ftotal = fleft = mins * 60; if (m == "focus") numFocusMin.Value = Math.Max(numFocusMin.Min, Math.Min(numFocusMin.Max, mins));
        bFocus.BackColor = m == "focus" ? Theme.Accent : Theme.Card; bFocus.ForeColor = m == "focus" ? Color.White : Theme.Text;
        bShort.BackColor = m == "short" ? Theme.Accent3 : Theme.Card; bShort.ForeColor = m == "short" ? Theme.Base : Theme.Text;
        bLong.BackColor = m == "long" ? Theme.Accent2 : Theme.Card; bLong.ForeColor = m == "long" ? Color.White : Theme.Text;
        lblRingMode.Text = m == "focus" ? "DEEP FOCUS" : m == "short" ? "SHORT BREAK" : "LONG BREAK";
        lblRingTime.ForeColor = Theme.Text;
        UpdateRing();
    }
    void ToggleRun() { frunning = !frunning; bStart.Text = frunning ? "Pause" : "Resume"; }
    void UpdateRing()
    {
        lblRingTime.Text = (fleft / 60).ToString("00") + ":" + (fleft % 60).ToString("00");
        if (ring != null) ring.Invalidate();
    }
    void FocusDone()
    {
        frunning = false; bStart.Text = "Start"; if (!muted) Sound.Chime();
        if (fmode == "focus")
        {
            focusCount++; lblFocusStatus.Text = "Completed focus blocks: " + focusCount + "  —  " + quotes[(focusCount + DateTime.Today.DayOfYear) % quotes.Length];
            if (chkLog != null && chkLog.Checked)
            {
                string subject = cbSub.SelectedItem as string; if (string.IsNullOrEmpty(subject)) subject = "Focus session";
                var t = tasks.FirstOrDefault(x => x.Title == subject);
                if (t != null) AddSession(t, ftotal / 60, 0);
                else { sessions.Add(new Sess { Date = Today(), Subject = subject, Minutes = ftotal / 60, Pages = 0 }); Save(); RefreshStats(); RefreshStreak(); }
            }
            SetMode("short", 5);
        }
        else SetMode("focus", (int)numFocusMin.Value);
    }
    void DrawRing(object sender, PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        int pad = 26; var r = new Rectangle(pad, pad, ring.Width - pad * 2, ring.Height - pad * 2);
        using (var pen = new Pen(Theme.Dark ? ControlPaint.Dark(Theme.Card, 0.06f) : Color.FromArgb(235, 245, 244), 12) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pen, r, 0, 360);
        float pct = ftotal > 0 ? 1f - fleft / (float)ftotal : 0f;
        Color c = fmode == "focus" ? Theme.Accent : fmode == "short" ? Theme.Accent3 : Theme.Accent2;
        using (var pen = new Pen(Color.FromArgb(60, c), 20) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pen, Rectangle.Inflate(r, -5, -5), -90, 360f * pct);
        using (var pen = new Pen(c, 12) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pen, r, -90, 360f * pct);
    }

    // ---------- stats ----------
    void BuildStats(Control p)
    {
        var right = new Panel { Dock = DockStyle.Fill }; var left = new Panel { Dock = DockStyle.Left, Width = 420, Padding = new Padding(0, 0, 14, 0) };
        p.Controls.Add(right); p.Controls.Add(left);
        lbSubjects = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9) }; left.Controls.Add(lbSubjects);
        var tt = new TimetableCard { Dock = DockStyle.Top, Height = 232, Slots = slots };
        tt.State = SlotState; tt.OnDay = AddSlot; tt.OnSlot = EditSlot; chart = tt; left.Controls.Add(tt);
        bar = new RoundBar { Dock = DockStyle.Top, Height = 22 }; left.Controls.Add(bar);
        var g = new Panel { Dock = DockStyle.Top, Height = 40 };
        numGoal = new Stepper { Left = 92, Top = 2, Width = 136, Min = 5, Max = 1440, Step = 5, Suffix = "min", Value = Math.Min(1440, Math.Max(5, goal)) };
        numGoal.Changed += delegate { goal = numGoal.Value; Save(); RefreshStats(); RefreshStreak(); };
        var bWk = Theme.Btn("Weekly summary", 240, 2, 164, "b"); bWk.Height = 36; ((PillButton)bWk).Icon = "chart"; bWk.Click += delegate { ShowWeekly(); };
        g.Controls.AddRange(new Control[] { new Label { Left = 0, Top = 10, Width = 90, Text = "Daily goal" }, numGoal, bWk }); left.Controls.Add(g);
        lblSummary = new Label { Dock = DockStyle.Top, Height = 36, Font = new Font("Segoe UI", 10, FontStyle.Bold), Tag = "title", TextAlign = ContentAlignment.MiddleLeft }; left.Controls.Add(lblSummary);
        BuildStreak(right);
    }
    Dictionary<string, int> ByDay()
    {
        var d = new Dictionary<string, int>();
        foreach (var s in sessions) { int v; d.TryGetValue(s.Date, out v); d[s.Date] = v + s.Minutes; }
        return d;
    }
    void RefreshStats()
    {
        if (lblSummary == null) return;
        var by = ByDay(); int td; by.TryGetValue(Today(), out td);
        int pages = sessions.Where(s => s.Date == Today()).Sum(s => s.Pages);
        int streak, best; Streaks(out streak, out best);
        lblSummary.Text = "Today: " + td + " min, " + pages + " pages   |   Streak: " + streak + " day(s)" + (td >= goal ? "   Goal reached!" : "");
        bar.Value = Math.Min(100, td * 100 / Math.Max(1, goal)); chart.Invalidate();
        lbSubjects.Items.Clear(); lbSubjects.Items.Add("Total time per subject"); lbSubjects.Items.Add(new string('-', 30));
        foreach (var g in sessions.GroupBy(s => s.Subject).OrderByDescending(g => g.Sum(s => s.Minutes))) lbSubjects.Items.Add(g.Sum(s => s.Minutes).ToString().PadLeft(6) + " min   " + g.Key);
    }
    void Streaks(out int curStreak, out int best)
    {
        var set = new HashSet<string>(sessions.Where(s => s.Minutes > 0).Select(s => s.Date));
        int c = 0; var d = DateTime.Today;
        if (!set.Contains(Key(d))) d = d.AddDays(-1);
        while (set.Contains(Key(d))) { c++; d = d.AddDays(-1); }
        curStreak = c; best = 0; int run = 0; DateTime? prev = null;
        foreach (var k in set.OrderBy(x => x))
        {
            var dt = DateTime.ParseExact(k, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            run = (prev != null && (dt - prev.Value).Days == 1) ? run + 1 : 1;
            if (run > best) best = run; prev = dt;
        }
    }
    void DrawChart(object sender, PaintEventArgs e)
    {
        var by = ByDay(); var gr = e.Graphics; int max = 1; var vals = new int[7]; var days = new DateTime[7];
        for (int i = 0; i < 7; i++) { days[i] = DateTime.Today.AddDays(i - 6); int v; by.TryGetValue(Key(days[i]), out v); vals[i] = v; max = Math.Max(max, v); }
        max = Math.Max(max, goal); int w = Math.Max(30, (chart.Width - 60) / 7 - 12); int gy = 160 - 120 * goal / max;
        using (var pen = new Pen(Theme.Accent3, 2) { DashStyle = DashStyle.Dash }) gr.DrawLine(pen, 10, gy, chart.Width - 10, gy);
        Color[] cs = { Theme.Accent, Theme.Accent2, Theme.Accent3 };
        for (int i = 0; i < 7; i++)
        {
            int x = 30 + i * (w + 12), h = 120 * vals[i] / max;
            using (var br = new SolidBrush(vals[i] >= goal ? Color.SeaGreen : cs[i % 3])) { var rr = new Rectangle(x, 160 - h, w, Math.Max(2, h)); using (var gp = Theme.Round(rr, Math.Min(8, Math.Max(2, h / 2)))) gr.FillPath(br, gp); }
            TextRenderer.DrawText(gr, vals[i] + "m", Font, new Point(x, 160 - h - 22), Theme.Text);
            TextRenderer.DrawText(gr, days[i].ToString("ddd d", CultureInfo.CurrentCulture), Font, new Point(x, 170), Theme.Muted);
        }
    }

    // ---------- streak calendar (monthly heatmap) ----------
    void BuildStreak(Control p)
    {
        heat = new Card { Dock = DockStyle.Fill }; heat.Paint += DrawHeat; heat.MouseClick += HeatClick; p.Controls.Add(heat);
        p.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 10 });
        var detail = new Card { Dock = DockStyle.Bottom, Height = 150, Padding = new Padding(16, 10, 16, 10) };
        lbDay = new ListBox { Dock = DockStyle.Fill, Tag = "flat", Font = new Font("Segoe UI", 9) };
        lblDayInfo = new Label { Dock = DockStyle.Top, Height = 44, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Tag = "title" };
        detail.Controls.Add(lbDay); detail.Controls.Add(lblDayInfo); p.Controls.Add(detail);
        p.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });
        var nav2 = new Panel { Dock = DockStyle.Top, Height = 42 };
        bPrev = Theme.Btn("<", 0, 5, 44, "c"); bNext = Theme.Btn(">", 50, 5, 44, "c"); bThisMonth = Theme.Btn("This month", 100, 5, 110, "b");
        nav2.Controls.AddRange(new Control[] { bPrev, bNext, bThisMonth, new Label { Left = 222, Top = 12, Width = 200, Height = 22, Tag = "muted", Text = "Green = daily goal reached" } });
        p.Controls.Add(nav2);
        lblStreak = new Label { Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI", 9, FontStyle.Bold), Tag = "sub" }; p.Controls.Add(lblStreak);
        p.Controls.Add(new Label { Dock = DockStyle.Top, Height = 32, Text = "Streaks", Font = new Font("Segoe UI", 12.5f, FontStyle.Bold), Tag = "title" });
        bPrev.Click += delegate { viewMonth = viewMonth.AddMonths(-1); heat.Invalidate(); };
        bNext.Click += delegate { viewMonth = viewMonth.AddMonths(1); heat.Invalidate(); };
        bThisMonth.Click += delegate { viewMonth = DateTime.Today; heat.Invalidate(); };
        ShowDay(DateTime.Today);
    }
    void RefreshStreak()
    {
        if (lblStreak == null) return;
        int c, b; Streaks(out c, out b);
        lblStreak.Text = "Current streak: " + c + " day(s)   |   Best: " + b + "   |   Days studied: " + sessions.Where(x => x.Minutes > 0).Select(x => x.Date).Distinct().Count();
        ShowDay(selDay);
    }
    void ShowDay(DateTime d)
    {
        selDay = d; string k = Key(d); var list = sessions.Where(x => x.Date == k).ToList();
        lblDayInfo.Text = d.ToString("dddd, d MMM yyyy", CultureInfo.CurrentCulture) + "\n" + list.Sum(x => x.Minutes) + " min in " + list.Count + " session(s)";
        lbDay.Items.Clear();
        if (list.Count == 0) lbDay.Items.Add("Nothing logged this day.");
        foreach (var x in list) lbDay.Items.Add(x.Minutes + " min  -  " + x.Subject + (x.Pages > 0 ? "  (" + x.Pages + " pages)" : ""));
        heat.Invalidate();
    }
    void HeatGeom(out int cell, out int gap, out int x0, out int y0)
    {
        gap = 5; y0 = 64; int byW = (heat.Width - 28 - 6 * gap) / 7, byH = (heat.Height - y0 - 14 - 5 * gap) / 6;
        cell = Math.Max(20, Math.Min(byW, byH)); x0 = (heat.Width - (7 * cell + 6 * gap)) / 2;
    }
    void HeatClick(object s, MouseEventArgs e)
    {
        int cell, gap, x0, y0; HeatGeom(out cell, out gap, out x0, out y0);
        if (e.X < x0 || e.Y < y0) return;
        int col = (e.X - x0) / (cell + gap), row = (e.Y - y0) / (cell + gap);
        if (col > 6 || row > 5) return;
        int first = ((int)new DateTime(viewMonth.Year, viewMonth.Month, 1).DayOfWeek + 6) % 7, day = row * 7 + col + 1 - first;
        if (day >= 1 && day <= DateTime.DaysInMonth(viewMonth.Year, viewMonth.Month)) ShowDay(new DateTime(viewMonth.Year, viewMonth.Month, day));
    }
    void DrawHeat(object sender, PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; var by = ByDay();
        TextRenderer.DrawText(g, viewMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture), new Font("Segoe UI", 11.5f, FontStyle.Bold), new Point(16, 12), Theme.Text);
        string[] wds = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        int cell, gap, x0, y0; HeatGeom(out cell, out gap, out x0, out y0);
        for (int i = 0; i < 7; i++) TextRenderer.DrawText(g, wds[i], new Font("Segoe UI", 8), new Rectangle(x0 + i * (cell + gap), y0 - 20, cell, 16), Theme.Muted, TextFormatFlags.HorizontalCenter);
        int first = ((int)new DateTime(viewMonth.Year, viewMonth.Month, 1).DayOfWeek + 6) % 7, dim = DateTime.DaysInMonth(viewMonth.Year, viewMonth.Month);
        for (int day = 1; day <= dim; day++)
        {
            int slot = first + day - 1, col = slot % 7, row = slot / 7; var dt = new DateTime(viewMonth.Year, viewMonth.Month, day);
            int v; by.TryGetValue(Key(dt), out v); Color fill;
            if (v <= 0) fill = Theme.Dark ? ControlPaint.Dark(Theme.Card, 0.04f) : Color.FromArgb(238, 246, 245);
            else if (v >= goal) fill = Color.SeaGreen;
            else { float ratio = Math.Min(1f, v / (float)Math.Max(1, goal)); fill = Color.FromArgb((int)(60 + 140 * ratio), Theme.Accent); }
            var r = new Rectangle(x0 + col * (cell + gap), y0 + row * (cell + gap), cell, cell);
            using (var gp = Theme.Round(r, 10)) { using (var b = new SolidBrush(fill)) g.FillPath(b, gp); using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, gp); }
            if (dt == DateTime.Today) using (var pen = new Pen(Theme.Accent2, 2)) using (var gp = Theme.Round(Rectangle.Inflate(r, 1, 1), 11)) g.DrawPath(pen, gp);
            if (dt == selDay.Date) using (var pen = new Pen(Theme.Text, 2)) using (var gp = Theme.Round(Rectangle.Inflate(r, 2, 2), 12)) g.DrawPath(pen, gp);
            TextRenderer.DrawText(g, day.ToString(), new Font("Segoe UI", 8.5f, FontStyle.Bold), r, v > 0 ? Color.White : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // ---------- files ----------
    void BuildFiles(Control p)
    {
        lvFiles = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
        lvFiles.Columns.Add("File", 300); lvFiles.Columns.Add("Progress", 120); lvFiles.Columns.Add("Last opened", 110); lvFiles.Columns.Add("Folder", 300); p.Controls.Add(lvFiles);
        txtSearch = new TextBox { Dock = DockStyle.Top, Font = new Font("Segoe UI", 11.5f) }; p.Controls.Add(txtSearch);
        var top = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Color.Transparent };
        var bChoose = Theme.Btn("Choose root folder", 0, 6, 160, "c"); var bScan = Theme.Btn("Rescan", 168, 6, 90, "a");
        lblRoot = new Label { Left = 270, Top = 14, Width = 560, AutoEllipsis = true, Tag = "muted" };
        top.Controls.AddRange(new Control[] { bChoose, bScan, lblRoot }); p.Controls.Add(top);
        bChoose.Click += delegate { using (var f = new FolderBrowserDialog()) { f.Description = "Pick the root folder with your notes and books"; if (f.ShowDialog() == DialogResult.OK) { root = f.SelectedPath; Save(); Scan(); } } };
        bScan.Click += delegate { Scan(); };
        txtSearch.TextChanged += delegate { Search(); };
        txtSearch.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { OpenFirst(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Down && lvFiles.Items.Count > 0) { lvFiles.Focus(); lvFiles.Items[0].Selected = true; e.Handled = true; }
        };
        lvFiles.DoubleClick += delegate { OpenFirst(); }; lvFiles.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) OpenFirst(); };
        var menu = new ContextMenuStrip(); menu.Items.Add("Open", null, delegate { OpenFirst(); });
        menu.Items.Add("Show in folder", null, delegate { if (lvFiles.SelectedItems.Count > 0) Process.Start("explorer.exe", "/select,\"" + (string)lvFiles.SelectedItems[0].Tag + "\""); });
        menu.Items.Add("Set reading progress...", null, delegate { SetProgress(); });
        menu.Items.Add("Clear progress", null, delegate { if (lvFiles.SelectedItems.Count > 0) { var it = lvFiles.SelectedItems[0]; books.Remove((string)it.Tag); Save(); it.SubItems[1].Text = ""; it.SubItems[2].Text = ""; } });
        lvFiles.ContextMenuStrip = menu;
    }
    static List<string[]> WalkFiles(string startDir)
    {
        // Run filesystem enumeration off the UI thread; use an explicit stack to avoid deep-recursion crashes.
        var found = new List<string[]>(); var pending = new Stack<string>(); pending.Push(startDir);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            try
            {
                foreach (var f in Directory.GetFiles(dir))
                {
                    string name = Path.GetFileName(f);
                    found.Add(new string[] { name.ToLowerInvariant(), name, f });
                }
            }
            catch { /* inaccessible folders/files are skipped */ }
            try { foreach (var d in Directory.GetDirectories(dir)) pending.Push(d); }
            catch { /* inaccessible folders are skipped */ }
        }
        return found;
    }
    void Scan()
    {
        int generation = ++scanGeneration;
        index.Clear();
        string scanRoot = root;
        if (string.IsNullOrWhiteSpace(scanRoot) || !Directory.Exists(scanRoot))
        {
            if (lblRoot != null) lblRoot.Text = "No root folder selected";
            if (lvFiles != null) Search();
            return;
        }
        if (lblRoot != null) lblRoot.Text = "Scanning folder…";
        if (lvFiles != null) lvFiles.Cursor = Cursors.WaitCursor;
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            List<string[]> found = WalkFiles(scanRoot);
            try
            {
                for (int wait = 0; wait < 100 && !IsHandleCreated && !IsDisposed; wait++) System.Threading.Thread.Sleep(50);   // up to 5 s for the window to open
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)delegate
                {
                    // Ignore stale scans if the user picked another folder or started a newer scan.
                    if (IsDisposed || generation != scanGeneration) return;
                    index = found;
                    if (lvFiles != null) lvFiles.Cursor = Cursors.Default;
                    if (lblRoot != null) lblRoot.Text = scanRoot + "  (" + index.Count + " files)";
                    Search();
                });
            }
            catch { /* the form may close while scanning */ }
        });
    }
    string ProgText(string p) { BookProg b; return books.TryGetValue(p, out b) && b.Total > 0 ? b.Page + " / " + b.Total + "  (" + Math.Min(100, b.Page * 100 / b.Total) + "%)" : ""; }
    string OpenedText(string p) { BookProg b; return books.TryGetValue(p, out b) && b.Opened > 0 ? new DateTime(b.Opened, DateTimeKind.Utc).ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.CurrentCulture) : ""; }
    void Search()
    {
        var words = txtSearch.Text.ToLower().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        lvFiles.BeginUpdate(); lvFiles.Items.Clear(); int n = 0;
        IEnumerable<string[]> src = index;
        if (words.Length == 0)   // nothing typed: show what you opened most recently first
        {
            var recent = books.Values.OrderByDescending(b => b.Opened).Select(b => b.FilePath).ToList();
            src = index.Where(e => recent.Contains(e[2])).OrderBy(e => recent.IndexOf(e[2])).Concat(index.Where(e => !recent.Contains(e[2])));
        }
        foreach (var e in src)
            if (words.All(w => e[0].Contains(w)))
            {
                var it = new ListViewItem(new string[] { e[1], ProgText(e[2]), OpenedText(e[2]), Path.GetDirectoryName(e[2]) }); it.Tag = e[2];
                lvFiles.Items.Add(it); if (++n >= 300) break;
            }
        lvFiles.EndUpdate();
    }
    void SetProgress()
    {
        if (lvFiles.SelectedItems.Count == 0) { MessageBox.Show("Select a file first."); return; }
        var it = lvFiles.SelectedItems[0]; string path = (string)it.Tag; BookProg b;
        if (!books.TryGetValue(path, out b)) b = new BookProg { FilePath = path };
        using (var f = new AskForm("Reading progress", Path.GetFileName(path), b.Page, b.Total))
        {
            if (f.ShowDialog(this) != DialogResult.OK) return;
            b.Page = f.Minutes; b.Total = Math.Max(f.Pages, f.Minutes); books[path] = b; Save(); it.SubItems[1].Text = ProgText(path);
        }
    }
    void RecordOpen(string path, ListViewItem it)
    {
        BookProg b; if (!books.TryGetValue(path, out b)) { b = new BookProg { FilePath = path }; books[path] = b; }
        b.Opened = DateTime.UtcNow.Ticks; Save();
        if (it != null) { it.SubItems[1].Text = ProgText(path); it.SubItems[2].Text = OpenedText(path); }
        lblRoot.Text = b.Total > 0 ? "Opened " + Path.GetFileName(path) + " - resume at page " + b.Page + " of " + b.Total + " (" + Math.Min(100, b.Page * 100 / b.Total) + "%)"
                                   : "Opened " + Path.GetFileName(path) + " - right-click it to save your page";
    }
    void OpenFirst()
    {
        ListViewItem it = lvFiles.SelectedItems.Count > 0 ? lvFiles.SelectedItems[0] : (lvFiles.Items.Count > 0 ? lvFiles.Items[0] : null);
        if (it == null) return;
        try { Process.Start(new ProcessStartInfo((string)it.Tag) { UseShellExecute = true }); RecordOpen((string)it.Tag, it); } catch (Exception ex) { MessageBox.Show(ex.Message); }
    }

    void ApplyFontPrefs()
    {
        float r = uiFontSize / 9f;
        if (uiFontFamily != "Segoe UI") Theme.SetFamily(this, uiFontFamily);
        if (Math.Abs(r - 1f) > 0.01f) Theme.ScaleFonts(this, r);
        Theme.Family = uiFontFamily; Theme.FontScale = r;
    }

    // ---------- about ----------
    void BuildAbout(Control p)
    {
        var pb2 = new PictureBox { Left = 10, Top = 10, Width = 130, Height = 130, Image = Theme.Logo(130), BackColor = Color.Transparent };
        var title = new Label { Left = 160, Top = 12, Width = 600, Height = 46, Text = "Study Tracker", Font = new Font("Segoe UI", 21.5f, FontStyle.Bold), Tag = "title" };
        var by = new Label { Left = 163, Top = 62, Width = 600, Height = 30, Text = "Created by 3Mkay Derado", Font = new Font("Segoe UI", 11.5f, FontStyle.Bold), Tag = "sub" };
        var desc = new Label { Left = 163, Top = 98, Width = 640, Height = 50, Text = "A free, offline study companion: plan what to read, run Pomodoro focus blocks, track your streaks, and find any note or book in seconds. Version " + Updater.Version };
        var med = new Label { Left = 10, Top = 170, Width = 780, Height = 26, Text = "Need medical lecture notes? Visit MedEduNotes:", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        var link = new LinkLabel { Left = 10, Top = 198, Width = 600, Height = 26, Text = "https://med-edu-notes.vercel.app/", Font = new Font("Segoe UI", 9.5f) };
        link.LinkClicked += delegate { try { Process.Start(new ProcessStartInfo("https://med-edu-notes.vercel.app/") { UseShellExecute = true }); } catch { } };
        var contactTitle = new Label { Left = 10, Top = 240, Width = 800, Height = 26, Tag = "sub", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Text = "Contact me" };
        var contact = new LinkLabel { Left = 10, Top = 268, Width = 600, Height = 26, Text = "kjnrmbiriri1+studytracker@gmail.com", Font = new Font("Segoe UI", 9) };
        contact.LinkClicked += delegate { try { Process.Start(new ProcessStartInfo("mailto:kjnrmbiriri1+studytracker@gmail.com") { UseShellExecute = true }); } catch { MessageBox.Show("Email: kjnrmbiriri1+studytracker@gmail.com", "Contact me"); } };
        var tip = new Label { Left = 10, Top = 302, Width = 800, Height = 44, Tag = "muted", Text = "Tip: put a file named logo.png next to the app to replace the built-in logo. Your study data stays on this computer and never leaves it. Optional network use: the daily update check, and an anonymous daily \"app opened\" count (switch it off any time in Settings)." };
        lblInst = new Label { Left = 10, Top = 352, Width = 800, Height = 24, Tag = "muted", Text = "Installed at: " + (Installer.RunningInstalled() ? App.ExeDir : "not installed (running portable)") };
        var bun = Theme.Btn("Uninstall Study Tracker", 10, 386, 220, "b"); bun.Height = 36;
        bun.Click += delegate { if (Installer.Uninstall(this)) { loading = true; Environment.Exit(0); } };
        p.Controls.AddRange(new Control[] { pb2, title, by, desc, med, link, contactTitle, contact, tip, lblInst, bun });
    }

    // ---------- updates ----------
    static string Fmt(DateTime d) { return d == DateTime.MinValue ? "never" : d.ToLocalTime().ToString("dd MMM yyyy, HH:mm", CultureInfo.CurrentCulture); }
    void Ui(Action a) { try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch { } }
    void BuildSettings(Control p)
    {
        var title = new Label { Left = 10, Top = 6, Width = 600, Height = 44, Text = "Settings", Font = new Font("Segoe UI", 18, FontStyle.Bold), Tag = "title" };
        var appearance = new Label { Left = 10, Top = 52, Width = 400, Height = 28, Text = "Appearance & profile", Font = new Font("Segoe UI", 11.5f, FontStyle.Bold), Tag = "sub" };
        var nameLabel = new Label { Left = 10, Top = 88, Width = 90, Height = 28, Text = "Your name", Tag = "muted" };
        var nameBox = new TextBox { Left = 105, Top = 84, Width = 220, Height = 28, Text = userName, Tag = "flat" };
        var saveName = Theme.Btn("Save name", 336, 82, 110, "a"); saveName.Height = 32;
        saveName.Click += delegate { if (!string.IsNullOrWhiteSpace(nameBox.Text)) { userName = nameBox.Text.Trim(); Save(); Greet(); } };
        var themeLabel = new Label { Left = 10, Top = 128, Width = 90, Height = 28, Text = "Theme", Tag = "muted" };
        var themePick = new ComboBox { Left = 105, Top = 124, Width = 150, DropDownStyle = ComboBoxStyle.DropDownList }; themePick.Items.AddRange(new object[] { "Dark", "Light" }); themePick.SelectedIndex = Theme.Dark ? 0 : 1;
        themePick.SelectedIndexChanged += delegate { Theme.Apply(themePick.SelectedIndex == 0, Theme.Pal); Recolor(); };
        var colorPick = Theme.Btn("Change accent colour", 270, 122, 180, "c"); colorPick.Height = 32; colorPick.Click += delegate { Theme.Apply(Theme.Dark, Theme.Pal + 1); Recolor(); };
        var familyLabel = new Label { Left = 10, Top = 168, Width = 90, Height = 28, Text = "Font", Tag = "muted" };
        var familyPick = new ComboBox { Left = 105, Top = 164, Width = 150, DropDownStyle = ComboBoxStyle.DropDownList }; familyPick.Items.AddRange(new object[] { "Segoe UI", "Arial", "Calibri", "Consolas" }); familyPick.SelectedItem = uiFontFamily;
        familyPick.SelectedIndexChanged += delegate { if (familyPick.SelectedItem != null) { string fam = familyPick.SelectedItem.ToString(); Theme.SetFamily(this, fam); uiFontFamily = fam; Theme.Family = fam; RefreshTasks(); RefreshStats(); Save(); } };
        var fontLabel = new Label { Left = 10, Top = 208, Width = 90, Height = 28, Text = "Font size", Tag = "muted" };
        var fontPick = new ComboBox { Left = 105, Top = 204, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList }; fontPick.Items.AddRange(new object[] { "Small", "Normal", "Large", "Extra large" }); fontPick.SelectedIndex = uiFontSize < 9.5f ? 0 : uiFontSize < 11f ? 1 : uiFontSize < 13f ? 2 : 3;
        fontPick.SelectedIndexChanged += delegate { float next = new float[] { 9f, 10f, 12f, 14f }[fontPick.SelectedIndex]; Theme.ScaleFonts(this, next / uiFontSize); uiFontSize = next; Theme.FontScale = next / 9f; RefreshTasks(); RefreshStats(); Save(); };
        var minimizeCheck = new SwitchBox { Left = 10, Top = 240, Width = 500, Height = 26, Text = "Minimize Study Tracker while the reading timer is running", Checked = minimizeDuringTimer };
        minimizeCheck.CheckedChanged += delegate { minimizeDuringTimer = minimizeCheck.Checked; Save(); };
        var statsCheck = new SwitchBox { Left = 10, Top = 272, Width = 820, Height = 26, Visible = Updater.StatsCode.Length > 0, Checked = statsOptIn,
            Text = "Share an anonymous daily usage count (no personal data)" };
        statsCheck.CheckedChanged += delegate { statsOptIn = statsCheck.Checked; statsAsked = true; Save(); if (statsOptIn) StatsPing(); RefreshUsers(); };
        var remHead = new Label { Left = 10, Top = 310, Width = 400, Height = 28, Text = "Reminders", Font = new Font("Segoe UI", 11.5f, FontStyle.Bold), Tag = "sub" };
        var remDailyCheck = new SwitchBox { Left = 10, Top = 346, Width = 230, Height = 26, Text = "Daily study reminder at", Checked = remDaily };
        var remTimeStep = new Stepper { Left = 244, Top = 341, Width = 150, Clock = true, Step = 15, Value = remHour * 60 + remMinute };   // shows e.g. 18:15
        var remBreakCheck = new SwitchBox { Left = 10, Top = 386, Width = 230, Height = 26, Text = "Break reminder every", Checked = remBreak };
        var remBreakStep = new Stepper { Left = 244, Top = 381, Width = 130, Min = 15, Max = 180, Step = 5, Suffix = "min", Value = remBreakMin };
        var remBreakNote = new Label { Left = 384, Top = 390, Width = 380, Height = 22, Tag = "muted", Text = "while a timer is running" };
        var remTrayCheck = new SwitchBox { Left = 10, Top = 426, Width = 820, Height = 26, Text = "Keep Study Tracker running in the tray when I close the window (needed for reminders)", Checked = remTray };
        Action remSave = delegate
        {
            remDaily = remDailyCheck.Checked; remHour = remTimeStep.Value / 60; remMinute = remTimeStep.Value % 60; remBreak = remBreakCheck.Checked;
            remBreakMin = remBreakStep.Value; remTray = remTrayCheck.Checked; Save(); SyncTray();
        };
        remDailyCheck.CheckedChanged += delegate { remSave(); }; remBreakCheck.CheckedChanged += delegate { remSave(); }; remTrayCheck.CheckedChanged += delegate { remSave(); };
        remTimeStep.Changed += delegate { remSave(); }; remBreakStep.Changed += delegate { remSave(); };
        int y0 = 470;   // every control in this section is placed relative to this one value, so they can never drift apart
        var updatesHead = new Label { Left = 10, Top = y0, Width = 400, Height = 28, Text = "Software updates", Font = new Font("Segoe UI", 11.5f, FontStyle.Bold), Tag = "sub" };
        lblVer = new Label { Left = 10, Top = y0 + 34, Width = 700, Height = 26, Font = new Font("Segoe UI", 11.5f, FontStyle.Bold) };
        lblUpdInfo = new Label { Left = 10, Top = y0 + 62, Width = 800, Height = 24, Tag = "muted" };
        lblUpdStatus = new Label { Left = 10, Top = y0 + 90, Width = 820, Height = 28, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Tag = "white" };
        bCheck = Theme.Btn("Check for updates", 10, y0 + 126, 190, "a"); bCheck.Height = 38; ((PillButton)bCheck).Icon = "refresh";
        bInstall = Theme.Btn("Install update now", 210, y0 + 126, 190, "c"); bInstall.Height = 38; ((PillButton)bInstall).Icon = "download"; bInstall.Visible = false;
        prog = new ProgressBar { Left = 414, Top = y0 + 139, Width = 260, Height = 12, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30, Visible = false };
        var updRow = new FlowLayoutPanel { Left = 10, Top = y0 + 126, Width = 840, Height = 40, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
        bCheck.Width = 190; bInstall.Width = 190; bCheck.Height = 38; bInstall.Height = 38;
        bCheck.Margin = new Padding(0, 0, 10, 0); bInstall.Margin = new Padding(0, 0, 16, 0); prog.Margin = new Padding(0, 13, 0, 0); prog.Width = 260; prog.Height = 12;
        updRow.Controls.Add(bCheck); updRow.Controls.Add(bInstall); updRow.Controls.Add(prog);
        rbAuto = new RadioBox { Left = 10, Top = y0 + 180, Width = 820, Height = 26, Text = "Automatic updates", Checked = autoMode };
        rbManual = new RadioBox { Left = 10, Top = y0 + 208, Width = 820, Height = 26, Text = "Notify me; let me install updates", Checked = !autoMode };
        var note = new Label { Left = 10, Top = y0 + 242, Width = 820, Height = 24, Tag = "muted", Text = "Your study data stays on this computer during updates." };
        p.Controls.AddRange(new Control[] { title, appearance, nameLabel, nameBox, saveName, themeLabel, themePick, colorPick, familyLabel, familyPick, fontLabel, fontPick, minimizeCheck, statsCheck, remHead, remDailyCheck, remTimeStep, remBreakCheck, remBreakStep, remBreakNote, remTrayCheck, updatesHead, lblVer, lblUpdInfo, lblUpdStatus, updRow, rbAuto, rbManual, note });
        bCheck.Click += delegate { CheckNow(true); };
        bInstall.Click += delegate { InstallUpdate(false); };
        rbAuto.CheckedChanged += delegate { autoMode = rbAuto.Checked; Save(); };
    }
    void UpdateUI()
    {
        if (lblVer == null) return;
        updateAvailable = latest != DateTime.MinValue && latest > stamp;
        lblVer.Text = "Study Tracker  v" + Updater.Version;
        lblUpdInfo.Text = "Version " + Updater.Version + "  •  Last checked: " + Fmt(lastCheck);
        if (updateAvailable) { lblUpdStatus.Text = "An update is available!"; lblUpdStatus.ForeColor = ColorTranslator.FromHtml("#E08A00"); }
        else { lblUpdStatus.Text = latest == DateTime.MinValue ? "Not checked yet. Press \"Check for updates\"." : "You're up to date."; lblUpdStatus.ForeColor = Theme.Accent; }
        bInstall.Visible = updateAvailable; Text = updateAvailable ? "Study Tracker  -  update available" : "Study Tracker";
        if (nav[PAGE_SETTINGS] != null) nav[PAGE_SETTINGS].Invalidate();
    }
    void NavDot(object s, PaintEventArgs e)
    {
        if (!updateAvailable) return;
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; int x = nav[PAGE_SETTINGS].Width - 22;
        using (var br = new SolidBrush(ColorTranslator.FromHtml(blink ? "#FFD21F" : "#FF8A00"))) g.FillEllipse(br, x, 7, 13, 13);
        using (var pen = new Pen(Color.White, 1.5f)) g.DrawEllipse(pen, x, 7, 13, 13);
    }
    void StartTimers()
    {
        dotTimer = new Timer { Interval = 500 }; dotTimer.Tick += delegate { blink = !blink; if (updateAvailable) nav[PAGE_SETTINGS].Invalidate(); }; dotTimer.Start();
        hourTimer = new Timer { Interval = 3600000 }; hourTimer.Tick += delegate { AutoCheck(); }; hourTimer.Start(); InitReminders();
        // also check immediately when internet (re)connects — still at most once per calendar day
        NetworkChange.NetworkAvailabilityChanged += delegate { Ui(delegate { AutoCheck(); }); };
    }
    void BuildFooter()
    {
        var foot = new Panel { Dock = DockStyle.Bottom, Height = 30, Tag = "card", Visible = Updater.StatsCode.Length > 0 };
        foot.Paint += (s, e) => { using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 0, 0, foot.Width, 0); };
        lblUsers = new Label { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft, Tag = "muted", AutoEllipsis = true, Text = "Active users: loading..." };
        foot.Controls.Add(lblUsers); Controls.Add(foot);
        usersTimer = new Timer { Interval = 120000 }; usersTimer.Tick += delegate { RefreshUsers(); }; usersTimer.Start();   // every 2 minutes
    }
    void RefreshUsers()
    {
        if (lblUsers == null || Updater.StatsCode.Length == 0) return;
        if (!statsOptIn) { lblUsers.Text = "Active users: hidden while the usage count is switched off in Settings"; return; }
        if (WindowState == FormWindowState.Minimized) return;                 // nobody is looking: save the data
        bool full = (usersTick++ % 5 == 0) || uWeek == null;                  // "Today" every 2 min; the slower-moving numbers every 10 min
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            string td = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string a = Updater.GetCount("/active", "start=" + td);
            if (a == null) a = Updater.GetCount("/active", "start=" + td + "&end=" + DateTime.Today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (full) { uWeek = Updater.GetCount("/active", "start=week"); uMonth = Updater.GetCount("/active", "start=month"); uTotal = Updater.GetCount("/install", ""); }
            string w = uWeek, m = uMonth, t = uTotal;
            Ui(delegate
            {
                lblUsers.Text = (a == null && w == null && m == null && t == null) ? "Active users: not available right now (" + Updater.LastCountInfo + ")"
                    : "Active users   Today " + (a ?? "-") + "   |   Last 7 days " + (w ?? "-") + "   |   Last month " + (m ?? "-") + "   |   Total installs " + (t ?? "-") + ((a == null || w == null || m == null || t == null) ? "   (" + Updater.LastCountInfo + ")" : "");
            });
        });
    }
    void StatsPing()
    {
        if (!statsOptIn || statsBusy || Updater.StatsCode.Length == 0) return;
        if (lastPing != DateTime.MinValue && lastPing.ToLocalTime().Date == DateTime.Now.Date) return;   // at most once per local day
        statsBusy = true; bool first = !installSent;
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            bool installOk = !first || Updater.SendPing("/install");   // counted once per installation
            bool ok = installOk && Updater.SendPing("/active");        // counted once per day
            Ui(delegate { statsBusy = false; if (installOk) installSent = true; if (ok) lastPing = DateTime.UtcNow; Save(); RefreshUsers(); });
        });
    }
    int SlotState(Slot sl)   // 1 = done (enough minutes logged for that subject that day), 2 = missed (day has passed), 0 = still to come
    {
        DateTime mon = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7)), date = mon.AddDays(sl.Day); string k = Key(date);
        int done = sessions.Where(x => x.Date == k && x.Subject == sl.Subject).Sum(x => x.Minutes);
        return done >= Math.Max(5, sl.Minutes) ? 1 : (date < DateTime.Today ? 2 : 0);
    }
    void AddSlot(int day) { EditSlotCore(new Slot { Day = day, Subject = selTask != null ? selTask.Title : "", Minutes = 45 }, false); }
    void EditSlot(Slot sl) { EditSlotCore(sl, true); }
    void EditSlotCore(Slot sl, bool exists)
    {
        string[] dn = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        var subjects = tasks.Select(t => t.Title).Concat(slots.Select(x => x.Subject)).Distinct().ToList();
        using (var f = new SlotForm(dn[sl.Day], sl, subjects, exists))
        {
            if (f.ShowDialog(this) != DialogResult.OK) return;
            if (f.Deleted) slots.Remove(sl);
            else { sl.Time = f.Result.Time; sl.Minutes = f.Result.Minutes; sl.Subject = f.Result.Subject; if (!exists) slots.Add(sl); }
            Save(); chart.Invalidate();
        }
    }

    // ---------- reminders ----------
    void InitReminders()
    {
        tray = new NotifyIcon { Text = "Study Tracker" };
        try { tray.Icon = Icon; } catch { }
        var cm = new ContextMenuStrip();
        cm.Items.Add("Open Study Tracker", null, delegate { ShowFromTray(); });
        cm.Items.Add("Exit", null, delegate { exiting = true; tray.Visible = false; Application.Exit(); });
        tray.ContextMenuStrip = cm; tray.DoubleClick += delegate { ShowFromTray(); }; tray.BalloonTipClicked += delegate { ShowFromTray(); };
        remTimer = new Timer { Interval = 30000 }; remTimer.Tick += delegate { ReminderTick(); }; remTimer.Start();
        FormClosing += delegate(object sender, FormClosingEventArgs e)
        {
            if (!exiting && remTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; Hide(); SyncTray();
                if (!trayHinted) { trayHinted = true; tray.ShowBalloonTip(3000, "Study Tracker", "Still running here so your reminders keep working.", ToolTipIcon.Info); }
            }
        };
        FormClosed += delegate { try { tray.Visible = false; tray.Dispose(); } catch { } };
        SyncTray();
    }
    void SyncTray() { if (tray != null && tray.Icon != null) tray.Visible = remDaily || remBreak || remTray; }
    void ShowFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    void Notify(string title, string text)
    {
        if (tray == null || tray.Icon == null) return;
        tray.Visible = true; tray.ShowBalloonTip(8000, title, text, ToolTipIcon.Info);
        if (!muted) Sound.Chime();
    }
    void ReminderTick()
    {
        DateTime now = DateTime.Now; string today = Key(now); DateTime at = now.Date.AddHours(remHour).AddMinutes(remMinute);
        if (remDaily && lastDaily != today && now >= at && now < at.AddHours(3))   // fires once a day, up to 3 h after the chosen time
        {
            lastDaily = today; Save(); int todo = tasks.Count(x => !x.Done);
            var tp = slots.Where(x => x.Day == ((int)now.DayOfWeek + 6) % 7).OrderBy(x => x.Time).Select(x => x.Time + " " + x.Subject).ToList();
            Notify("Time to study" + (string.IsNullOrEmpty(userName) ? "" : ", " + userName) + "!",
                (todo > 0 ? "You have " + todo + " task(s) to do. A short session now keeps your streak going." : "Add a topic and start a focus session to keep your streak going.") + (tp.Count > 0 ? " Planned today: " + string.Join(", ", tp.ToArray()) + "." : ""));
        }
        bool timing = (mini != null && !mini.IsDisposed) || frunning;
        if (remBreak && timing) { breakAcc += 0.5; if (breakAcc >= remBreakMin) { breakAcc = 0; Notify("Take a short break", "You have been studying for a while. Stretch, drink some water and rest your eyes."); } }
        else breakAcc = 0;
    }

    // ---------- weekly summary ----------
    static string Hm(int m) { return m >= 60 ? (m / 60) + " h " + (m % 60) + " min" : m + " min"; }
    IEnumerable<Sess> Between(int fromAgo, int toAgo)   // sessions from N..M days ago (0 = today)
    {
        foreach (var x in sessions)
        {
            DateTime d; if (!DateTime.TryParseExact(x.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) continue;
            int ago = (int)(DateTime.Today - d.Date).TotalDays; if (ago >= fromAgo && ago <= toAgo) yield return x;
        }
    }
    void MaybeWeekly()   // once per calendar week, the first time the app is opened and there is something to report
    {
        DateTime mon = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7)); string key = Key(mon);
        if (lastSummary == key) return;
        if (Between(0, 13).Sum(x => x.Minutes) == 0) return;
        lastSummary = key; Save(); ShowWeekly();
    }
    void ShowWeekly()
    {
        var week = Between(0, 6).ToList(); int tw = week.Sum(x => x.Minutes), lw = Between(7, 13).Sum(x => x.Minutes);
        var rows = new List<string[]>();
        string delta = tw == lw ? "same as last week" : (tw > lw ? "+" : "-") + Hm(Math.Abs(tw - lw)) + " vs last week";
        rows.Add(new string[] { "Studied in the last 7 days", Hm(tw) + "   (" + delta + ")" });
        var days = week.GroupBy(x => x.Date).Select(g => new { D = g.Key, M = g.Sum(x => x.Minutes) }).ToList();
        rows.Add(new string[] { "Days studied", days.Count(x => x.M > 0) + " of 7   |   daily goal reached on " + days.Count(x => x.M >= goal) });
        var best = days.OrderByDescending(x => x.M).FirstOrDefault(); DateTime bd;
        rows.Add(new string[] { "Best day", best == null ? "-" : (DateTime.TryParseExact(best.D, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out bd) ? bd.ToString("dddd d MMM", CultureInfo.CurrentCulture) : best.D) + "  -  " + Hm(best.M) });
        var top = week.GroupBy(x => x.Subject).Select(g => new { S = g.Key, M = g.Sum(x => x.Minutes) }).OrderByDescending(x => x.M).FirstOrDefault();
        rows.Add(new string[] { "Most-studied subject", top == null ? "-" : top.S + "  -  " + Hm(top.M) });
        var open = tasks.Where(x => !x.Done).Select(x => new { T = x, M = week.Where(w => w.Subject == x.Title).Sum(w => w.Minutes) }).OrderBy(x => x.M).ThenBy(x => x.T.DaysLeft).FirstOrDefault();
        rows.Add(new string[] { "Needs attention", open == null ? "No open tasks" : open.T.Title + "  -  " + Hm(open.M) + " this week" });
        int c, bst; Streaks(out c, out bst); int over = tasks.Count(x => !x.Done && x.DaysLeft < 0);
        rows.Add(new string[] { "Streak", c + " day(s)   (best " + bst + ")" + (over > 0 ? "   |   " + over + " overdue task(s)" : "") });
        using (var f = new InfoForm("Your week in review", DateTime.Today.AddDays(-6).ToString("d MMM", CultureInfo.CurrentCulture) + " - " + DateTime.Today.ToString("d MMM yyyy", CultureInfo.CurrentCulture), rows)) f.ShowDialog(this);
    }

    void AutoCheck()
    {
        StatsPing();
        // daily: at most one successful check per local calendar day; failures (offline) retry hourly / on reconnect
        if (!busy && (lastCheck == DateTime.MinValue || lastCheck.ToLocalTime().Date != DateTime.Now.Date)) CheckNow(false);
    }
    void SetBusy(bool on, string msg)
    {
        prog.Visible = on; bCheck.Enabled = !on; bInstall.Enabled = !on; if (msg != null) lblUpdStatus.Text = msg;
    }
    void CheckNow(bool manual)
    {
        if (busy) return; busy = true; SetBusy(true, "Checking GitHub...");
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            DateTime d; string err; bool ok = Updater.GetLatest(out d, out err);
            Ui(delegate
            {
                busy = false; SetBusy(false, null);
                if (ok) { latest = d; lastCheck = DateTime.UtcNow; Save(); UpdateUI(); if (updateAvailable && autoMode && !manual) InstallUpdate(true); }
                else { UpdateUI(); if (manual) lblUpdStatus.Text = "Could not check for updates: " + err; }
            });
        });
    }
    void InstallUpdate(bool silent)
    {
        if (busy) return;
        if (mini != null && !mini.IsDisposed) { if (!silent) MessageBox.Show("Finish your reading timer first, then install the update."); return; }
        if (frunning) { if (!silent) MessageBox.Show("Pause or finish your focus timer first, then install the update."); return; }
        if (!silent && MessageBox.Show("Download and install the update now? Study Tracker will restart. Your data is kept.", "Update", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        busy = true; SetBusy(true, "Starting update..."); if (silent) lblQuote.Text = "Updating to the latest version...";
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                string exe = Updater.Prepare(delegate(string m) { Ui(delegate { lblUpdStatus.Text = m; }); });
                Ui(delegate
                {
                    if (Updater.Apply(exe)) { stamp = latest; Save(); Environment.Exit(0); }
                    else { Updater.Cleanup(); busy = false; SetBusy(false, "Update cancelled. Nothing was changed."); Greet(); }
                });
            }
            catch (Exception ex)
            {
                Updater.Cleanup(); string msg = ex.Message;
                Ui(delegate { busy = false; SetBusy(false, "Update failed: " + msg); Greet(); });
            }
        });
    }

    string Prompt(string title, string label, string def)
    {
        using (var f = new AskForm(title, label, def)) return f.ShowDialog(this) == DialogResult.OK ? f.Answer : null;
    }
    bool AskMinutes(string title, string sub, string label, int def, bool pages, out int mins, out int pgs)
    {
        using (var f = new AskForm(title, sub, label, def, pages)) { bool ok = f.ShowDialog(this) == DialogResult.OK; mins = f.Minutes; pgs = f.Pages; return ok; }
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles(); Theme.Apply(true, 0);
        if (args.Length > 0 && args[0] == "/uninstall") { Installer.Uninstall(null); return; }
        if (!Installer.RunningInstalled())
        {
            using (var s = new SetupForm())
            {
                s.ShowDialog();
                if (s.Choice == 0) return;
                if (s.Choice == 1)
                {
                    string err; bool ok = Installer.Install(s.Dir, s.Desk, s.StartMenu, out err);
                    if (err != null) MessageBox.Show(err, "Study Tracker Setup");
                    if (ok) { MessageBox.Show("Study Tracker is installed! Opening it now.", "Study Tracker Setup"); Process.Start(Path.Combine(s.Dir, "StudyTracker.exe")); }
                    return;
                }
            }
        }
        Application.Run(new MainForm());
    }
}
