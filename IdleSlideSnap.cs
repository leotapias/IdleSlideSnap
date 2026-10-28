// IdleSlideSnap - quietly grabs a screenshot whenever the screen changes.
// Handy for saving every slide of a Zoom/Teams presentation.
// Build it with build.bat (uses the C# compiler that ships with Windows, nothing to install).
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        // Without this, Windows lies about monitor sizes when display scaling is on,
        // and we'd end up capturing the wrong area.
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
        catch { try { SetProcessDPIAware(); } catch { } }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

class MainForm : Form
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

    const string APP = "IdleSlideSnap";
    const int WM_HOTKEY = 0x0312;
    const int TW = 160, TH = 90;          // we compare tiny thumbnails, not full screenshots
    const int PIXEL_DIFF = 18;            // how much a pixel's brightness must shift to count as "changed"
    const int INTERVAL_MS = 700;          // how often we look at the screen

    Label lblFolder, lblSens, lblStatus;
    Button btnFolder, btnPlay, btnShot, btnOpen;
    ComboBox cmbScreen;
    TrackBar trkSens;
    CheckBox chkAutoStop;
    NumericUpDown numMinutes;
    System.Windows.Forms.Timer clock;
    DateTime startedAt;

    string folder;
    Thread worker;
    volatile bool running;
    volatile float sensPercent = 1.5f;
    ManualResetEvent stopEvent = new ManualResetEvent(false);
    int count;

    public MainForm()
    {
        Text = APP;
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(400, 320);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        Controls.Add(new Label { Text = "Save screenshots to:", Location = new Point(12, 12), AutoSize = true });
        lblFolder = new Label { Location = new Point(12, 32), Size = new Size(290, 34), AutoEllipsis = true, ForeColor = Color.DimGray };
        btnFolder = new Button { Text = "Change...", Location = new Point(308, 30), Size = new Size(80, 26) };
        btnFolder.Click += delegate { ChooseFolder(); };

        Controls.Add(new Label { Text = "Screen to watch:", Location = new Point(12, 72), AutoSize = true });
        cmbScreen = new ComboBox { Location = new Point(12, 92), Size = new Size(376, 24), DropDownStyle = ComboBoxStyle.DropDownList };
        Screen[] screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
            cmbScreen.Items.Add(string.Format("Screen {0}  ({1}x{2}){3}", i + 1,
                screens[i].Bounds.Width, screens[i].Bounds.Height, screens[i].Primary ? "  - main" : ""));
        cmbScreen.SelectedIndex = 0;

        lblSens = new Label { Location = new Point(12, 126), AutoSize = true };
        trkSens = new TrackBar { Location = new Point(12, 144), Size = new Size(376, 30), Minimum = 1, Maximum = 20, Value = 3, TickFrequency = 1 };
        trkSens.ValueChanged += delegate { UpdateSens(); };
        UpdateSens();

        // Off by default, so it records until you hit STOP.
        chkAutoStop = new CheckBox { Text = "Auto-stop after", Location = new Point(14, 242), AutoSize = true };
        numMinutes = new NumericUpDown { Location = new Point(130, 240), Size = new Size(60, 24), Minimum = 1, Maximum = 999, Value = 60, Enabled = false };
        chkAutoStop.CheckedChanged += delegate { numMinutes.Enabled = chkAutoStop.Checked; UpdateTitle(); };
        Label lblMinutes = new Label { Text = "minutes", Location = new Point(194, 243), AutoSize = true };

        btnPlay = new Button { Location = new Point(12, 186), Size = new Size(266, 44), Font = new Font("Segoe UI", 12f, FontStyle.Bold) };
        btnPlay.Click += delegate { Toggle(); };
        btnShot = new Button { Text = "Manual shot", Location = new Point(284, 186), Size = new Size(104, 44) };
        btnShot.Click += delegate { ManualShot(); };

        lblStatus = new Label { Location = new Point(12, 278), Size = new Size(280, 34), ForeColor = Color.DimGray };
        btnOpen = new Button { Text = "Open folder", Location = new Point(298, 280), Size = new Size(90, 26) };

        // Ticks once a second to count down the auto-stop timer.
        clock = new System.Windows.Forms.Timer { Interval = 1000 };
        clock.Tick += delegate
        {
            if (running && chkAutoStop.Checked && TimeLeft() <= TimeSpan.Zero) Stop();
            else UpdateTitle();
        };
        btnOpen.Click += delegate { if (folder != null && Directory.Exists(folder)) Process.Start("explorer.exe", "\"" + folder + "\""); };

        Controls.AddRange(new Control[] { lblFolder, btnFolder, cmbScreen, lblSens, trkSens, chkAutoStop, numMinutes, lblMinutes, btnPlay, btnShot, lblStatus, btnOpen });
        SetRunningUI(false);
        lblStatus.Text = "Ready. Hotkeys: Ctrl+Alt+S play/stop, Ctrl+Alt+A manual shot";

        Shown += delegate { ChooseFolder(); };
    }

    void UpdateSens()
    {
        sensPercent = trkSens.Value * 0.5f;
        lblSens.Text = string.Format("Minimum change to capture: {0:0.0}% of the screen  (left = more sensitive)", sensPercent);
    }

    void ChooseFolder()
    {
        using (FolderBrowserDialog dlg = new FolderBrowserDialog())
        {
            dlg.Description = "Where should the screenshots be saved?";
            dlg.SelectedPath = folder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                folder = dlg.SelectedPath;
                count = LastNumberIn(folder);
                lblStatus.Text = count > 0
                    ? string.Format("Found {0} existing slides, will continue from slide_{1:0000}.", count, count + 1)
                    : "Ready. Hotkeys: Ctrl+Alt+S play/stop, Ctrl+Alt+A manual shot";
            }
        }
        lblFolder.Text = folder ?? "(none selected)";
    }

    // Looks for screenshots we saved here before, so numbering picks up where it left off
    // instead of starting over at 0001.
    static int LastNumberIn(string dir)
    {
        int max = 0;
        try
        {
            foreach (string f in Directory.GetFiles(dir, "slide_*.png"))
            {
                Match m = Regex.Match(Path.GetFileName(f), @"^slide_(\d+)_");
                int n;
                if (m.Success && int.TryParse(m.Groups[1].Value, out n) && n > max) max = n;
            }
        }
        catch { }
        return max;
    }

    void Toggle()
    {
        if (running) { Stop(); return; }
        if (folder == null) { ChooseFolder(); if (folder == null) return; }
        Directory.CreateDirectory(folder);
        Rectangle bounds = Screen.AllScreens[cmbScreen.SelectedIndex].Bounds;
        running = true;
        startedAt = DateTime.Now;
        clock.Start();
        stopEvent.Reset();
        worker = new Thread(delegate () { Loop(bounds); }) { IsBackground = true };
        worker.Start();
        SetRunningUI(true);
        Status(string.Format("Recording... {0} shots", count));
    }

    void ManualShot()
    {
        if (folder == null) { ChooseFolder(); if (folder == null) return; }
        Directory.CreateDirectory(folder);
        Rectangle b = Screen.AllScreens[cmbScreen.SelectedIndex].Bounds;
        try
        {
            using (Bitmap bmp = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(b.Location, Point.Empty, b.Size);
                Status(string.Format("{0}... {1} shots (manual: {2})", running ? "Recording" : "Stopped", count, SaveShot(bmp)));
            }
        }
        catch (Exception ex) { Status("Couldn't take the screenshot: " + ex.Message); }
    }

    // Auto and manual shots share one counter so the files stay in order.
    string SaveShot(Bitmap bmp)
    {
        int n = Interlocked.Increment(ref count);
        string name = string.Format("slide_{0:0000}_{1:yyyyMMdd_HHmmss}.png", n, DateTime.Now);
        bmp.Save(Path.Combine(folder, name), ImageFormat.Png);
        return name;
    }

    void Stop()
    {
        bool timeUp = chkAutoStop.Checked && TimeLeft() <= TimeSpan.Zero;
        running = false;
        clock.Stop();
        stopEvent.Set();
        if (worker != null) worker.Join(3000);
        SetRunningUI(false);
        Status(string.Format("{0}. {1} shots in the folder so far.", timeUp ? "Time's up, stopped" : "Stopped", count));
    }

    // The minutes can be changed while recording; the countdown just follows along.
    TimeSpan TimeLeft()
    {
        return startedAt.AddMinutes((double)numMinutes.Value) - DateTime.Now;
    }

    void UpdateTitle()
    {
        if (!running) { Text = APP; return; }
        string title = string.Format("● {0} ({1})", APP, count);
        if (chkAutoStop.Checked)
        {
            TimeSpan left = TimeLeft();
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            title += string.Format(" - stops in {0}:{1:00}:{2:00}", (int)left.TotalHours, left.Minutes, left.Seconds);
        }
        Text = title;
    }

    void SetRunningUI(bool on)
    {
        btnPlay.Text = on ? "■  STOP" : "▶  PLAY";
        btnPlay.BackColor = on ? Color.FromArgb(220, 60, 60) : Color.FromArgb(40, 160, 90);
        btnPlay.ForeColor = Color.White;
        btnFolder.Enabled = cmbScreen.Enabled = !on;
        UpdateTitle();
    }

    void Status(string s)
    {
        if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { Status(s); }); return; }
        lblStatus.Text = s;
        UpdateTitle();
    }

    // The main loop. We save a shot when the screen looks different from the last one
    // we saved, but only once it has settled down - that way we don't catch a slide
    // halfway through a transition, and a playing video doesn't flood the folder.
    void Loop(Rectangle b)
    {
        byte[] prev = null, lastSaved = null;
        using (Bitmap full = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb))
        using (Graphics g = Graphics.FromImage(full))
        {
            while (running)
            {
                try
                {
                    g.CopyFromScreen(b.Location, Point.Empty, b.Size);
                    byte[] t = Thumb(full);
                    float s = sensPercent;
                    bool stable = prev != null && Diff(t, prev) < s * 0.5f;
                    if (stable && (lastSaved == null || Diff(t, lastSaved) >= s))
                    {
                        string name = SaveShot(full);
                        lastSaved = t;
                        Status(string.Format("Recording... {0} shots (last: {1})", count, name));
                    }
                    prev = t;
                }
                catch (Exception ex)
                {
                    // Usually the screen is locked or a UAC prompt is up. Just try again next round.
                    Status("Retrying: " + ex.Message);
                }
                stopEvent.WaitOne(INTERVAL_MS);
            }
        }
    }

    // Shrinks the screenshot down and keeps only brightness, which is all we need to spot changes.
    static byte[] Thumb(Bitmap src)
    {
        byte[] lum = new byte[TW * TH];
        using (Bitmap small = new Bitmap(TW, TH, PixelFormat.Format24bppRgb))
        {
            using (Graphics sg = Graphics.FromImage(small))
            {
                sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                sg.DrawImage(src, 0, 0, TW, TH);
            }
            BitmapData d = small.LockBits(new Rectangle(0, 0, TW, TH), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            byte[] raw = new byte[d.Stride * TH];
            Marshal.Copy(d.Scan0, raw, 0, raw.Length);
            small.UnlockBits(d);
            for (int y = 0; y < TH; y++)
                for (int x = 0; x < TW; x++)
                {
                    int i = y * d.Stride + x * 3;
                    lum[y * TW + x] = (byte)((raw[i] * 29 + raw[i + 1] * 150 + raw[i + 2] * 77) >> 8);
                }
        }
        return lum;
    }

    // What percentage of the thumbnail changed between two frames.
    static float Diff(byte[] a, byte[] b)
    {
        int changed = 0;
        for (int i = 0; i < a.Length; i++)
            if (Math.Abs(a[i] - b[i]) > PIXEL_DIFF) changed++;
        return changed * 100f / a.Length;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Keep our own window out of the screenshots (works on Windows 10 2004 and newer).
        try { SetWindowDisplayAffinity(Handle, 0x11); } catch { }
        // Global hotkeys, so you can control it without leaving Zoom.
        RegisterHotKey(Handle, 1, 0x0001 | 0x0002 | 0x4000, (uint)Keys.S); // Ctrl+Alt+S: play/stop
        RegisterHotKey(Handle, 2, 0x0001 | 0x0002 | 0x4000, (uint)Keys.A); // Ctrl+Alt+A: manual shot
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            if (m.WParam.ToInt32() == 1) Toggle();
            else ManualShot();
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (running) Stop();
        UnregisterHotKey(Handle, 1);
        UnregisterHotKey(Handle, 2);
        base.OnFormClosing(e);
    }
}
