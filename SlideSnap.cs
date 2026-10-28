// SlideSnap - captura silenciosamente la pantalla cuando cambia (ideal para slides en Zoom/Teams).
// Compilar con build.bat (usa el csc.exe incluido en Windows, sin dependencias).
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        // DPI aware para que las coordenadas de cada monitor sean reales (pantallas con escalado).
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

    const int WM_HOTKEY = 0x0312;
    const int TW = 160, TH = 90;          // tamaño de la miniatura usada para comparar
    const int PIXEL_DIFF = 18;            // diferencia de luminancia para considerar un pixel "cambiado"
    const int INTERVAL_MS = 700;

    Label lblFolder, lblSens, lblStatus;
    Button btnFolder, btnPlay, btnShot, btnOpen;
    ComboBox cmbScreen;
    TrackBar trkSens;

    string folder;
    Thread worker;
    volatile bool running;
    volatile float sensPercent = 1.5f;
    ManualResetEvent stopEvent = new ManualResetEvent(false);
    int count;

    public MainForm()
    {
        Text = "SlideSnap";
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(400, 285);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        Controls.Add(new Label { Text = "Carpeta de destino:", Location = new Point(12, 12), AutoSize = true });
        lblFolder = new Label { Location = new Point(12, 32), Size = new Size(290, 34), AutoEllipsis = true, ForeColor = Color.DimGray };
        btnFolder = new Button { Text = "Cambiar...", Location = new Point(308, 30), Size = new Size(80, 26) };
        btnFolder.Click += delegate { ChooseFolder(); };

        Controls.Add(new Label { Text = "Pantalla a vigilar:", Location = new Point(12, 72), AutoSize = true });
        cmbScreen = new ComboBox { Location = new Point(12, 92), Size = new Size(376, 24), DropDownStyle = ComboBoxStyle.DropDownList };
        Screen[] screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
            cmbScreen.Items.Add(string.Format("Pantalla {0}  ({1}x{2}){3}", i + 1,
                screens[i].Bounds.Width, screens[i].Bounds.Height, screens[i].Primary ? "  - principal" : ""));
        cmbScreen.SelectedIndex = 0;

        lblSens = new Label { Location = new Point(12, 126), AutoSize = true };
        trkSens = new TrackBar { Location = new Point(12, 144), Size = new Size(376, 30), Minimum = 1, Maximum = 20, Value = 3, TickFrequency = 1 };
        trkSens.ValueChanged += delegate { UpdateSens(); };
        UpdateSens();

        btnPlay = new Button { Location = new Point(12, 186), Size = new Size(266, 44), Font = new Font("Segoe UI", 12f, FontStyle.Bold) };
        btnPlay.Click += delegate { Toggle(); };
        btnShot = new Button { Text = "Captura manual", Location = new Point(284, 186), Size = new Size(104, 44) };
        btnShot.Click += delegate { ManualShot(); };

        lblStatus = new Label { Location = new Point(12, 244), Size = new Size(280, 34), ForeColor = Color.DimGray };
        btnOpen = new Button { Text = "Abrir carpeta", Location = new Point(298, 246), Size = new Size(90, 26) };
        btnOpen.Click += delegate { if (folder != null && Directory.Exists(folder)) Process.Start("explorer.exe", "\"" + folder + "\""); };

        Controls.AddRange(new Control[] { lblFolder, btnFolder, cmbScreen, lblSens, trkSens, btnPlay, btnShot, lblStatus, btnOpen });
        SetRunningUI(false);
        lblStatus.Text = "Listo. Atajos: Ctrl+Alt+S play/stop, Ctrl+Alt+A captura manual";

        Shown += delegate { ChooseFolder(); };
    }

    void UpdateSens()
    {
        sensPercent = trkSens.Value * 0.5f;
        lblSens.Text = string.Format("Cambio mínimo para capturar: {0:0.0}% de la pantalla  (izq = más sensible)", sensPercent);
    }

    void ChooseFolder()
    {
        using (FolderBrowserDialog dlg = new FolderBrowserDialog())
        {
            dlg.Description = "¿Dónde guardar las capturas?";
            dlg.SelectedPath = folder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (dlg.ShowDialog(this) == DialogResult.OK) folder = dlg.SelectedPath;
        }
        lblFolder.Text = folder ?? "(sin elegir)";
    }

    void Toggle()
    {
        if (running) { Stop(); return; }
        if (folder == null) { ChooseFolder(); if (folder == null) return; }
        Directory.CreateDirectory(folder);
        Rectangle bounds = Screen.AllScreens[cmbScreen.SelectedIndex].Bounds;
        running = true;
        stopEvent.Reset();
        worker = new Thread(delegate () { Loop(bounds); }) { IsBackground = true };
        worker.Start();
        SetRunningUI(true);
        Status(string.Format("Grabando... {0} capturas", count));
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
                Status(string.Format("{0}... {1} capturas (manual: {2})", running ? "Grabando" : "Detenido", count, SaveShot(bmp)));
            }
        }
        catch (Exception ex) { Status("Error al capturar: " + ex.Message); }
    }

    // Numeración compartida entre capturas automáticas y manuales, para que queden en orden.
    string SaveShot(Bitmap bmp)
    {
        int n = Interlocked.Increment(ref count);
        string name = string.Format("slide_{0:0000}_{1:yyyyMMdd_HHmmss}.png", n, DateTime.Now);
        bmp.Save(Path.Combine(folder, name), ImageFormat.Png);
        return name;
    }

    void Stop()
    {
        running = false;
        stopEvent.Set();
        if (worker != null) worker.Join(3000);
        SetRunningUI(false);
        Status(string.Format("Detenido. {0} capturas guardadas en total.", count));
    }

    void SetRunningUI(bool on)
    {
        btnPlay.Text = on ? "■  STOP" : "▶  PLAY";
        btnPlay.BackColor = on ? Color.FromArgb(220, 60, 60) : Color.FromArgb(40, 160, 90);
        btnPlay.ForeColor = Color.White;
        btnFolder.Enabled = cmbScreen.Enabled = !on;
        Text = on ? "● SlideSnap - grabando" : "SlideSnap";
    }

    void Status(string s)
    {
        if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { Status(s); }); return; }
        lblStatus.Text = s;
        if (running) Text = string.Format("● SlideSnap ({0})", count);
    }

    // Bucle de captura: guarda cuando la imagen difiere de la última guardada
    // y además está "quieta" (evita capturar a mitad de una transición/animación).
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
                        Status(string.Format("Grabando... {0} capturas (última: {1})", count, name));
                    }
                    prev = t;
                }
                catch (Exception ex)
                {
                    // p.ej. pantalla bloqueada o UAC: se reintenta en el siguiente ciclo
                    Status("Reintentando: " + ex.Message);
                }
                stopEvent.WaitOne(INTERVAL_MS);
            }
        }
    }

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

    // Porcentaje de pixeles de la miniatura que cambiaron.
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
        // La ventana de SlideSnap no aparece en sus propias capturas (Windows 10 2004+).
        try { SetWindowDisplayAffinity(Handle, 0x11); } catch { }
        RegisterHotKey(Handle, 1, 0x0001 | 0x0002 | 0x4000, (uint)Keys.S); // Ctrl+Alt+S
        RegisterHotKey(Handle, 2, 0x0001 | 0x0002 | 0x4000, (uint)Keys.A); // Ctrl+Alt+A
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
