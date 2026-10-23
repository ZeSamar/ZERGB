using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Interop;
using Microsoft.Win32;
using Path = System.IO.Path;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

[assembly: AssemblyTitle("ZERGB")]
[assembly: AssemblyProduct("ZERGB")]
[assembly: AssemblyDescription("Desktop widget for PC case RGB lighting control over HID (no RGB Fusion required)")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
internal static class HotKeys
{
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const int WM_HOTKEY = 0x0312;
}

internal static class Win32Show
{
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    public const int SW_RESTORE = 9;
    public const int SW_SHOW = 5;
}

internal static class Native
{
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
}

/// <summary>Прямое управление подсветкой через HID-контроллер ITE.</summary>
internal sealed class RgbHid
{
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(IntPtr h, byte[] buffer, int length);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(string path, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr templ);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);

    private IntPtr handle = IntPtr.Zero;
    public bool ChannelFix = true;
    public int Mode = 1;
    public int Speed = 5;

    public bool IsOpen { get { return handle != IntPtr.Zero && handle != new IntPtr(-1); } }

    public static string FindDevicePath()
    {
        try
        {
            using (var searcher = new ManagementObjectSearcher(
                "SELECT PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE '%VID_048D&PID_5702%'"))
            {
                string best = null;
                foreach (ManagementObject mo in searcher.Get())
                {
                    string id = Convert.ToString(mo["PNPDeviceID"]);
                    if (string.IsNullOrEmpty(id)) continue;
                    if (id.IndexOf("COL02", StringComparison.OrdinalIgnoreCase) >= 0) return Build(id);
                    if (best == null) best = id;
                }
                if (best != null) return Build(best);
            }
        }
        catch { }
        return null;
    }

    private static string Build(string instanceId)
    {
        return @"\\?\" + instanceId.ToLowerInvariant().Replace('\\', '#') + "#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    }

    public bool Open(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        Close();
        handle = CreateFileW(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        return IsOpen;
    }

    public void Close()
    {
        if (handle != IntPtr.Zero && handle != new IntPtr(-1))
        {
            try { CloseHandle(handle); } catch { }
        }
        handle = IntPtr.Zero;
    }

    private void Send(byte[] data)
    {
        if (!IsOpen) return;
        byte[] buf = new byte[64];
        Array.Copy(data, buf, Math.Min(data.Length, 64));
        HidD_SetFeature(handle, buf, 64);
    }

    private void SendZones()
    {
        for (byte z = 0x21; z <= 0x27; z++) Send(new byte[] { 0xCC, z });
        Send(new byte[] { 0xCC, 0x28, 0xFF });
    }

    public bool SetColor(int r, int g, int b, int bright)
    {
        if (!IsOpen) return false;
        int sr = r, sg = g, sb = b;
        if (ChannelFix) { sr = b; sg = r; sb = g; }
        byte br = (byte)Math.Max(0, Math.Min(100, bright));
        SendZones();
        byte[] cfg = new byte[64];
        cfg[0] = 0xCC; cfg[1] = 0x20; cfg[2] = 0xFF;
        cfg[11] = 0x01; cfg[12] = br;
        cfg[14] = (byte)sb; cfg[15] = (byte)sg; cfg[16] = (byte)sr;
        Send(cfg);
        Send(new byte[] { 0xCC, 0x28, 0xFF });
        return true;
    }

    // Таблицы из декомпилированного RGB Fusion (LedLib2, класс MCU_8297)
    private static readonly int[,] PluseTiming = {
        {1600,1600},{1400,1400},{1200,1200},{1000,1000},{900,900},
        {800,800},{700,700},{600,600},{500,500},{400,400} };
    private static readonly int[,] FlashTiming = {
        {100,100,2400},{100,100,2200},{100,100,2000},{100,100,1800},{100,100,1600},
        {100,100,1400},{100,100,1200},{100,100,1000},{100,100,800},{100,100,600} };
    private static readonly int[,] DFlashTiming = {
        {100,100,2600},{100,100,2400},{100,100,2200},{100,100,2000},{100,100,1800},
        {100,100,1600},{100,100,1400},{100,100,1200},{100,100,1000},{100,100,800} };
    private static readonly int[] ShakingTiming = { 230, 185, 145, 110, 80, 55, 35, 20, 15, 10 };
    private static readonly int[] ExplodeTiming = { 1800, 1600, 1400, 1200, 1000, 800, 700, 600, 500, 400 };
    private static readonly int[] WaveTiming = { 300, 250, 205, 165, 130, 100, 75, 55, 40, 30 };
    private static readonly int[] MeteorTiming = { 150, 130, 110, 90, 70, 50, 35, 20, 15, 10 };
    private static readonly int[,] CycleTiming = {
        {2400,2200},{1100,900},{1000,800},{900,700},{800,600},
        {700,500},{600,400},{500,300},{400,200},{300,100} };

    /// <summary>Аппаратный режим: 1 статика, 2 дыхание, 3 мигание, 4 радуга.</summary>
    /// <summary>Аппаратный режим по кодам LMode_8297 из RGB Fusion.</summary>
    public bool SetEffect(byte mode, int r, int g, int b, int bright, int speedLevel)
    {
        if (!IsOpen) return false;
        if (speedLevel < 0) speedLevel = 0;
        if (speedLevel > 9) speedLevel = 9;
        if (bright < 0) bright = 0;
        if (bright > 100) bright = 100;

        int sr = r, sg = g, sb = b;
        if (ChannelFix) { sr = b; sg = r; sb = g; }
        byte br = (byte)bright;

        byte[] buf = new byte[64];
        buf[0] = 0xCC; buf[1] = 0x20; buf[2] = 0xFF;
        buf[11] = mode;          // Mode_Sel
        buf[12] = br;            // MaxBrightness
        buf[13] = 0;             // MinBrightness
        buf[14] = (byte)sb; buf[15] = (byte)sg; buf[16] = (byte)sr;

        switch (mode)
        {
            case 1:   // статика
                break;

            case 2:   // дыхание
            {
                int p0 = PluseTiming[speedLevel, 0];
                int p1 = PluseTiming[speedLevel, 1];
                int num2 = p0 - p0 / 2;
                int num3 = p1 - p0 / 2;
                int num4 = speedLevel <= 7 ? num2 - 220 : (speedLevel == 8 ? num2 - 120 : num2 - 100);
                if (num4 < 0) num4 = 0;
                PutTime(buf, num2, num3 - num4, num2 + num4 / 2);
                buf[12] = 100;
                buf[31] = 1;
                break;
            }

            case 3:   // мигание
                PutTime(buf, FlashTiming[speedLevel, 0], FlashTiming[speedLevel, 1], FlashTiming[speedLevel, 2]);
                buf[30] = 0; buf[31] = 1; buf[32] = 1;
                break;

            case 51:  // двойное мигание: Mode_Sel 3 + другая таблица
                buf[11] = 3;
                PutTime(buf, DFlashTiming[speedLevel, 0], DFlashTiming[speedLevel, 1], DFlashTiming[speedLevel, 2]);
                buf[30] = 0; buf[31] = 1; buf[32] = 2;
                break;

            case 4:   // радуга
                PutTime(buf, CycleTiming[speedLevel, 0], CycleTiming[speedLevel, 1], 0);
                buf[30] = 7;
                break;

            case 5:   // метеор
                PutTime(buf, MeteorTiming[speedLevel], 0, 0);
                buf[30] = 0; buf[31] = 1;
                break;

            case 6:   // волна
            case 7:   // рябь
            case 100: // SWave
            case 112: // SWaveMode1
                PutTime(buf, WaveTiming[speedLevel], 0, 0);
                buf[30] = 0; buf[31] = 1;
                break;

            case 8:   // случайный
                PutTime(buf, CycleTiming[speedLevel, 0], 0, 0);
                break;

            case 52:  // плавный переход
                buf[11] = 1;
                PutTime(buf, 800, 0, 0);
                break;

            case 53:  // демо
                PutTime(buf, CycleTiming[speedLevel, 0], CycleTiming[speedLevel, 1], 0);
                buf[30] = 7;
                break;

            case 56:  // ритм
                PutTime(buf, FlashTiming[speedLevel, 2], 0, 0);
                buf[30] = 0; buf[31] = 1; buf[32] = 1;
                break;

            case 102: // SMeteor
            case 106: // двойной метеор
                PutTime(buf, MeteorTiming[speedLevel], 0, 20);
                buf[30] = 0; buf[31] = 1;
                break;

            case 103: // дрожание
                PutTime(buf, ShakingTiming[speedLevel], 0, 100);
                buf[30] = 0; buf[31] = 1;
                break;

            case 104: // взрыв
            case 105: // восстановление
                PutTime(buf, ExplodeTiming[speedLevel], 0, 100);
                buf[30] = 0; buf[31] = 1;
                break;

            case 107: // стек цвета
            case 109: // цветной дождь
            case 111: // DWave1
                PutTime(buf, CycleTiming[speedLevel, 0], CycleTiming[speedLevel, 1], 0);
                buf[30] = 7;
                break;

            default:
                PutTime(buf, CycleTiming[speedLevel, 0], 0, 0);
                break;
        }

        SendZones();
        Send(buf);
        Send(new byte[] { 0xCC, 0x28, 0xFF });
        return true;
    }

    private static void PutTime(byte[] buf, int w0, int w1, int w2)
    {
        buf[22] = (byte)(w0 & 0xFF); buf[23] = (byte)((w0 >> 8) & 0xFF);
        buf[24] = (byte)(w1 & 0xFF); buf[25] = (byte)((w1 >> 8) & 0xFF);
        buf[26] = (byte)(w2 & 0xFF); buf[27] = (byte)((w2 >> 8) & 0xFF);
    }
    public bool SetOff()
    {
        if (!IsOpen) return false;
        SendZones();

        // выключение = статический режим, чёрный цвет на полной яркости
        byte[] cfg = new byte[64];
        cfg[0] = 0xCC; cfg[1] = 0x20; cfg[2] = 0xFF;
        cfg[11] = 0x01; cfg[12] = 0x5A;
        cfg[14] = 0x00; cfg[15] = 0x00; cfg[16] = 0x00;
        Send(cfg);
        Send(new byte[] { 0xCC, 0x28, 0xFF });
        Send(cfg);
        Send(new byte[] { 0xCC, 0x28, 0xFF });

        return true;
    }
}

internal sealed class Cfg
{
    private readonly string path;
    private readonly string dirPath;
    public string LastError = null;
    public int R = 255, G = 0, B = 184;
    public int Brightness = 100;
    public bool TopMost = false;
    public bool ChannelFix = true;
    public int Mode = 1;
    public int Speed = 5;
    public double? Left = null, Top = null;
    public string[] Presets = { "#FF4D6D", "#FFB020", "#3DDC84", "#38BDF8", "#A78BFA", "#F5F5F5" };

    public Cfg()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZERGB"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZERGB"),
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "data")
        };

        foreach (string cand in candidates)
        {
            try
            {
                Directory.CreateDirectory(cand);
                string probe = Path.Combine(cand, "settings.ini");
                using (var fs = new FileStream(probe, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite)) { }
                dirPath = cand;
                path = probe;
                break;
            }
            catch { }
        }

        if (path == null)
        {
            dirPath = candidates[candidates.Length - 1];
            path = Path.Combine(dirPath, "settings.ini");
        }

        Load();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(path)) return;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "color": { var p = v.Split(','); if (p.Length == 3) { R = int.Parse(p[0]); G = int.Parse(p[1]); B = int.Parse(p[2]); } break; }
                    case "brightness": Brightness = int.Parse(v); break;
                    case "topmost": TopMost = v == "1"; break;
                    case "channelfix": ChannelFix = v == "1"; break;
                    case "mode": Mode = int.Parse(v); break;
                    case "speed": Speed = int.Parse(v); break;
                    case "left": Left = double.Parse(v, CultureInfo.InvariantCulture); break;
                    case "top": Top = double.Parse(v, CultureInfo.InvariantCulture); break;
                    case "presets": { var p = v.Split(';'); if (p.Length >= 1) Presets = p; } break;
                }
            }
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("# RGB Widget settings");
            sb.AppendLine("color=" + R + "," + G + "," + B);
            sb.AppendLine("brightness=" + Brightness);
            sb.AppendLine("topmost=" + (TopMost ? "1" : "0"));
            sb.AppendLine("channelfix=" + (ChannelFix ? "1" : "0"));
            sb.AppendLine("mode=" + Mode);
            sb.AppendLine("speed=" + Speed);
            if (Left.HasValue) sb.AppendLine("left=" + Left.Value.ToString(CultureInfo.InvariantCulture));
            if (Top.HasValue) sb.AppendLine("top=" + Top.Value.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("presets=" + string.Join(";", Presets));
            File.WriteAllText(path, sb.ToString());
        }
        catch (Exception ex)
        {
            LastError = ex.GetType().Name + ": " + ex.Message + " (path=" + path + ")";
            try { File.AppendAllText(Path.Combine(dirPath, "save-error.log"), DateTime.Now + " " + ex + "\r\n"); } catch { }
        }
    }
}

internal sealed class WidgetApp
{
    private readonly Window win;
    private readonly Cfg cfg = new Cfg();
    private readonly RgbHid hid = new RgbHid();
    private WinForms.NotifyIcon tray;
    private readonly string asmPath = Assembly.GetExecutingAssembly().Location;

    private Image wheelImg;
    private Ellipse wheelRing, wheelCore;
    private Canvas wheelHost;
    private Border swatch, briFill;
    private TextBlock hexText, rgbText, briText, statusText;
    private Grid briHost, header;
    private Ellipse briThumb;
    private Ellipse statusDot;
    private UniformGrid presetPanel;
    private Panel modePanel;
    private Slider speedSlider;
    private MusicEngine music;
    private WinForms.ToolStripMenuItem miFix;
    private Button minBtn, closeBtn, powerBtn;
    private ToggleButton pinBtn;
    private ToggleButton chanFixBtn;
    private bool powerOn = true;
    private byte currentMode = 1;   // 1 статика … 8 случайный
    private int speedLevel = 5;     // 0..9
    private readonly System.Collections.Generic.List<byte> modeCodes = new System.Collections.Generic.List<byte>();
    private readonly System.Collections.Generic.List<string> modeTitles = new System.Collections.Generic.List<string>();

    private int curR, curG, curB, curBri;
    private double curHue, curSat;
    private bool dragging, briDrag;

    private const double WheelCenter = 122.0;
    private const double WheelRadius = 118.0;
    private const double ThumbWidth = 14.0;

    private double TrackWidth
    {
        get { return briHost != null && briHost.ActualWidth > 1 ? briHost.ActualWidth : 236.0; }
    }

    public WidgetApp()
    {
        string xaml = ReadText("ui.xaml");
        win = (Window)XamlReader.Parse(xaml);

        wheelHost = (Canvas)win.FindName("WheelHost");
        wheelImg = (Image)win.FindName("WheelImg");
        wheelRing = (Ellipse)win.FindName("WheelRing");
        wheelCore = (Ellipse)win.FindName("WheelCore");
        swatch = (Border)win.FindName("Swatch");
        hexText = (TextBlock)win.FindName("HexText");
        rgbText = (TextBlock)win.FindName("RgbText");
        briText = (TextBlock)win.FindName("BriText");
        briHost = (Grid)win.FindName("BriHost");
        briFill = (Border)win.FindName("BriFill");
        briThumb = (Ellipse)win.FindName("BriThumb");
        statusText = (TextBlock)win.FindName("StatusText");
        header = (Grid)win.FindName("Header");
        statusDot = (Ellipse)win.FindName("StatusDot");
        presetPanel = (UniformGrid)win.FindName("PresetPanel");
        modePanel = (Panel)win.FindName("ModePanel");
        speedSlider = (Slider)win.FindName("SpeedSlider");
        pinBtn = (ToggleButton)win.FindName("PinBtn");
        powerBtn = (Button)win.FindName("PowerBtn");
        chanFixBtn = (ToggleButton)win.FindName("ChanFixBtn");
        minBtn = (Button)win.FindName("MinBtn");
        closeBtn = (Button)win.FindName("CloseBtn");

        ImageSource winIcon = LoadWindowIcon();
        if (winIcon != null) win.Icon = winIcon;

        ImageSource wheel = LoadImage("wheel.png");
        if (wheel != null) wheelImg.Source = wheel;

        curR = cfg.R; curG = cfg.G; curB = cfg.B; curBri = cfg.Brightness;
        currentMode = (byte)(cfg.Mode >= 1 && cfg.Mode <= 200 ? cfg.Mode : 1);
        speedLevel = cfg.Speed >= 0 && cfg.Speed <= 9 ? cfg.Speed : 5;
        hid.ChannelFix = cfg.ChannelFix;

        win.Topmost = cfg.TopMost;
        if (cfg.Left.HasValue && cfg.Top.HasValue)
        {
            win.WindowStartupLocation = WindowStartupLocation.Manual;
            win.Left = cfg.Left.Value; win.Top = cfg.Top.Value;
        }
        else
        {
            var wa = WinForms.Screen.PrimaryScreen.WorkingArea;
            win.WindowStartupLocation = WindowStartupLocation.Manual;
            win.Left = wa.Right - 356 - 28;
            win.Top = wa.Bottom - 530 - 28;
        }

        UpdatePinVisual();
        UpdateChanFixVisual();
        UpdateBrightnessVisual();
        UpdatePreview();
        BuildPresets();
        BuildModes();
        BuildSpeed();
        WireEvents();
        InitTray();

        if (cfg.LastError != null) SetStatus("Настройки не сохраняются: " + cfg.LastError, "#E06060");

        bool rfClosed = CloseRgbFusion();

        bool ok = hid.Open(RgbHid.FindDevicePath());
        if (ok)
        {
            statusDot.Fill = Brush("#3DDC84");
            if (IsRgbFusionRunning())
                SetStatus("RGB Fusion запущен с правами админа — закройте через меню трея", "#E0A060");
            else
                SetStatus(rfClosed ? "RGB Fusion закрыт, прямое управление (HID)" : "Прямое управление (HID)", "#3DDC84");
            hid.SetEffect(currentMode, curR, curG, curB, curBri, speedLevel);
        }
        else
        {
            statusDot.Fill = Brush("#E06060");
            SetStatus("Контроллер подсветки не найден", "#E06060");
        }

        cfg.Save();
        if (cfg.LastError != null) SetStatus("Настройки: " + cfg.LastError, "#E06060");

        win.Show();
        StartActivationListener();
        RegisterHotKeys();
    }

    /// <summary>Закрывает RGB Fusion: он пишет в тот же контроллер и вызывает мигания.</summary>
    private static void Log(string msg)
    {
        try
        {
            string p = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "debug.log");
            File.AppendAllText(p, DateTime.Now.ToString("HH:mm:ss") + " " + msg + "\r\n");
        }
        catch { }
    }

    private static bool CloseRgbFusion()
    {
        bool any = false;
        var ids = new List<int>();

        // 1) через WMI — видит процессы даже при ограниченном токене
        try
        {
            using (var q = new ManagementObjectSearcher("SELECT ProcessId FROM Win32_Process WHERE Name='RGBFusion.exe'"))
            {
                foreach (ManagementObject mo in q.Get())
                {
                    try { ids.Add(Convert.ToInt32(mo["ProcessId"])); } catch { }
                }
            }
        }
        catch (Exception ex) { Log("wmi enum failed: " + ex.GetType().Name); }

        // 2) обычное перечисление как запасной путь
        if (ids.Count == 0)
        {
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("RGBFusion")) ids.Add(p.Id);
            }
            catch { }
        }

        if (ids.Count > 0) Log("close attempt: found " + ids.Count);

        foreach (int id in ids)
        {
            try
            {
                var pr = System.Diagnostics.Process.GetProcessById(id);
                pr.Kill();
                pr.WaitForExit(3000);
                any = true;
                Log("killed pid " + id);
            }
            catch (Exception ex)
            {
                Log("kill failed pid " + id + ": " + ex.GetType().Name);
                // запасной вариант — taskkill
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("taskkill", "/PID " + id + " /F")
                    { CreateNoWindow = true, UseShellExecute = false };
                    using (var tk = System.Diagnostics.Process.Start(psi)) { tk.WaitForExit(4000); }
                    any = true;
                    Log("taskkill used for pid " + id);
                }
                catch (Exception ex2) { Log("taskkill failed: " + ex2.GetType().Name); }
            }
        }
        return any;
    }

    /// <summary>Закрывает RGB Fusion через запрос прав администратора (он запущен с повышением).</summary>
    private void CloseRgbFusionElevated()
    {
        if (!IsRgbFusionRunning())
        {
            SetStatus("RGB Fusion не запущен", "#3DDC84");
            return;
        }
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("taskkill", "/IM RGBFusion.exe /F")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            var p = System.Diagnostics.Process.Start(psi);
            if (p != null) p.WaitForExit(8000);
            SetStatus(IsRgbFusionRunning() ? "Не удалось закрыть RGB Fusion" : "RGB Fusion закрыт", "#3DDC84");
        }
        catch { SetStatus("Закрытие RGB Fusion отменено", "#E0A060"); }
    }

    private static bool IsRgbFusionRunning()
    {
        try { return System.Diagnostics.Process.GetProcessesByName("RGBFusion").Length > 0; }
        catch { return false; }
    }

    // ---------- ресурсы ----------

    private static string ReadText(string name)
    {
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        using (var r = new StreamReader(s))
            return r.ReadToEnd();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.ico"))
            {
                if (s != null) return new System.Drawing.Icon(s, 32, 32);
            }
        }
        catch { }
        try { return Drawing.Icon.ExtractAssociatedIcon(Application.ResourceAssembly.Location); }
        catch { return null; }
    }

    private static ImageSource LoadWindowIcon()
    {
        try
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.ico"))
            {
                if (s == null) return null;
                var dec = new IconBitmapDecoder(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                return dec.Frames.Count > 0 ? dec.Frames[0] : null;
            }
        }
        catch { return null; }
    }

    private static ImageSource LoadImage(string name)
    {
        try
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return null;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = s;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
        }
        catch { return null; }
    }

    private static Brush Brush(string hex)
    {
        return (Brush)new BrushConverter().ConvertFromString(hex);
    }

    // ---------- ui ----------

    private void SetStatus(string text, string color)
    {
        statusText.Text = text;
        statusText.Foreground = Brush(color);
    }

    private void UpdatePreview()
    {
        string hex = string.Format("#{0:X2}{1:X2}{2:X2}", curR, curG, curB);
        swatch.Background = Brush(hex);
        hexText.Text = hex;
        rgbText.Text = curR + " · " + curG + " · " + curB;

        var hsv = RgbToHsv(curR, curG, curB);
        curHue = hsv[0]; curSat = hsv[1];

        double rad = curHue * Math.PI / 180.0;
        double d = 122.0 * curSat;
        Canvas.SetLeft(wheelRing, 122 + d * Math.Cos(rad) - 10);
        Canvas.SetTop(wheelRing, 122 - d * Math.Sin(rad) - 10);
        wheelCore.Visibility = curSat < 0.06 ? Visibility.Visible : Visibility.Hidden;
    }

    private void UpdateBrightnessVisual()
    {
        double w = TrackWidth;
        double p = Math.Max(0, Math.Min(100, curBri)) / 100.0;
        briFill.Width = Math.Max(3, w * p);
        briThumb.Margin = new Thickness(Math.Max(0, p * (w - ThumbWidth)), 0, 0, 0);
        briText.Text = curBri + "%";
    }

    private void UpdatePinVisual()
    {
        pinBtn.IsChecked = win.Topmost;
    }

    private void ApplyColor()
    {
        if (!hid.IsOpen) { SetStatus("Нет связи с контроллером", "#E06060"); return; }
        if (!powerOn)
        {
            cfg.R = curR; cfg.G = curG; cfg.B = curB; cfg.Brightness = curBri; cfg.Save();
            SetStatus("Цвет сохранён (подсветка выключена)", "#8A93A8");
            return;
        }
        if (currentMode == 200)
        {
            if (music == null) music = new MusicEngine((r, g, b, br) => { if (powerOn && hid.IsOpen) hid.SetEffect(1, r, g, b, br, speedLevel); });
            music.Start();
            SetStatus("Музыка — слушаю системный звук", "#3DDC84");
            return;
        }
        if (music != null) music.Stop();

        if (hid.SetEffect(currentMode, curR, curG, curB, curBri, speedLevel))
            SetStatus("Применено " + string.Format("#{0:X2}{1:X2}{2:X2}", curR, curG, curB), "#3DDC84");
        else
            SetStatus("Не удалось применить цвет", "#E0A060");
        cfg.R = curR; cfg.G = curG; cfg.B = curB; cfg.Brightness = curBri; cfg.Save();
    }

    private void WireEvents()
    {
        header.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { win.DragMove(); } catch { } } };

        powerBtn.Click += (s, e) => TogglePower();
        chanFixBtn.Click += (s, e) => ToggleChannelFix();

        pinBtn.Click += (s, e) => { win.Topmost = pinBtn.IsChecked == true; cfg.TopMost = win.Topmost; cfg.Save(); UpdatePinVisual(); };
        minBtn.Click += (s, e) => { win.WindowState = WindowState.Minimized; };
        closeBtn.Click += (s, e) => HideToTray();

        wheelHost.MouseLeftButtonDown += (s, e) =>
        {
            dragging = true;
            wheelHost.CaptureMouse();
            PickWheel(e.GetPosition(wheelHost));
        };
        wheelHost.MouseMove += (s, e) => { if (dragging) PickWheel(e.GetPosition(wheelHost)); };
        wheelHost.MouseLeftButtonUp += (s, e) =>
        {
            if (!dragging) return;
            dragging = false;
            wheelHost.ReleaseMouseCapture();
            PickWheel(e.GetPosition(wheelHost));
            ApplyColor();
        };

        briHost.MouseLeftButtonDown += (s, e) => { briDrag = true; briHost.CaptureMouse(); PickBrightness(e.GetPosition(briHost)); };
        briHost.MouseMove += (s, e) => { if (briDrag) PickBrightness(e.GetPosition(briHost)); };
        briHost.MouseLeftButtonUp += (s, e) =>
        {
            if (!briDrag) return;
            briDrag = false;
            briHost.ReleaseMouseCapture();
            PickBrightness(e.GetPosition(briHost));
            if (hid.IsOpen && hid.SetEffect(currentMode, curR, curG, curB, curBri, speedLevel)) SetStatus("Яркость " + curBri + "%", "#3DDC84");
            cfg.Brightness = curBri; cfg.Save();
        };

        win.Closing += (s, e) =>
        {
            cfg.Left = win.Left; cfg.Top = win.Top;
            cfg.TopMost = win.Topmost;
            cfg.Save();
        };
        briHost.SizeChanged += (s, e) => UpdateBrightnessVisual();
        win.Loaded += (s, e) => UpdateBrightnessVisual();

        win.Closed += (s, e) =>
        {
            if (music != null) music.Stop();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            hid.Close();
            Application.Current.Shutdown();
        };
    }

    /// <summary>Повторный запуск приложения поднимает окно уже работающего экземпляра.</summary>
    /// <summary>Показывает и активирует окно из трея.</summary>
    private bool trayHintShown = false;

    /// <summary>Сворачивает в трей и один раз подсказывает, как вернуть окно.</summary>
    private void HideToTray()
    {
        win.Hide();
        if (!trayHintShown && tray != null)
        {
            trayHintShown = true;
            try
            {
                tray.BalloonTipTitle = "ZERGB";
                tray.BalloonTipText = "Приложение работает в трее. Клик по значку вернёт окно.";
                tray.BalloonTipIcon = WinForms.ToolTipIcon.Info;
                tray.ShowBalloonTip(4000);
            }
            catch { }
        }
    }

    private void RestoreWindow()
    {
        try
        {
            if (!win.IsVisible) win.Show();
            if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
            var helper = new System.Windows.Interop.WindowInteropHelper(win);
            Win32Show.ShowWindow(helper.Handle, Win32Show.SW_RESTORE);
            Win32Show.SetForegroundWindow(helper.Handle);
            win.Activate();
        }
        catch { }
    }

    private HwndSource hwndSource;
    private readonly System.Collections.Generic.List<int> hotKeyIds = new System.Collections.Generic.List<int>();

    /// <summary>Глобальные горячие клавиши: Ctrl+Alt+L, Ctrl+Alt+↑/↓, Ctrl+Alt+1..9.</summary>
    private void RegisterHotKeys()
    {
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(win);
            hwndSource = HwndSource.FromHwnd(helper.Handle);
            if (hwndSource == null) return;
            hwndSource.AddHook(HwndHook);

            IntPtr h = helper.Handle;
            uint mod = HotKeys.MOD_CONTROL | HotKeys.MOD_ALT;

            if (HotKeys.RegisterHotKey(h, 1, mod, 0x4C)) hotKeyIds.Add(1);          // L
            if (HotKeys.RegisterHotKey(h, 2, mod, 0x26)) hotKeyIds.Add(2);          // Up
            if (HotKeys.RegisterHotKey(h, 3, mod, 0x28)) hotKeyIds.Add(3);          // Down
            for (int i = 0; i < 9; i++)
            {
                int id = 10 + i;
                if (HotKeys.RegisterHotKey(h, id, mod, (uint)(0x31 + i))) hotKeyIds.Add(id); // 1..9
            }
            Log("hotkeys registered: " + hotKeyIds.Count);
        }
        catch (Exception ex) { Log("hotkeys failed: " + ex.Message); }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != HotKeys.WM_HOTKEY) return IntPtr.Zero;
        int id = wParam.ToInt32();
        handled = true;
        try
        {
            if (id == 1) { TogglePower(); return IntPtr.Zero; }
            if (id == 2) { curBri = Math.Min(100, curBri + 10); UpdateBrightnessVisual(); ApplyColor(); return IntPtr.Zero; }
            if (id == 3) { curBri = Math.Max(0, curBri - 10); UpdateBrightnessVisual(); ApplyColor(); return IntPtr.Zero; }
            if (id >= 10 && id < 19)
            {
                int idx = id - 10;
                if (idx < modeCodes.Count)
                {
                    currentMode = modeCodes[idx];
                    cfg.Mode = currentMode; cfg.Save();
                    ApplyColor();
                    SetStatus("Эффект: " + modeTitles[idx], "#3DDC84");
                }
            }
        }
        catch { }
        return IntPtr.Zero;
    }

    private void StartActivationListener()
    {
        try
        {
            var ev = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\ZERGBShow");
            var th = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        ev.WaitOne();
                        win.Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                if (!win.IsVisible) win.Show();
                                if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
                                var helper = new System.Windows.Interop.WindowInteropHelper(win);
                                Win32Show.ShowWindow(helper.Handle, Win32Show.SW_RESTORE);
                                Win32Show.SetForegroundWindow(helper.Handle);
                                win.Activate();
                            }
                            catch { }
                        });
                    }
                    catch { }
                }
            });
            th.IsBackground = true;
            th.Start();
        }
        catch { }
    }

    private void ToggleChannelFix()
    {
        cfg.ChannelFix = chanFixBtn.IsChecked == true;
        hid.ChannelFix = cfg.ChannelFix;
        cfg.Save();
        UpdateChanFixVisual();
        if (miFix != null) miFix.Checked = cfg.ChannelFix;
        ApplyColor();
    }

    private void UpdateChanFixVisual()
    {
        if (chanFixBtn != null)
            chanFixBtn.IsChecked = cfg.ChannelFix;
    }

    private void TogglePower()
    {
        powerOn = !powerOn;
        if (powerOn)
        {
            powerBtn.Foreground = Brush("#8A93A8");
            ApplyColor();
        }
        else
        {
            powerBtn.Foreground = Brush("#E06060");
            if (music != null) music.Stop();
            try { hid.SetOff(); } catch { }
            SetStatus("Подсветка выключена", "#8A93A8");
        }
    }

    private void PickWheel(Point p)
    {
        double dx = p.X - WheelCenter, dy = p.Y - WheelCenter;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist > WheelRadius) dist = WheelRadius;
        double hue = (Math.Atan2(-dy, dx) * 180.0 / Math.PI + 360.0) % 360.0;
        double sat = Math.Min(1.0, dist / WheelRadius);
        var rgb = HsvToRgb(hue, sat, 1.0);
        curR = rgb[0]; curG = rgb[1]; curB = rgb[2];
        UpdatePreview();
    }

    private void PickBrightness(Point p)
    {
        double w = TrackWidth;
        int pct = (int)Math.Round(100.0 * (p.X / w));
        if (p.X >= w - 6 || pct >= 97) pct = 100;   // прилипание к правому краю
        if (p.X <= 6 || pct <= 3) pct = 0;          // и к левому
        curBri = Math.Max(0, Math.Min(100, pct));
        UpdateBrightnessVisual();
    }

    private void BuildModes()
    {
        if (modePanel == null) return;
        modePanel.Children.Clear();
        var defs = new[]
        {
            new object[] { (byte)1, "Статика" },
            new object[] { (byte)2, "Дыхание" },
            new object[] { (byte)3, "Мигание" },
            new object[] { (byte)51, "2×миг." },
            new object[] { (byte)4, "Радуга" },
            new object[] { (byte)5, "Метеор" },
            new object[] { (byte)6, "Волна" },
            new object[] { (byte)7, "Рябь" },
            new object[] { (byte)8, "Случайный" },
            new object[] { (byte)200, "Музыка" },
            new object[] { (byte)52, "Переход" },
            new object[] { (byte)53, "Демо" },
            new object[] { (byte)56, "Ритм" },
            new object[] { (byte)100, "SWave" },
            new object[] { (byte)102, "SMet." },
            new object[] { (byte)103, "Дрожь" },
            new object[] { (byte)104, "Взрыв" },
            new object[] { (byte)105, "Возвр." },
            new object[] { (byte)106, "2×мет." },
            new object[] { (byte)107, "Стек" },
            new object[] { (byte)109, "Дождь" },
            new object[] { (byte)111, "DWave" },
            new object[] { (byte)112, "SW2" }
        };
        foreach (var def in defs)
        {
            byte code = (byte)def[0];
            string title = (string)def[1];
            modeCodes.Add(code); modeTitles.Add(title);
            var tb = new System.Windows.Controls.Primitives.ToggleButton();
            tb.Content = title;
            tb.Tag = code;
            tb.Style = (Style)win.FindResource("Chip");
            tb.FontSize = 10;
            tb.IsChecked = code == currentMode;
            tb.Click += (s, e) =>
            {
                currentMode = (byte)((System.Windows.Controls.Primitives.ToggleButton)s).Tag;
                cfg.Mode = currentMode; cfg.Save();
                foreach (var child in modePanel.Children)
                {
                    var t = child as System.Windows.Controls.Primitives.ToggleButton;
                    if (t != null) t.IsChecked = (byte)t.Tag == currentMode;
                }
                ApplyColor();
            };
            modePanel.Children.Add(tb);
        }
    }

    private void BuildSpeed()
    {
        if (speedSlider == null) return;
        speedSlider.Minimum = 0;
        speedSlider.Maximum = 9;
        speedSlider.TickFrequency = 1;
        speedSlider.IsSnapToTickEnabled = true;
        speedSlider.Value = speedLevel;
        speedSlider.ValueChanged += (s, e) =>
        {
            speedLevel = (int)Math.Round(speedSlider.Value);
            cfg.Speed = speedLevel; cfg.Save();
            ApplyColor();
        };
    }
    private void BuildPresets()
    {
        presetPanel.Children.Clear();
        for (int i = 0; i < cfg.Presets.Length; i++)
        {
            int idx = i;
            string hex = cfg.Presets[i];
            if (string.IsNullOrEmpty(hex) || hex.Length < 7) hex = "#FFFFFF";

            var b = new Button
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                ToolTip = hex + "  ·  ЛКМ — применить, ПКМ — сохранить",
                Template = PresetTemplate(hex)
            };
            b.Click += (s, e) =>
            {
                int r, g, bl;
                if (TryParseHex(cfg.Presets[idx], out r, out g, out bl))
                {
                    curR = r; curG = g; curB = bl;
                    UpdatePreview();
                    ApplyColor();
                }
            };
            b.MouseRightButtonUp += (s, e) =>
            {
                cfg.Presets[idx] = string.Format("#{0:X2}{1:X2}{2:X2}", curR, curG, curB);
                cfg.Save();
                BuildPresets();
        BuildModes();
        BuildSpeed();
                SetStatus("Пресет " + (idx + 1) + " сохранён", "#3DDC84");
            };
            presetPanel.Children.Add(b);
        }
    }

    private static ControlTemplate PresetTemplate(string fill)
    {
        string xaml = "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                      "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
                      "<Grid>" +
                      "<Ellipse x:Name='ring' Width='30' Height='30' Stroke='#2A3245' StrokeThickness='2' Fill='" + fill + "'/>" +
                      "<Ellipse x:Name='hover' Width='30' Height='30' Stroke='#FFFFFF' StrokeThickness='2' Opacity='0' Fill='Transparent'/>" +
                      "</Grid>" +
                      "<ControlTemplate.Triggers>" +
                      "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='hover' Property='Opacity' Value='1'/></Trigger>" +
                      "</ControlTemplate.Triggers>" +
                      "</ControlTemplate>";
        return (ControlTemplate)XamlReader.Parse(xaml);
    }

    private static bool TryParseHex(string hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (string.IsNullOrEmpty(hex)) return false;
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return false;
        return int.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            && int.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            && int.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
    }

    private static int[] HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return new[] { (int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255) };
    }

    private static double[] RgbToHsv(int r, int g, int b)
    {
        double rn = r / 255.0, gn = g / 255.0, bn = b / 255.0;
        double mx = Math.Max(rn, Math.Max(gn, bn));
        double mn = Math.Min(rn, Math.Min(gn, bn));
        double d = mx - mn;
        double h = 0;
        if (d > 1e-5)
        {
            if (mx == rn) h = 60 * (((gn - bn) / d) % 6);
            else if (mx == gn) h = 60 * (((bn - rn) / d) + 2);
            else h = 60 * (((rn - gn) / d) + 4);
            if (h < 0) h += 360;
        }
        double s = mx > 0 ? d / mx : 0;
        return new[] { h, s, mx };
    }

    // ---------- трей ----------

    private void InitTray()
    {
        tray = new WinForms.NotifyIcon();
        try { tray.Icon = LoadTrayIcon(); } catch { }
        tray.Text = "ZERGB"; Log("tray icon loaded: " + (tray.Icon != null));
        tray.Visible = true;

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Открыть виджет", null, (s, e) => { win.Show(); win.Activate(); });

        var miTop = new WinForms.ToolStripMenuItem("Поверх всех окон") { CheckOnClick = true, Checked = win.Topmost };
        miTop.Click += (s, e) => { win.Topmost = miTop.Checked; cfg.TopMost = miTop.Checked; cfg.Save(); UpdatePinVisual(); };
        menu.Items.Add(miTop);

        miFix = new WinForms.ToolStripMenuItem("Поправка каналов (R→B→G)") { CheckOnClick = true, Checked = cfg.ChannelFix };
        miFix.Click += (s, e) => { cfg.ChannelFix = miFix.Checked; hid.ChannelFix = miFix.Checked; cfg.Save(); UpdateChanFixVisual(); ApplyColor(); };
        menu.Items.Add(miFix);

        var miAuto = new WinForms.ToolStripMenuItem("Автозапуск") { CheckOnClick = true, Checked = IsAutostartOn() };
        miAuto.Click += (s, e) => SetAutostart(miAuto.Checked);
        menu.Items.Add(miAuto);

        var miKill = new WinForms.ToolStripMenuItem("Закрыть RGB Fusion");
        miKill.Click += (s, e) => CloseRgbFusionElevated();
        menu.Items.Add(miKill);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (s, e) => win.Close());

        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (s, e) => RestoreWindow();
        tray.MouseClick += (s, e) => { if (e.Button == WinForms.MouseButtons.Left) RestoreWindow(); };
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "ZERGB";

    private static bool IsAutostartOn()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunName) != null;
        }
        catch { return false; }
    }

    private void SetAutostart(bool on)
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (k == null) return;
                if (on)
                {
                    string vbs = Path.Combine(Path.GetDirectoryName(asmPath), "ZERGB.vbs");
                    string cmd = File.Exists(vbs)
                        ? "\"" + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32\\wscript.exe") + "\" \"" + vbs + "\""
                        : "\"" + asmPath + "\"";
                    k.SetValue(RunName, cmd);
                }
                else k.DeleteValue(RunName, false);
            }
        }
        catch { }
    }
}

/// <summary>Реакция на музыку: захват системного звука (WASAPI loopback) и разбор спектра.</summary>
internal sealed class MusicEngine
{
    private readonly Action<int, int, int, int> send;
    private NAudio.Wave.WasapiLoopbackCapture capture;
    private System.Windows.Threading.DispatcherTimer timer;
    private readonly System.Collections.Generic.List<float> buffer = new System.Collections.Generic.List<float>();
    private readonly object sync = new object();
    private bool active;

    public MusicEngine(Action<int, int, int, int> send) { this.send = send; }

    public void Start()
    {
        if (active) return;
        try
        {
            capture = new NAudio.Wave.WasapiLoopbackCapture();
            capture.DataAvailable += OnData;
            capture.StartRecording();
            active = true;
            timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(125) };
            timer.Tick += (s, e) => Tick();
            timer.Start();
        }
        catch { active = false; try { if (capture != null) capture.Dispose(); } catch { } capture = null; }
    }

    public void Stop()
    {
        if (!active) return;
        active = false;
        try { if (timer != null) timer.Stop(); } catch { }
        try { if (capture != null) { capture.StopRecording(); capture.Dispose(); } } catch { }
        capture = null; timer = null;
    }

    private void OnData(object s, NAudio.Wave.WaveInEventArgs e)
    {
        int channels = capture.WaveFormat.Channels;
        int frames = e.BytesRecorded / (4 * channels);
        float[] tmp = new float[frames * channels];
        Buffer.BlockCopy(e.Buffer, 0, tmp, 0, e.BytesRecorded);
        lock (sync)
        {
            for (int i = 0; i < frames; i++)
            {
                float m = 0f;
                for (int cch = 0; cch < channels; cch++) m += tmp[i * channels + cch];
                buffer.Add(m / channels);
            }
            if (buffer.Count > 8192) buffer.RemoveRange(0, buffer.Count - 8192);
        }
    }

    private void Tick()
    {
        float[] buf;
        lock (sync)
        {
            if (buffer.Count < 4096) return;
            buf = buffer.GetRange(buffer.Count - 4096, 4096).ToArray();
        }
        int fs = capture != null ? capture.WaveFormat.SampleRate : 48000;
        float bass = Goertzel(buf, fs, 60) + Goertzel(buf, fs, 120) + Goertzel(buf, fs, 250);
        float mid = Goertzel(buf, fs, 500) + Goertzel(buf, fs, 1000) + Goertzel(buf, fs, 2000);
        float treb = Goertzel(buf, fs, 4000) + Goertzel(buf, fs, 8000);

        float nb = Math.Min(1f, bass * 0.005f);
        float nm = Math.Min(1f, mid * 0.005f);
        float nt = Math.Min(1f, treb * 0.005f);

        int r = (int)(nb * 255f);
        int g = (int)(nm * 255f);
        int b = (int)(nt * 255f);
        int bright = (int)Math.Min(100, 30 + (nb + nm + nt) * 60f);

        send(r, g, b, bright);
    }

    private static float Goertzel(float[] x, int fs, double f)
    {
        int N = x.Length;
        int k = (int)Math.Round(N * f / fs);
        double w = 2.0 * Math.PI * k / N;
        double coeff = 2.0 * Math.Cos(w);
        double s0 = 0, s1 = 0, s2 = 0;
        for (int i = 0; i < N; i++) { s0 = x[i] + coeff * s1 - s2; s2 = s1; s1 = s0; }
        return (float)Math.Sqrt(s1 * s1 + s2 * s2 - coeff * s1 * s2);
    }
}
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string nm = new AssemblyName(e.Name).Name;
            if (nm == "NAudio")
            {
                using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream("NAudio.dll"))
                {
                    if (st == null) return null;
                    var ms = new MemoryStream(); st.CopyTo(ms); return Assembly.Load(ms.ToArray());
                }
            }
            return null;
        };

        bool created;
        using (var mutex = new Mutex(true, "Local\\ZERGB", out created))
        {
            if (!created)
            {
                // приложение уже работает — просим его показать окно
                try
                {
                    using (var ev = EventWaitHandle.OpenExisting("Local\\ZERGBShow"))
                        ev.Set();
                }
                catch { }
                return;
            }
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var widget = new WidgetApp();
            app.Run();
        }
    }
}
