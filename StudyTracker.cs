using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

class StudyTask { public string Title; public int Target; public bool Done; }
class Sess { public string Date, Subject; public int Minutes, Pages; }

static class App
{
    public static string ExeDir { get { return Path.GetDirectoryName(Application.ExecutablePath); } }
    public static string DataDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StudyTracker"); } }
}

// ===== Colors: built around the MedEduNotes dark teal (#0c1f22) =====
static class Theme
{
    public static bool Dark; public static int Pal;
    public static Color Base = ColorTranslator.FromHtml("#0c1f22");
    public static Color Bg, Card, Text, Muted, Accent, Accent2, Accent3;
    public static string[] PalNames = { "Ocean", "Rose", "Lavender", "Sunny" };
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
        Accent = ColorTranslator.FromHtml(pals[Pal][0]); Accent2 = ColorTranslator.FromHtml(pals[Pal][1]); Accent3 = ColorTranslator.FromHtml(pals[Pal][2]);
    }
    public static Button Btn(string text, int x, int y, int w, string role)
    {
        var b = new Button { Text = text, Left = x, Top = y, Width = w, Height = 32, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Tag = role, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
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
            if (k is Button) { if (tag != "nav") { var b = (Button)k; b.BackColor = tag == "b" ? Accent2 : tag == "c" ? Accent3 : Accent; b.ForeColor = tag == "c" ? Base : Color.White; } }
            else if (k is LinkLabel) { var l = (LinkLabel)k; l.LinkColor = Accent; l.ActiveLinkColor = Accent2; l.BackColor = Color.Transparent; }
            else if (k is Label) { k.BackColor = Color.Transparent; if (tag != "white") k.ForeColor = tag == "muted" ? Muted : tag == "title" ? Accent : tag == "sub" ? Accent2 : Text; }
            else if (k is CheckBox) { k.ForeColor = Text; k.BackColor = Color.Transparent; }
            else if (k is TextBox) { k.BackColor = Card; k.ForeColor = Text; ((TextBox)k).BorderStyle = BorderStyle.FixedSingle; }
            else if (k is NumericUpDown) { k.BackColor = Card; k.ForeColor = Text; }
            else if (k is ListView) { k.BackColor = Card; k.ForeColor = Text; ((ListView)k).BorderStyle = BorderStyle.FixedSingle; }
            else if (k is ListBox) { k.BackColor = Card; k.ForeColor = Text; ((ListBox)k).BorderStyle = BorderStyle.FixedSingle; }
            else if (k is Panel)
            {
                if (tag == "banner") { } else if (tag == "line") k.BackColor = Accent; else if (tag == "card") k.BackColor = Card; else k.BackColor = Bg;
            }
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
    static bool CanWrite(string dir)
    {
        try { Directory.CreateDirectory(dir); string f = Path.Combine(dir, "~w.tmp"); File.WriteAllText(f, "x"); File.Delete(f); return true; } catch { return false; }
    }
    static bool RunBat(List<string> lines, bool elevate, bool wait)
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
        // only our own files are removed; the folder is removed only if it is empty afterwards
        var l = new List<string> { "ping 127.0.0.1 -n 3 >nul",
            "del /f /q \"" + Path.Combine(dir, "StudyTracker.exe") + "\"", "del /f /q \"" + Path.Combine(dir, "StudyTracker.ico") + "\"",
            "del /f /q \"" + Path.Combine(dir, "logo.png") + "\"", "rmdir \"" + dir + "\"" };
        RunBat(l, !CanWrite(dir), false);
        MessageBox.Show(owner, "Study Tracker has been uninstalled. Thanks for using it!", "Uninstall");
        return true;
    }
}

class SetupForm : Form
{
    public int Choice; public string Dir; public bool Desk = true, StartMenu = true;
    public SetupForm()
    {
        Text = "Study Tracker Setup"; ClientSize = new Size(600, 400); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 10); BackColor = Theme.Bg;
        var ban = new Panel { Dock = DockStyle.Top, Height = 90, Tag = "banner" };
        ban.Paint += (s, e) => { using (var gb = new LinearGradientBrush(ban.ClientRectangle, Theme.Base, Theme.Accent, 0f)) e.Graphics.FillRectangle(gb, ban.ClientRectangle); };
        var pic = new PictureBox { Left = 18, Top = 15, Width = 60, Height = 60, Image = Theme.Logo(60), BackColor = Color.Transparent };
        var h = new Label { Left = 92, Top = 16, Width = 480, Height = 34, Text = "Welcome to Study Tracker", Font = new Font("Segoe UI", 18, FontStyle.Bold), Tag = "white", ForeColor = Color.White };
        var h2 = new Label { Left = 94, Top = 54, Width = 480, Height = 24, Text = "Created by 3Mkay Derado", Tag = "white", ForeColor = Color.FromArgb(210, 245, 240) };
        ban.Controls.AddRange(new Control[] { pic, h, h2 });
        var l1 = new Label { Left = 24, Top = 110, Width = 550, Height = 24, Text = "Where should Study Tracker be installed?", Font = new Font("Segoe UI", 11, FontStyle.Bold) };
        var tb = new TextBox { Left = 24, Top = 140, Width = 440, Text = Installer.DefaultDir() };
        var br2 = Theme.Btn("Browse...", 474, 137, 100, "c");
        var cd = new CheckBox { Left = 24, Top = 192, Width = 400, Text = "Create a desktop shortcut", Checked = true };
        var cm = new CheckBox { Left = 24, Top = 222, Width = 400, Text = "Add to the Start menu", Checked = true };
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

// ===== Pop-up reading timer: borderless, rounded, draggable, no white title bar =====
class MiniTimer : Form
{
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, int w, int l);
    public event Action<int, bool> Finished;
    int total, left, elapsed; bool paused, ended; public bool Muted;
    Label lbl, lblTask; ProgressBar pb; Timer t = new Timer { Interval = 1000 }; Button bPause, bPin, bMute; SoundPlayer player;

    public MiniTimer(string task, int minutes, bool muted)
    {
        Muted = muted; total = left = minutes * 60;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; ClientSize = new Size(240, 166);
        BackColor = Color.FromArgb(17, 41, 45); Font = new Font("Segoe UI", 9);
        Region = new Region(Theme.Round(new Rectangle(0, 0, Width, Height), 22));
        var wa = Screen.PrimaryScreen.WorkingArea; StartPosition = FormStartPosition.Manual; Location = new Point(wa.Right - Width - 14, wa.Top + 90);
        lblTask = new Label { Text = task, Left = 16, Top = 12, Width = 208, Height = 18, ForeColor = Theme.Accent3, AutoEllipsis = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        lbl = new Label { Left = 0, Top = 32, Width = 240, Height = 52, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Consolas", 28, FontStyle.Bold) };
        pb = new ProgressBar { Left = 16, Top = 90, Width = 208, Height = 8, Maximum = total, Style = ProgressBarStyle.Continuous };
        bPause = Mk("Pause", 16, 108, 64, Theme.Accent); bPin = Mk("Pinned", 88, 108, 64, Theme.Accent2); bMute = Mk(Muted ? "Unmute" : "Mute", 160, 108, 64, Color.FromArgb(70, 100, 105));
        var bStop = Mk("Finish early", 16, 136, 208, Color.FromArgb(40, 70, 76));
        Controls.AddRange(new Control[] { lblTask, lbl, pb, bPause, bPin, bMute, bStop });
        MouseDown += Drag; lbl.MouseDown += Drag; lblTask.MouseDown += Drag; Show2();
        t.Tick += delegate { if (paused || ended) return; left--; elapsed++; Show2(); if (left <= 0) Done(true); };
        t.Start();
        bPause.Click += delegate { paused = !paused; bPause.Text = paused ? "Resume" : "Pause"; };
        bPin.Click += delegate { TopMost = !TopMost; bPin.Text = TopMost ? "Pinned" : "Pin"; bPin.BackColor = TopMost ? Theme.Accent2 : Color.FromArgb(70, 100, 105); };
        bMute.Click += delegate { Muted = !Muted; bMute.Text = Muted ? "Unmute" : "Mute"; };
        bStop.Click += delegate { Done(false); };
        FormClosing += delegate { if (!ended) { ended = true; t.Stop(); if (Finished != null) Finished(elapsed / 60, false); } };
    }
    void Drag(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); } }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = Theme.Round(new Rectangle(1, 1, Width - 3, Height - 3), 22)) using (var pen = new Pen(Theme.Accent, 3)) e.Graphics.DrawPath(pen, p);
    }
    Button Mk(string s, int x, int y, int w, Color c)
    {
        var b = new Button { Text = s, Left = x, Top = y, Width = w, Height = 24, FlatStyle = FlatStyle.Flat, BackColor = c, ForeColor = Color.White, Cursor = Cursors.Hand };
        b.FlatAppearance.BorderSize = 0; return b;
    }
    void Show2() { lbl.Text = (left / 60).ToString("00") + ":" + (left % 60).ToString("00"); pb.Value = Math.Min(total, Math.Max(0, elapsed)); }
    void Done(bool completed)
    {
        if (ended) return; ended = true; t.Stop();
        if (completed) { lbl.Text = "Time's up!"; lbl.Font = new Font("Segoe UI", 17, FontStyle.Bold); BackColor = Color.FromArgb(20, 70, 72); if (!Muted) Chime(); }
        if (Finished != null) Finished((int)Math.Round(elapsed / 60.0), completed);
        if (completed) { var c = new Timer { Interval = 6000 }; c.Tick += delegate { c.Stop(); Close(); }; c.Start(); } else Close();
    }
    void Chime() // soft bell generated in memory, low volume
    {
        int sr = 22050; var pcm = new List<short>(); double[] notes = { 784, 1047, 1319 };
        foreach (double f in notes) for (int i = 0; i < sr * 0.5; i++) { double tt = i / (double)sr; pcm.Add((short)(Math.Sin(2 * Math.PI * f * tt) * Math.Exp(-5 * tt) * 5000)); }
        var ms = new MemoryStream(); var w = new BinaryWriter(ms);
        w.Write(new char[] { 'R', 'I', 'F', 'F' }); w.Write(36 + pcm.Count * 2); w.Write(new char[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sr); w.Write(sr * 2); w.Write((short)2); w.Write((short)16);
        w.Write(new char[] { 'd', 'a', 't', 'a' }); w.Write(pcm.Count * 2); foreach (var s in pcm) w.Write(s);
        ms.Position = 0; player = new SoundPlayer(ms); player.Play();
    }
}

class MainForm : Form
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
    string dataFile, root = "", userName = ""; int goal = 60, cur = 0; bool loading = true, muted = false;
    List<StudyTask> tasks = new List<StudyTask>(); List<Sess> sessions = new List<Sess>(); List<string[]> index = new List<string[]>();
    Panel banner, content; Panel[] pages = new Panel[4]; Button[] nav = new Button[4]; PictureBox pic; Button bDark, bPal;
    TextBox txtTitle; NumericUpDown numTarget; ListView lvTasks; Label lblStatus, lblHello, lblQuote, lblInst; Button btnRead; MiniTimer mini; StudyTask miniTask;
    Label lblSummary; ProgressBar bar; Panel chart; ListBox lbSubjects; NumericUpDown numGoal; Label lblRoot; TextBox txtSearch; ListView lvFiles;
    string[] quotes = { "Small steps every day add up.", "Read ahead, stay ahead.", "Future doctor in the making!", "Consistency beats cramming.", "One more page. You've got this!",
        "You're doing amazing, keep going!", "Glow up your grades, one page at a time.", "Rest is part of studying too." };

    static string Today() { return DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
    static string Clean(string s) { return (s ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " "); }

    public MainForm()
    {
        Text = "Study Tracker"; Width = 940; Height = 710; MinimumSize = new Size(840, 620); Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterScreen;
        dataFile = Path.Combine(App.DataDir, "study_data.txt"); bool dark = false; int pal = 0; Load_(ref dark, ref pal); Theme.Apply(dark, pal);
        try { Icon = Icon.FromHandle(Theme.Logo(64).GetHicon()); } catch { }
        content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) }; Controls.Add(content);
        for (int i = 0; i < 4; i++) { pages[i] = new Panel { Dock = DockStyle.Fill }; content.Controls.Add(pages[i]); }
        var navBar = new Panel { Dock = DockStyle.Top, Height = 48 }; string[] names = { "Study Plan", "Stats", "Find Files", "About" };
        for (int i = 0; i < 4; i++) { int k = i; nav[i] = Theme.Btn(names[i], 16 + i * 136, 8, 128, "nav"); nav[i].Height = 34; nav[i].Click += delegate { ShowPage(k); }; navBar.Controls.Add(nav[i]); }
        Controls.Add(navBar);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, Tag = "line" });
        BuildBanner(); BuildPlan(pages[0]); BuildStats(pages[1]); BuildFiles(pages[2]); BuildAbout(pages[3]);
        loading = false; Recolor(); RefreshTasks(); RefreshStats(); Scan(); Greet(); ShowPage(0);
        Shown += delegate { if (userName == "") { var n = Prompt("Welcome!", "Hi! What's your name?", ""); userName = string.IsNullOrWhiteSpace(n) ? "Student" : n.Trim(); Save(); Greet(); } };
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); DarkBar(); }
    void DarkBar() { try { int v = Theme.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref v, 4); DwmSetWindowAttribute(Handle, 19, ref v, 4); } catch { } }

    void Recolor()
    {
        BackColor = Theme.Bg; Theme.Skin(this); pic.Image = Theme.Logo(54);
        bDark.Text = Theme.Dark ? "Light mode" : "Dark mode"; bPal.Text = "Colors: " + Theme.PalNames[Theme.Pal];
        banner.Invalidate(); if (chart != null) chart.Invalidate(); RefreshTasks(); ShowPage(cur); DarkBar(); Save();
    }
    void ShowPage(int i)
    {
        cur = i;
        for (int j = 0; j < 4; j++) { pages[j].Visible = j == i; nav[j].BackColor = j == i ? Theme.Accent : Theme.Card; nav[j].ForeColor = j == i ? Color.White : Theme.Text; }
        if (i == 1) RefreshStats(); if (i == 2) txtSearch.Focus();
    }

    // ---------- banner ----------
    void BuildBanner()
    {
        banner = new Panel { Dock = DockStyle.Top, Height = 84, Tag = "banner" };
        banner.Paint += (s, e) => { using (var br = new LinearGradientBrush(banner.ClientRectangle, Theme.Base, Theme.Accent, 0f)) e.Graphics.FillRectangle(br, banner.ClientRectangle); };
        pic = new PictureBox { Left = 16, Top = 14, Width = 54, Height = 54, BackColor = Color.Transparent };
        lblHello = new Label { Left = 82, Top = 10, Width = 520, Height = 34, ForeColor = Color.White, Tag = "white", Font = new Font("Segoe UI", 17, FontStyle.Bold) };
        lblQuote = new Label { Left = 84, Top = 48, Width = 520, Height = 24, ForeColor = Color.FromArgb(215, 245, 240), Tag = "white", Font = new Font("Segoe UI", 10, FontStyle.Italic) };
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
            var p = line.Split('\t'); int a, b;
            if (p[0] == "R" && p.Length > 1) root = p[1];
            else if (p[0] == "N" && p.Length > 1) userName = p[1];
            else if (p[0] == "M" && p.Length > 1) muted = p[1] == "1";
            else if (p[0] == "D" && p.Length > 1) dark = p[1] == "1";
            else if (p[0] == "A" && p.Length > 1 && int.TryParse(p[1], out a)) pal = a;
            else if (p[0] == "G" && p.Length > 1 && int.TryParse(p[1], out a)) goal = a;
            else if (p[0] == "T" && p.Length > 3 && int.TryParse(p[2], out a)) tasks.Add(new StudyTask { Title = p[1], Target = a, Done = p[3] == "1" });
            else if (p[0] == "S" && p.Length > 4 && int.TryParse(p[3], out a) && int.TryParse(p[4], out b)) sessions.Add(new Sess { Date = p[1], Subject = p[2], Minutes = a, Pages = b });
        }
    }
    void Save()
    {
        if (loading) return;
        var l = new List<string> { "R\t" + root, "N\t" + Clean(userName), "M\t" + (muted ? "1" : "0"), "D\t" + (Theme.Dark ? "1" : "0"), "A\t" + Theme.Pal, "G\t" + goal };
        foreach (var t in tasks) l.Add("T\t" + Clean(t.Title) + "\t" + t.Target + "\t" + (t.Done ? "1" : "0"));
        foreach (var s in sessions) l.Add("S\t" + s.Date + "\t" + Clean(s.Subject) + "\t" + s.Minutes + "\t" + s.Pages);
        try { Directory.CreateDirectory(App.DataDir); File.WriteAllLines(dataFile, l.ToArray()); } catch (Exception e) { MessageBox.Show("Could not save: " + e.Message); }
    }
    int Studied(string title) { return sessions.Where(s => s.Subject == title).Sum(s => s.Minutes); }

    // ---------- plan ----------
    void BuildPlan(Control p)
    {
        lvTasks = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, GridLines = true };
        lvTasks.Columns.Add("What to study", 360); lvTasks.Columns.Add("Target", 90); lvTasks.Columns.Add("Studied", 90); lvTasks.Columns.Add("Progress", 90); lvTasks.Columns.Add("Status", 110);
        p.Controls.Add(lvTasks);
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 100 };
        var bLog = Theme.Btn("Log time", 0, 12, 110, "a"); var bDone = Theme.Btn("Toggle done", 118, 12, 120, "c"); var bDel = Theme.Btn("Delete", 246, 12, 90, "b");
        btnRead = Theme.Btn("Start reading timer", 0, 56, 200, "b"); btnRead.Height = 36;
        lblStatus = new Label { Left = 212, Top = 64, Width = 620, Height = 24, Tag = "muted", Text = "Pick a task, then start a timer. A small timer pops up at the side of your screen." };
        bottom.Controls.AddRange(new Control[] { bLog, bDone, bDel, btnRead, lblStatus }); p.Controls.Add(bottom);
        var top = new Panel { Dock = DockStyle.Top, Height = 50 };
        txtTitle = new TextBox { Left = 0, Top = 10, Width = 380, Font = new Font("Segoe UI", 11) };
        numTarget = new NumericUpDown { Left = 392, Top = 10, Width = 70, Minimum = 0, Maximum = 9999, Value = 60, Font = new Font("Segoe UI", 11) };
        var bAdd = Theme.Btn("Add task", 560, 8, 110, "a");
        top.Controls.AddRange(new Control[] { txtTitle, numTarget, new Label { Text = "min target", Left = 468, Top = 14, Width = 85 }, bAdd }); p.Controls.Add(top);
        bAdd.Click += delegate { AddTask(); };
        txtTitle.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { AddTask(); e.SuppressKeyPress = true; } };
        bLog.Click += delegate
        {
            var t = Selected(); if (t == null) return;
            var m = Prompt("Log time", "Minutes studied on \"" + t.Title + "\":", "30"); int mi;
            if (m == null || !int.TryParse(m, out mi) || mi < 1) return;
            var pg = Prompt("Pages", "Pages read (0 to skip):", "0"); int pgs; int.TryParse(pg ?? "0", out pgs); AddSession(t, mi, pgs);
        };
        bDone.Click += delegate { var t = Selected(); if (t == null) return; t.Done = !t.Done; Save(); RefreshTasks(); };
        bDel.Click += delegate { var t = Selected(); if (t == null) return; if (MessageBox.Show("Delete this task? Logged sessions are kept.", "Delete", MessageBoxButtons.YesNo) == DialogResult.Yes) { tasks.Remove(t); Save(); RefreshTasks(); } };
        btnRead.Click += delegate { StartReading(); };
    }
    void StartReading()
    {
        if (mini != null && !mini.IsDisposed) { mini.Show(); mini.TopMost = true; return; }
        var t = Selected(); if (t == null) return;
        var m = Prompt("Reading timer", "How many minutes will you read? (e.g. 25)", "25"); int mins;
        if (m == null || !int.TryParse(m, out mins) || mins < 1) return;
        miniTask = t; mini = new MiniTimer(t.Title, mins, muted);
        mini.Finished += (done, completed) =>
        {
            muted = mini.Muted; Save(); if (done >= 1) AddSession(miniTask, done, 0);
            lblStatus.Text = completed ? "Great job, " + userName + "! Logged " + done + " min on \"" + miniTask.Title + "\"." : (done >= 1 ? "Logged " + done + " min." : "Session ended (under 1 minute, not logged).");
            mini = null;
        };
        mini.Show(); lblStatus.Text = "Timer running on the side of your screen. Focus time!";
    }
    StudyTask Selected() { if (lvTasks.SelectedItems.Count == 0) { MessageBox.Show("Select a task first."); return null; } return (StudyTask)lvTasks.SelectedItems[0].Tag; }
    void AddTask()
    {
        var t = txtTitle.Text.Trim(); if (t == "") return;
        tasks.Add(new StudyTask { Title = t, Target = (int)numTarget.Value }); txtTitle.Text = ""; Save(); RefreshTasks();
    }
    void AddSession(StudyTask t, int mins, int pages)
    {
        sessions.Add(new Sess { Date = Today(), Subject = t.Title, Minutes = mins, Pages = pages });
        if (t.Target > 0 && Studied(t.Title) >= t.Target) t.Done = true;
        Save(); RefreshTasks(); RefreshStats();
    }
    void RefreshTasks()
    {
        lvTasks.Items.Clear();
        foreach (var t in tasks)
        {
            int st = Studied(t.Title); string pr = t.Target > 0 ? Math.Min(100, st * 100 / t.Target) + "%" : "-";
            var it = new ListViewItem(new string[] { t.Title, t.Target + " min", st + " min", pr, t.Done ? "Done" : "In progress" });
            it.Tag = t; it.ForeColor = t.Done ? Theme.Accent : Theme.Text; lvTasks.Items.Add(it);
        }
    }

    // ---------- stats ----------
    void BuildStats(Control p)
    {
        lbSubjects = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 10) }; p.Controls.Add(lbSubjects);
        chart = new Panel { Dock = DockStyle.Top, Height = 200, Tag = "card", BorderStyle = BorderStyle.FixedSingle }; chart.Paint += DrawChart; p.Controls.Add(chart);
        bar = new ProgressBar { Dock = DockStyle.Top, Height = 22, Maximum = 100 }; p.Controls.Add(bar);
        var g = new Panel { Dock = DockStyle.Top, Height = 40 };
        numGoal = new NumericUpDown { Left = 190, Top = 6, Width = 80, Minimum = 5, Maximum = 1440, Value = Math.Min(1440, Math.Max(5, goal)) };
        numGoal.ValueChanged += delegate { goal = (int)numGoal.Value; Save(); RefreshStats(); };
        g.Controls.AddRange(new Control[] { new Label { Left = 0, Top = 10, Width = 185, Text = "Daily goal (minutes):" }, numGoal }); p.Controls.Add(g);
        lblSummary = new Label { Dock = DockStyle.Top, Height = 40, Font = new Font("Segoe UI", 13, FontStyle.Bold), Tag = "title", TextAlign = ContentAlignment.MiddleLeft }; p.Controls.Add(lblSummary);
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
        int pages = sessions.Where(s => s.Date == Today()).Sum(s => s.Pages); int streak = 0, v; var d = DateTime.Today;
        if (!by.TryGetValue(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out v) || v == 0) d = d.AddDays(-1);
        while (by.TryGetValue(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out v) && v > 0) { streak++; d = d.AddDays(-1); }
        lblSummary.Text = "Today: " + td + " min, " + pages + " pages   |   Streak: " + streak + " day(s)" + (td >= goal ? "   Goal reached!" : "");
        bar.Value = Math.Min(100, td * 100 / Math.Max(1, goal)); chart.Invalidate();
        lbSubjects.Items.Clear(); lbSubjects.Items.Add("Total time per subject"); lbSubjects.Items.Add(new string('-', 30));
        foreach (var g in sessions.GroupBy(s => s.Subject).OrderByDescending(g => g.Sum(s => s.Minutes))) lbSubjects.Items.Add(g.Sum(s => s.Minutes).ToString().PadLeft(6) + " min   " + g.Key);
    }
    void DrawChart(object sender, PaintEventArgs e)
    {
        var by = ByDay(); var gr = e.Graphics; int max = 1; var vals = new int[7]; var days = new DateTime[7];
        for (int i = 0; i < 7; i++) { days[i] = DateTime.Today.AddDays(i - 6); int v; by.TryGetValue(days[i].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out v); vals[i] = v; max = Math.Max(max, v); }
        max = Math.Max(max, goal); int w = Math.Max(30, (chart.Width - 60) / 7 - 12); int gy = 150 - 120 * goal / max;
        using (var pen = new Pen(Theme.Accent3, 2) { DashStyle = DashStyle.Dash }) gr.DrawLine(pen, 10, gy, chart.Width - 10, gy);
        Color[] cs = { Theme.Accent, Theme.Accent2, Theme.Accent3 };
        for (int i = 0; i < 7; i++)
        {
            int x = 30 + i * (w + 12), h = 120 * vals[i] / max;
            using (var br = new SolidBrush(vals[i] >= goal ? Color.SeaGreen : cs[i % 3])) gr.FillRectangle(br, x, 150 - h, w, h);
            TextRenderer.DrawText(gr, vals[i] + "m", Font, new Point(x, 150 - h - 22), Theme.Text);
            TextRenderer.DrawText(gr, days[i].ToString("ddd d", CultureInfo.CurrentCulture), Font, new Point(x, 160), Theme.Muted);
        }
    }

    // ---------- files ----------
    void BuildFiles(Control p)
    {
        lvFiles = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
        lvFiles.Columns.Add("File", 340); lvFiles.Columns.Add("Folder", 480); p.Controls.Add(lvFiles);
        txtSearch = new TextBox { Dock = DockStyle.Top, Font = new Font("Segoe UI", 14) }; p.Controls.Add(txtSearch);
        var top = new Panel { Dock = DockStyle.Top, Height = 46 };
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
        lvFiles.ContextMenuStrip = menu;
    }
    void Walk(string dir)
    {
        try { foreach (var f in Directory.GetFiles(dir)) { var n = Path.GetFileName(f); index.Add(new string[] { n.ToLower(), n, f }); } } catch { }
        try { foreach (var d in Directory.GetDirectories(dir)) Walk(d); } catch { }
    }
    void Scan()
    {
        index.Clear();
        if (root == "" || !Directory.Exists(root)) { lblRoot.Text = "No root folder selected"; return; }
        Cursor = Cursors.WaitCursor; Walk(root); Cursor = Cursors.Default; lblRoot.Text = root + "  (" + index.Count + " files)"; Search();
    }
    void Search()
    {
        var words = txtSearch.Text.ToLower().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        lvFiles.BeginUpdate(); lvFiles.Items.Clear(); int n = 0;
        foreach (var e in index) if (words.All(w => e[0].Contains(w))) { var it = new ListViewItem(new string[] { e[1], Path.GetDirectoryName(e[2]) }); it.Tag = e[2]; lvFiles.Items.Add(it); if (++n >= 300) break; }
        lvFiles.EndUpdate();
    }
    void OpenFirst()
    {
        ListViewItem it = lvFiles.SelectedItems.Count > 0 ? lvFiles.SelectedItems[0] : (lvFiles.Items.Count > 0 ? lvFiles.Items[0] : null);
        if (it == null) return;
        try { Process.Start(new ProcessStartInfo((string)it.Tag) { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(ex.Message); }
    }

    // ---------- about ----------
    void BuildAbout(Control p)
    {
        var pb2 = new PictureBox { Left = 10, Top = 10, Width = 130, Height = 130, Image = Theme.Logo(130), BackColor = Color.Transparent };
        var title = new Label { Left = 160, Top = 12, Width = 600, Height = 46, Text = "Study Tracker", Font = new Font("Segoe UI", 26, FontStyle.Bold), Tag = "title" };
        var by = new Label { Left = 163, Top = 62, Width = 600, Height = 30, Text = "Created by 3Mkay Derado", Font = new Font("Segoe UI", 14, FontStyle.Bold), Tag = "sub" };
        var desc = new Label { Left = 163, Top = 98, Width = 640, Height = 50, Text = "A free, offline study companion: plan what to read, track your time with a pop-up timer, see your streaks, and find any note or book in seconds. Version 3.0" };
        var med = new Label { Left = 10, Top = 170, Width = 780, Height = 26, Text = "Need medical lecture notes? Visit MedEduNotes:", Font = new Font("Segoe UI", 11, FontStyle.Bold) };
        var link = new LinkLabel { Left = 10, Top = 198, Width = 600, Height = 26, Text = "https://med-edu-notes.vercel.app/", Font = new Font("Segoe UI", 11) };
        link.LinkClicked += delegate { try { Process.Start(new ProcessStartInfo("https://med-edu-notes.vercel.app/") { UseShellExecute = true }); } catch { } };
        var tip = new Label { Left = 10, Top = 240, Width = 800, Height = 44, Tag = "muted", Text = "Tip: put a file named logo.png next to the app to replace the built-in logo. Your data stays on this computer (in your user folder) and never leaves it." };
        lblInst = new Label { Left = 10, Top = 296, Width = 800, Height = 24, Tag = "muted", Text = "Installed at: " + (Installer.RunningInstalled() ? App.ExeDir : "not installed (running portable)") };
        var bun = Theme.Btn("Uninstall Study Tracker", 10, 330, 220, "b"); bun.Height = 36;
        bun.Click += delegate { if (Installer.Uninstall(this)) { loading = true; Environment.Exit(0); } };
        p.Controls.AddRange(new Control[] { pb2, title, by, desc, med, link, tip, lblInst, bun });
    }

    string Prompt(string title, string label, string def)
    {
        using (var f = new Form())
        {
            f.Text = title; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.StartPosition = FormStartPosition.CenterParent; f.ClientSize = new Size(390, 120); f.MaximizeBox = false; f.MinimizeBox = false; f.BackColor = Theme.Bg;
            var l = new Label { Text = label, Left = 14, Top = 12, Width = 360 }; var t = new TextBox { Left = 14, Top = 44, Width = 360, Text = def };
            var ok = Theme.Btn("OK", 294, 78, 80, "a"); ok.Height = 30; ok.DialogResult = DialogResult.OK;
            f.Controls.AddRange(new Control[] { l, t, ok }); f.AcceptButton = ok; Theme.Skin(f);
            return f.ShowDialog(this) == DialogResult.OK ? t.Text : null;
        }
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles(); Theme.Apply(false, 0);
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
