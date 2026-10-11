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

class StudyTask { public string Title; public int Target; public bool Done; }
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
            else if (k is CheckBox || k is RadioButton) { k.ForeColor = Text; k.BackColor = Color.Transparent; }
            else if (k is TextBox) { k.BackColor = Card; k.ForeColor = Text; ((TextBox)k).BorderStyle = tag == "flat" ? BorderStyle.None : BorderStyle.FixedSingle; }
            else if (k is NumericUpDown) { k.BackColor = Card; k.ForeColor = Text; }
            else if (k is ComboBox) { var cb = (ComboBox)k; cb.BackColor = Card; cb.ForeColor = Text; cb.FlatStyle = FlatStyle.Flat; }
            else if (k is ListView) { k.BackColor = Card; k.ForeColor = Text; ((ListView)k).BorderStyle = BorderStyle.None; }
            else if (k is ListBox) { k.BackColor = Card; k.ForeColor = Text; ((ListBox)k).BorderStyle = tag == "flat" ? BorderStyle.None : BorderStyle.FixedSingle; }
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

// ===== Rounded, bordered card panel (site-style) =====
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
    public const string Version = "5.3";
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

// ===== Pop-up timer: rounded, draggable, compact dashboard =====
class MiniTimer : Form
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, int w, int l);
    public event Action<int, bool> Finished;
    int total, left, elapsed; bool paused, ended; public bool Muted;
    Label lbl, title; Panel clock; Button bPause, bPin, bMute, bStop;
    Timer t = new Timer { Interval = 1000 };
    public MiniTimer(string task, int minutes, bool muted)
    {
        Muted = muted; total = left = Math.Max(1, minutes) * 60;
        FormBorderStyle = FormBorderStyle.Sizable; ShowInTaskbar = false; TopMost = true; Text = "Timer";
        ClientSize = new Size(350, 205); MinimumSize = new Size(300, 195);
        try { Icon = Icon.FromHandle(Theme.Logo(32).GetHicon()); } catch { }
        BackColor = Color.FromArgb(15, 20, 29); Font = new Font("Segoe UI", 9);
        var wa = Screen.PrimaryScreen.WorkingArea; StartPosition = FormStartPosition.Manual; Location = new Point(wa.Right - Width - 14, wa.Top + 90);
        Shown += delegate { try { int darkCaption = 1; DwmSetWindowAttribute(Handle, 20, ref darkCaption, 4); DwmSetWindowAttribute(Handle, 19, ref darkCaption, 4); } catch { } };
        title = new Label { Text = "Timer", ForeColor = Color.White, Font = new Font("Segoe UI", 13, FontStyle.Bold) };
        clock = new Panel { BackColor = BackColor }; clock.Paint += DrawClock;
        lbl = new Label { TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Segoe UI", 16, FontStyle.Bold), BackColor = Color.Transparent };
        clock.Controls.Add(lbl);
        bPause = Mk("Ⅱ  Pause", 0, 0, 0, 0, Color.FromArgb(255, 100, 35));
        bStop = Mk("Finish", 0, 0, 0, 0, Color.FromArgb(36, 46, 62));
        bPin = Mk("Pin", 0, 0, 0, 0, Color.FromArgb(36, 46, 62));
        bMute = Mk(Muted ? "Muted" : "Sound", 0, 0, 0, 0, Color.FromArgb(36, 46, 62));
        Controls.AddRange(new Control[] { title, clock, bPause, bStop, bPin, bMute });
        Resize += delegate { LayoutTimer(); }; LayoutTimer();
        MouseDown += Drag; title.MouseDown += Drag; clock.MouseDown += Drag; lbl.MouseDown += Drag;
        Show2();
        t.Tick += delegate { if (paused || ended) return; left--; elapsed++; Show2(); if (left <= 0) Done(true); };
        t.Start();
        bPause.Click += delegate { paused = !paused; bPause.Text = paused ? "▶  Start" : "Ⅱ  Pause"; };
        bPin.Click += delegate { TopMost = !TopMost; bPin.Text = TopMost ? "Pinned" : "Pin"; bPin.BackColor = TopMost ? Color.FromArgb(87, 57, 174) : Color.FromArgb(36, 46, 62); };
        bMute.Click += delegate { Muted = !Muted; bMute.Text = Muted ? "Muted" : "Sound"; };
        bStop.Click += delegate { Done(false); };
        FormClosing += delegate { if (!ended) { ended = true; t.Stop(); if (Finished != null) Finished((int)Math.Round(elapsed / 60.0), false); } };
    }
    void LayoutTimer()
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        title.SetBounds(14, 5, w - 28, 24);
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
    void Drag(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); } }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = Theme.Round(new Rectangle(1, 1, Width - 3, Height - 3), 18)) using (var pen = new Pen(Color.FromArgb(39, 49, 66), 1)) e.Graphics.DrawPath(pen, p);
    }
    Button Mk(string s, int x, int y, int w, int h, Color c)
    {
        var b = new Button { Text = s, Left = x, Top = y, Width = w, Height = h, FlatStyle = FlatStyle.Flat, BackColor = c, ForeColor = Color.White, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
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
        lbl = new Label { Top = 7, Height = 22, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
        Controls.AddRange(new Control[] { bm, bp, lbl });
        bm.Click += delegate { Value = val - Step; }; bp.Click += delegate { Value = val + Step; };
        Resize += delegate { Lay(); }; Lay(); Value = val;
    }
    void Lay() { bm.Left = 3; bp.Left = Width - 33; lbl.Left = 34; lbl.Width = Math.Max(20, Width - 68); }
    public int Value { get { return val; } set { val = Math.Max(Min, Math.Min(Max, value)); lbl.Text = val + " " + suf; } }
    public string Suffix { get { return suf; } set { suf = value; lbl.Text = val + " " + suf; } }
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
        ClientSize = new Size(440, h); BackColor = Theme.Bg; Font = new Font("Segoe UI", 10); Text = title;
        var head = new Panel { Dock = DockStyle.Top, Height = 70, Tag = "banner" };
        head.Paint += (s, e) => { using (var gb = new LinearGradientBrush(head.ClientRectangle, Theme.Base, Theme.Accent, 0f)) e.Graphics.FillRectangle(gb, head.ClientRectangle); };
        head.Controls.Add(new Label { Left = 22, Top = 10, Width = 396, Height = 28, Text = title, Tag = "white", ForeColor = Color.White, Font = new Font("Segoe UI", 15, FontStyle.Bold) });
        head.Controls.Add(new Label { Left = 24, Top = 40, Width = 396, Height = 22, Text = sub, Tag = "white", ForeColor = Color.FromArgb(215, 245, 240), AutoEllipsis = true });
        Controls.Add(head);
    }
    void Buttons(int h)
    {
        var ok = Theme.Btn("Save", 316, h - 56, 100, "a"); ok.Height = 36; ok.DialogResult = DialogResult.OK;
        var no = Theme.Btn("Cancel", 206, h - 56, 100, "c"); no.Height = 36; no.DialogResult = DialogResult.Cancel;
        Controls.Add(ok); Controls.Add(no); AcceptButton = ok; CancelButton = no; Theme.Skin(this);
    }
    public AskForm(string title, string label, string def)   // text answer
    {
        Init(title, "", 236);
        Controls.Add(new Label { Left = 24, Top = 86, Width = 392, Height = 22, Text = label, Tag = "muted" });
        var card = new Card { Left = 24, Top = 112, Width = 392, Height = 48 };
        tb = new TextBox { Left = 14, Top = 11, Width = 364, Text = def, Tag = "flat", Font = new Font("Segoe UI", 13) };
        card.Controls.Add(tb); Controls.Add(card); Buttons(236);
        Shown += delegate { tb.Focus(); tb.SelectAll(); };
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
        Buttons(h);
    }
}

// ===== One study task shown as a rounded, bordered card =====
class TaskCard : Card
{
    public StudyTask T; public int Studied; public bool Selected; public Action<TaskCard> OnSelect, OnToggle;
    static Font fT = new Font("Segoe UI", 11.5f, FontStyle.Bold), fS = new Font("Segoe UI", 9f);
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (new Rectangle(12, 12, 38, 38).Contains(e.Location)) { if (OnToggle != null) OnToggle(this); } else if (OnSelect != null) OnSelect(this);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Selected) using (var gp = Theme.Round(new Rectangle(1, 1, Width - 3, Height - 3), 16)) using (var pen = new Pen(Theme.Accent, 2f)) g.DrawPath(pen, gp);
        var c = new Rectangle(18, 18, 24, 24);
        if (T.Done) { using (var br = new SolidBrush(Theme.Accent)) g.FillEllipse(br, c); using (var pen = new Pen(Color.White, 2.5f)) g.DrawLines(pen, new Point[] { new Point(24, 30), new Point(29, 35), new Point(37, 24) }); }
        else using (var pen = new Pen(Theme.Border, 2f)) g.DrawEllipse(pen, c);
        int tw = Math.Max(60, Width - 300);
        var fl = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
        TextRenderer.DrawText(g, T.Title, fT, new Rectangle(56, 10, tw, 26), T.Done ? Theme.Muted : Theme.Text, fl);
        TextRenderer.DrawText(g, "Studied " + Studied + " of " + T.Target + " min" + (T.Done ? "  -  completed" : ""), fS, new Rectangle(56, 36, tw, 20), Theme.Muted, fl);
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
    string dataFile, root = "", userName = ""; int goal = 60, cur = 0, homeFilter = 0; float uiFontSize = 10f; string uiFontFamily = "Segoe UI"; bool loading = true, muted = false, onboardingDone = false, minimizeDuringTimer = true;
    List<StudyTask> tasks = new List<StudyTask>(); List<Sess> sessions = new List<Sess>(); List<string[]> index = new List<string[]>();
    Panel banner, content; Panel[] pages = new Panel[PAGES]; Button[] nav = new Button[PAGES]; PictureBox pic; Button bDark, bPal;
    bool updateAvailable, blink, busy, autoMode = true; DateTime stamp = DateTime.MinValue, lastCheck = DateTime.MinValue, latest = DateTime.MinValue;
    Timer dotTimer, hourTimer; Label lblVer, lblUpdInfo, lblUpdStatus; Button bCheck, bInstall; ProgressBar prog; RadioButton rbAuto, rbManual;
    TextBox txtTitle; Stepper numTarget; FlowLayoutPanel taskFlow, homeFlow; Label homeSummary; Button[] homeFilters = new Button[3]; StudyTask selTask; Label lblPlanSummary; Label lblDayInfo; ListBox lbDay; DateTime selDay = DateTime.Today; Label lblStatus, lblHello, lblQuote, lblInst; Button btnRead; MiniTimer mini; StudyTask miniTask;
    Label lblSummary; ProgressBar bar; Panel chart; ListBox lbSubjects; NumericUpDown numGoal; Label lblRoot; TextBox txtSearch; ListView lvFiles;
    // focus timer (Pomodoro)
    Panel ring; Label lblRingTime, lblRingMode, lblFocusStatus; Button bStart, bReset, bSound; NumericUpDown numFocusMin; ComboBox cbSub; CheckBox chkLog;
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
        Text = "Study Tracker"; Width = 980; Height = 720; MinimumSize = new Size(880, 640); Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterScreen;
        dataFile = Path.Combine(App.DataDir, "study_data.txt"); bool dark = true; int pal = 0; Load_(ref dark, ref pal); Theme.Apply(dark, pal);
        viewMonth = DateTime.Today;
        try { Icon = Icon.FromHandle(Theme.Logo(64).GetHicon()); } catch { }
        content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14) }; Controls.Add(content);
        for (int i = 0; i < PAGES; i++) { pages[i] = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg }; content.Controls.Add(pages[i]); }
        var navBar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Bg };
        string[] names = { "Home", "Study Plan", "Focus Timer", "Stats", "Find Files", "Settings", "About" };
        for (int i = 0; i < PAGES; i++) { int k = i; nav[i] = Theme.Btn(names[i], 8 + i * 126, 8, 122, "nav"); nav[i].Height = 34; nav[i].Click += delegate { ShowPage(k); }; navBar.Controls.Add(nav[i]); }
        nav[PAGE_SETTINGS].Paint += NavDot; Controls.Add(navBar);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, Tag = "line" });
        BuildBanner(); BuildHome(pages[PAGE_HOME]); BuildPlan(pages[PAGE_PLAN]); BuildFocus(pages[PAGE_FOCUS]); BuildStats(pages[PAGE_STATS]); BuildFiles(pages[PAGE_FILES]); BuildSettings(pages[PAGE_SETTINGS]); BuildAbout(pages[PAGE_ABOUT]);
        if (stamp == DateTime.MinValue) stamp = DateTime.UtcNow; loading = false; Recolor(); RefreshTasks(); RefreshStats(); RefreshStreak(); Scan(); Greet(); ShowPage(0); StartTimers();
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
        };
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); DarkBar(); }
    void DarkBar() { try { int v = Theme.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref v, 4); DwmSetWindowAttribute(Handle, 19, ref v, 4); } catch { } }

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
            else if (p[0] == "T" && p.Length > 3 && int.TryParse(p[2], out a)) tasks.Add(new StudyTask { Title = p[1], Target = a, Done = p[3] == "1" });
            else if (p[0] == "S" && p.Length > 4 && int.TryParse(p[3], out a) && int.TryParse(p[4], out b)) sessions.Add(new Sess { Date = p[1], Subject = p[2], Minutes = a, Pages = b });
        }
    }
    void Save()
    {
        if (loading) return;
        var l = new List<string> { "R\t" + root, "N\t" + Clean(userName), "M\t" + (muted ? "1" : "0"), "D\t" + (Theme.Dark ? "1" : "0"), "A\t" + Theme.Pal, "G\t" + goal, "F\t" + focusCount, "U\t" + stamp.Ticks, "K\t" + lastCheck.Ticks, "L\t" + latest.Ticks, "Z\t" + (autoMode ? "1" : "0"), "Q\t" + uiFontSize.ToString(CultureInfo.InvariantCulture), "V\t" + uiFontFamily, "W\t" + (onboardingDone ? "1" : "0"), "X\t" + (minimizeDuringTimer ? "1" : "0") };
        foreach (var t in tasks) l.Add("T\t" + Clean(t.Title) + "\t" + t.Target + "\t" + (t.Done ? "1" : "0"));
        foreach (var s in sessions) l.Add("S\t" + s.Date + "\t" + Clean(s.Subject) + "\t" + s.Minutes + "\t" + s.Pages);
        try { Directory.CreateDirectory(App.DataDir); File.WriteAllLines(dataFile, l.ToArray()); } catch (Exception e) { MessageBox.Show("Could not save: " + e.Message); }
    }
    int Studied(string title) { return sessions.Where(s => s.Subject == title).Sum(s => s.Minutes); }

    // ---------- home: task overview ----------
    void BuildHome(Control p)
    {
        var title = new Label { Left = 10, Top = 6, Width = 600, Height = 42, Text = "My Tasks", Font = new Font("Segoe UI", 22, FontStyle.Bold), Tag = "title" };
        homeSummary = new Label { Left = 12, Top = 52, Width = 800, Height = 28, Tag = "muted", Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        string[] labels = { "All tasks", "To do", "Completed" };
        for (int i = 0; i < 3; i++) { int k = i; homeFilters[i] = Theme.Btn(labels[i], 12 + i * 132, 88, 124, "c"); homeFilters[i].Click += delegate { homeFilter = k; RefreshHome(); }; p.Controls.Add(homeFilters[i]); }
        homeFlow = new FlowLayoutPanel { Left = 10, Top = 132, Width = 850, Height = 500, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        p.Controls.AddRange(new Control[] { title, homeSummary, homeFlow });
        p.Resize += delegate { homeFlow.Width = Math.Max(300, p.ClientSize.Width - 20); homeFlow.Height = Math.Max(120, p.ClientSize.Height - 142); RefreshHome(); };
    }
    void RefreshHome()
    {
        if (homeFlow == null) return;
        homeSummary.Text = tasks.Count + " total  •  " + tasks.Count(t => !t.Done) + " to do  •  " + tasks.Count(t => t.Done) + " completed";
        for (int i = 0; i < 3; i++) { homeFilters[i].BackColor = i == homeFilter ? Theme.Accent : Theme.Card; homeFilters[i].ForeColor = i == homeFilter ? Color.White : Theme.Text; }
        homeFlow.SuspendLayout(); foreach (Control c in homeFlow.Controls.Cast<Control>().ToList()) { homeFlow.Controls.Remove(c); c.Dispose(); }
        var list = tasks.Where(t => homeFilter == 0 || (homeFilter == 1 && !t.Done) || (homeFilter == 2 && t.Done)).ToList();
        if (list.Count == 0) homeFlow.Controls.Add(new Label { Width = Math.Max(280, homeFlow.ClientSize.Width - 30), Height = 80, TextAlign = ContentAlignment.MiddleCenter, Tag = "muted", Text = tasks.Count == 0 ? "No tasks yet. Add your first task in Study Plan." : "No tasks in this section." });
        foreach (var t in list)
        {
            var card = new Card { Width = Math.Max(280, homeFlow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12), Height = 62, Margin = new Padding(0, 0, 0, 8), Tag = "card" };
            var status = new CheckBox { Left = 12, Top = 18, Width = 22, Height = 24, Checked = t.Done, Text = "", Tag = "flat" };
            var name = new Label { Left = 42, Top = 8, Width = 420, Height = 24, Text = t.Title, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Tag = "title", AutoEllipsis = true };
            var detail = new Label { Left = 42, Top = 32, Width = 420, Height = 20, Text = (t.Done ? "Completed" : "To do") + "  •  " + Studied(t.Title) + " / " + t.Target + " min studied", Tag = "muted" };
            var open = Theme.Btn("Open", 0, 14, 74, "a"); open.Height = 32; open.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            card.Controls.AddRange(new Control[] { status, name, detail, open });
            Action layout = delegate { open.Left = card.ClientSize.Width - 88; name.Width = detail.Width = Math.Max(100, open.Left - 54); }; card.Resize += delegate { layout(); }; layout();
            status.CheckedChanged += delegate { t.Done = status.Checked; Save(); RefreshTasks(); RefreshHome(); };
            open.Click += delegate { selTask = t; ShowPage(PAGE_PLAN); RefreshTasks(); };
            homeFlow.Controls.Add(card);
        }
        homeFlow.ResumeLayout(); Theme.Skin(homeFlow);
    }

    // ---------- plan (task cards) ----------
    int CardW() { return Math.Max(320, taskFlow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6); }
    void BuildPlan(Control p)
    {
        taskFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        taskFlow.Resize += delegate { int w = CardW(); foreach (Control c in taskFlow.Controls) c.Width = w; };
        p.Controls.Add(taskFlow);
        lblStatus = new Label { Dock = DockStyle.Bottom, Height = 30, Tag = "muted", TextAlign = ContentAlignment.MiddleLeft, Text = "Click a card to select it (click empty space to deselect), or press Start for a pop-up reading timer." };
        p.Controls.Add(lblStatus);
        lblPlanSummary = new Label { Dock = DockStyle.Top, Height = 34, Tag = "muted", TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        p.Controls.Add(lblPlanSummary);
        var holder = new Panel { Dock = DockStyle.Top, Height = 68, Padding = new Padding(0, 0, 0, 12) };
        var bar2 = new Card { Dock = DockStyle.Fill };
        txtTitle = new TextBox { Left = 20, Top = 15, Width = 300, Font = new Font("Segoe UI", 12), Tag = "flat" };
        numTarget = new Stepper { Top = 10 };
                var bAdd = Theme.Btn("Add task", 0, 10, 110, "a"); bAdd.Height = 36;
        bar2.Controls.AddRange(new Control[] { txtTitle, numTarget, bAdd });
        Action lay = delegate { int w = bar2.Width; bAdd.Left = w - 126; numTarget.Left = w - 262; txtTitle.Width = Math.Max(100, w - 262 - 34); };
        bar2.Resize += delegate { lay(); }; lay();
        holder.Controls.Add(bar2); p.Controls.Add(holder);
        foreach (Control c0 in new Control[] { taskFlow, p, holder, bar2, lblPlanSummary, lblStatus }) c0.MouseDown += delegate { ClearSel(); };
        txtTitle.HandleCreated += delegate { SendMessage(txtTitle.Handle, 0x1501, 1, "What are you studying today?"); };
        bAdd.Click += delegate { AddTask(); };
        txtTitle.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { AddTask(); e.SuppressKeyPress = true; } };
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
        c.Controls.AddRange(new Control[] { bs, bl, bx });
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
        tasks.Add(new StudyTask { Title = t, Target = (int)numTarget.Value }); txtTitle.Text = ""; Save(); RefreshTasks(); FillCombo();
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
        taskFlow.ResumeLayout(); Theme.Skin(taskFlow);
        lblPlanSummary.Text = tasks.Count + " task(s)   |   " + tasks.Count(x => x.Done) + " done   |   " + sessions.Where(x => x.Date == Today()).Sum(x => x.Minutes) + " min studied today";
        FillCombo(); RefreshHome();
    }

    // ---------- focus timer (Pomodoro) ----------
    void BuildFocus(Control p)
    {
        var title = new Label { Left = 10, Top = 6, Width = 400, Height = 44, Text = "Focus Timer", Font = new Font("Segoe UI", 22, FontStyle.Bold), Tag = "title" };
        var sub = new Label { Left = 12, Top = 52, Width = 500, Height = 24, Tag = "muted", Text = "Pomodoro-style blocks. Deep Focus 25 · Short Break 5 · Long Break 15." };
        ring = new Card { Left = 30, Top = 90, Width = 280, Height = 280 }; ring.Paint += DrawRing;
        // clock labels live INSIDE the ring card and are positioned relative to it, centered
        lblRingMode = new Label { Left = 0, Top = 106, Width = ring.Width, Height = 26, TextAlign = ContentAlignment.MiddleCenter, Tag = "sub", Font = new Font("Segoe UI", 11, FontStyle.Bold), BackColor = Color.Transparent };
        lblRingTime = new Label { Left = 0, Top = 134, Width = ring.Width, Height = 60, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Text, Font = new Font("Consolas", 30, FontStyle.Bold), BackColor = Color.Transparent };
        lblRingMode.Parent = ring; lblRingTime.Parent = ring;
        var right = new Panel { Left = 340, Top = 90, Width = 580, Height = 300, BackColor = Color.Transparent };
        bFocus = Theme.Btn("Deep Focus · 25", 0, 0, 180, "a"); bShort = Theme.Btn("Short Break · 5", 188, 0, 180, "c"); bLong = Theme.Btn("Long Break · 15", 376, 0, 180, "b");
        bFocus.Height = bShort.Height = bLong.Height = 36;
        var lblCustom = new Label { Left = 0, Top = 52, Width = 160, Text = "Custom minutes:", Tag = "muted" };
        numFocusMin = new NumericUpDown { Left = 160, Top = 48, Width = 70, Minimum = 1, Maximum = 180, Value = 25, Font = new Font("Segoe UI", 11) };
        var bSet = Theme.Btn("Set", 238, 46, 70, "c");
        bStart = Theme.Btn("Start", 0, 92, 180, "a"); bStart.Height = 44; bStart.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        bReset = Theme.Btn("Reset", 188, 96, 120, "b"); bReset.Height = 40;
        bSound = Theme.Btn(muted ? "Sound: Off" : "Sound: On", 316, 96, 120, "c"); bSound.Height = 40;
        bSound.Click += delegate { muted = !muted; bSound.Text = muted ? "Sound: Off" : "Sound: On"; Save(); };
        chkLog = new CheckBox { Left = 0, Top = 150, Width = 400, Text = "Auto-log completed focus blocks to a subject", Checked = true };
        var lblSub = new Label { Left = 0, Top = 182, Width = 160, Text = "Log focus time to:", Tag = "muted" };
        cbSub = new ComboBox { Left = 160, Top = 178, Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 10) };
        lblFocusStatus = new Label { Left = 0, Top = 220, Width = 560, Height = 44, Tag = "muted", Text = "Completed focus blocks: 0" };
        right.Controls.AddRange(new Control[] { bFocus, bShort, bLong, lblCustom, numFocusMin, bSet, bStart, bReset, bSound, chkLog, lblSub, cbSub, lblFocusStatus });
        p.Controls.AddRange(new Control[] { title, sub, ring, right });
        bFocus.Click += delegate { SetMode("focus", 25); };
        bShort.Click += delegate { SetMode("short", 5); };
        bLong.Click += delegate { SetMode("long", 15); };
        bSet.Click += delegate { SetMode("focus", (int)numFocusMin.Value); };
        numFocusMin.ValueChanged += delegate { if (!frunning && fmode == "focus") SetMode("focus", (int)numFocusMin.Value); };
        bStart.Click += delegate { ToggleRun(); };
        bReset.Click += delegate { frunning = false; fleft = ftotal; bStart.Text = "Start"; UpdateRing(); };
        fTimer = new Timer { Interval = 1000 };
        fTimer.Tick += delegate
        {
            if (!frunning) return;
            fleft--; UpdateRing(); if (fleft > 0 && fleft <= 5 && !muted) System.Media.SystemSounds.Beep.Play();
            if (fleft <= 0) FocusDone();
        };
        fTimer.Start(); // FIX: the timer must actually run; it no-ops while frunning == false
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
        ftotal = fleft = mins * 60; if (m == "focus") numFocusMin.Value = Math.Max(numFocusMin.Minimum, Math.Min(numFocusMin.Maximum, mins));
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
        lbSubjects = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 10) }; left.Controls.Add(lbSubjects);
        chart = new Card { Dock = DockStyle.Top, Height = 210 }; chart.Paint += DrawChart; left.Controls.Add(chart);
        bar = new ProgressBar { Dock = DockStyle.Top, Height = 22, Maximum = 100 }; left.Controls.Add(bar);
        var g = new Panel { Dock = DockStyle.Top, Height = 40 };
        numGoal = new NumericUpDown { Left = 190, Top = 6, Width = 80, Minimum = 5, Maximum = 1440, Value = Math.Min(1440, Math.Max(5, goal)) };
        numGoal.ValueChanged += delegate { goal = (int)numGoal.Value; Save(); RefreshStats(); RefreshStreak(); };
        g.Controls.AddRange(new Control[] { new Label { Left = 0, Top = 10, Width = 185, Text = "Daily goal (minutes):" }, numGoal }); left.Controls.Add(g);
        lblSummary = new Label { Dock = DockStyle.Top, Height = 36, Font = new Font("Segoe UI", 11.5f, FontStyle.Bold), Tag = "title", TextAlign = ContentAlignment.MiddleLeft }; left.Controls.Add(lblSummary);
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
        lbDay = new ListBox { Dock = DockStyle.Fill, Tag = "flat", Font = new Font("Segoe UI", 10) };
        lblDayInfo = new Label { Dock = DockStyle.Top, Height = 44, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Tag = "title" };
        detail.Controls.Add(lbDay); detail.Controls.Add(lblDayInfo); p.Controls.Add(detail);
        p.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });
        var nav2 = new Panel { Dock = DockStyle.Top, Height = 42 };
        bPrev = Theme.Btn("<", 0, 5, 44, "c"); bNext = Theme.Btn(">", 50, 5, 44, "c"); bThisMonth = Theme.Btn("This month", 100, 5, 110, "b");
        nav2.Controls.AddRange(new Control[] { bPrev, bNext, bThisMonth, new Label { Left = 222, Top = 12, Width = 200, Height = 22, Tag = "muted", Text = "Green = daily goal reached" } });
        p.Controls.Add(nav2);
        lblStreak = new Label { Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI", 10, FontStyle.Bold), Tag = "sub" }; p.Controls.Add(lblStreak);
        p.Controls.Add(new Label { Dock = DockStyle.Top, Height = 32, Text = "Streaks", Font = new Font("Segoe UI", 15, FontStyle.Bold), Tag = "title" });
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
        TextRenderer.DrawText(g, viewMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture), new Font("Segoe UI", 13, FontStyle.Bold), new Point(16, 12), Theme.Text);
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
            TextRenderer.DrawText(g, day.ToString(), new Font("Segoe UI", 9, FontStyle.Bold), r, v > 0 ? Color.White : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // ---------- files ----------
    void BuildFiles(Control p)
    {
        lvFiles = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
        lvFiles.Columns.Add("File", 340); lvFiles.Columns.Add("Folder", 480); p.Controls.Add(lvFiles);
        txtSearch = new TextBox { Dock = DockStyle.Top, Font = new Font("Segoe UI", 14) }; p.Controls.Add(txtSearch);
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

    void SetFontFamily(Control parent, string family)
    {
        foreach (Control c in parent.Controls) { try { c.Font = new Font(family, c.Font.Size, c.Font.Style); } catch { } if (c.Controls.Count > 0) SetFontFamily(c, family); }
    }
    void ScaleFonts(Control parent, float ratio)
    {
        foreach (Control c in parent.Controls)
        {
            try { c.Font = new Font(c.Font.FontFamily, Math.Max(8f, Math.Min(24f, c.Font.Size * ratio)), c.Font.Style); } catch { }
            if (c.Controls.Count > 0) ScaleFonts(c, ratio);
        }
    }

    // ---------- about ----------
    void BuildAbout(Control p)
    {
        var pb2 = new PictureBox { Left = 10, Top = 10, Width = 130, Height = 130, Image = Theme.Logo(130), BackColor = Color.Transparent };
        var title = new Label { Left = 160, Top = 12, Width = 600, Height = 46, Text = "Study Tracker", Font = new Font("Segoe UI", 26, FontStyle.Bold), Tag = "title" };
        var by = new Label { Left = 163, Top = 62, Width = 600, Height = 30, Text = "Created by 3Mkay Derado", Font = new Font("Segoe UI", 14, FontStyle.Bold), Tag = "sub" };
        var desc = new Label { Left = 163, Top = 98, Width = 640, Height = 50, Text = "A free, offline study companion: plan what to read, run Pomodoro focus blocks, track your streaks, and find any note or book in seconds. Version " + Updater.Version };
        var med = new Label { Left = 10, Top = 170, Width = 780, Height = 26, Text = "Need medical lecture notes? Visit MedEduNotes:", Font = new Font("Segoe UI", 11, FontStyle.Bold) };
        var link = new LinkLabel { Left = 10, Top = 198, Width = 600, Height = 26, Text = "https://med-edu-notes.vercel.app/", Font = new Font("Segoe UI", 11) };
        link.LinkClicked += delegate { try { Process.Start(new ProcessStartInfo("https://med-edu-notes.vercel.app/") { UseShellExecute = true }); } catch { } };
        var contactTitle = new Label { Left = 10, Top = 240, Width = 800, Height = 26, Tag = "sub", Font = new Font("Segoe UI", 11, FontStyle.Bold), Text = "Contact me" };
        var contact = new LinkLabel { Left = 10, Top = 268, Width = 600, Height = 26, Text = "kjnrmbiriri1+studytracker@gmail.com", Font = new Font("Segoe UI", 10) };
        contact.LinkClicked += delegate { try { Process.Start(new ProcessStartInfo("mailto:kjnrmbiriri1+studytracker@gmail.com") { UseShellExecute = true }); } catch { MessageBox.Show("Email: kjnrmbiriri1+studytracker@gmail.com", "Contact me"); } };
        var tip = new Label { Left = 10, Top = 302, Width = 800, Height = 44, Tag = "muted", Text = "Tip: put a file named logo.png next to the app to replace the built-in logo. Your data stays on this computer (in your user folder) and never leaves it." };
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
        var title = new Label { Left = 10, Top = 6, Width = 600, Height = 44, Text = "Settings", Font = new Font("Segoe UI", 22, FontStyle.Bold), Tag = "title" };
        var appearance = new Label { Left = 10, Top = 52, Width = 400, Height = 28, Text = "Appearance & profile", Font = new Font("Segoe UI", 13, FontStyle.Bold), Tag = "sub" };
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
        familyPick.SelectedIndexChanged += delegate { if (familyPick.SelectedItem != null) { uiFontFamily = familyPick.SelectedItem.ToString(); SetFontFamily(this, uiFontFamily); Save(); } };
        var fontLabel = new Label { Left = 10, Top = 208, Width = 90, Height = 28, Text = "Font size", Tag = "muted" };
        var fontPick = new ComboBox { Left = 105, Top = 204, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList }; fontPick.Items.AddRange(new object[] { "Small", "Normal", "Large", "Extra large" }); fontPick.SelectedIndex = uiFontSize < 9.5f ? 0 : uiFontSize < 11f ? 1 : uiFontSize < 13f ? 2 : 3;
        fontPick.SelectedIndexChanged += delegate { float next = new float[] { 9f, 10f, 12f, 14f }[fontPick.SelectedIndex]; float ratio = next / uiFontSize; ScaleFonts(this, ratio); uiFontSize = next; Save(); };
        var minimizeCheck = new CheckBox { Left = 10, Top = 240, Width = 500, Height = 26, Text = "Minimize Study Tracker while the reading timer is running", Checked = minimizeDuringTimer };
        minimizeCheck.CheckedChanged += delegate { minimizeDuringTimer = minimizeCheck.Checked; Save(); };
        var updatesHead = new Label { Left = 10, Top = 276, Width = 400, Height = 28, Text = "Software updates", Font = new Font("Segoe UI", 13, FontStyle.Bold), Tag = "sub" };
        lblVer = new Label { Left = 10, Top = 54, Width = 700, Height = 28, Font = new Font("Segoe UI", 13, FontStyle.Bold) };
        lblUpdInfo = new Label { Left = 10, Top = 88, Width = 800, Height = 42, Tag = "muted" };
        lblUpdStatus = new Label { Left = 10, Top = 136, Width = 820, Height = 30, Font = new Font("Segoe UI", 12, FontStyle.Bold), Tag = "white" };
        bCheck = Theme.Btn("Check for updates", 10, 176, 190, "a"); bCheck.Height = 38;
        bInstall = Theme.Btn("Install update now", 210, 176, 190, "c"); bInstall.Height = 38; bInstall.Visible = false;
        prog = new ProgressBar { Left = 410, Top = 188, Width = 300, Height = 14, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30, Visible = false };
        rbAuto = new RadioButton { Left = 10, Top = 226, Width = 820, Height = 26, Text = "Automatic updates", Checked = autoMode };
        rbManual = new RadioButton { Left = 10, Top = 256, Width = 820, Height = 26, Text = "Notify me; let me install updates", Checked = !autoMode };
        var note = new Label { Left = 10, Top = 350, Width = 820, Height = 28, Tag = "muted", Text = "Your study data stays on this computer during updates." };
        p.Controls.AddRange(new Control[] { title, appearance, nameLabel, nameBox, saveName, themeLabel, themePick, colorPick, familyLabel, familyPick, fontLabel, fontPick, minimizeCheck, updatesHead, lblVer, lblUpdInfo, lblUpdStatus, bCheck, bInstall, prog, rbAuto, rbManual, note });
        lblVer.Top = 310; lblUpdInfo.Top = 342; lblUpdStatus.Top = 386; bCheck.Top = 426; bInstall.Top = 426; prog.Top = 438; rbAuto.Top = 472; rbManual.Top = 502; note.Top = 536;
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
        hourTimer = new Timer { Interval = 3600000 }; hourTimer.Tick += delegate { AutoCheck(); }; hourTimer.Start();
        // also check immediately when internet (re)connects — still at most once per calendar day
        NetworkChange.NetworkAvailabilityChanged += delegate { Ui(delegate { AutoCheck(); }); };
    }
    void AutoCheck()
    {
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
