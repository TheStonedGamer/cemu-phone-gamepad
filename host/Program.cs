using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;

namespace CemuPhoneGamePad;

internal static class Program
{
    [STAThread] private static void Main()
    {
        using var instance = new Mutex(true, @"Local\CemuPhoneGamePad.Host", out bool first);
        ApplicationConfiguration.Initialize();
        if (!first) { MessageBox.Show("Cemu Phone GamePad is already running. Open it from the notification area.", "Cemu Phone GamePad"); return; }
        Application.Run(new HostForm());
    }
}

internal sealed class HostForm : Form
{
    private static readonly Color Ink = Color.FromArgb(23, 39, 58), Accent = Color.FromArgb(0, 117, 103);
    private readonly HostSettings settings = HostSettings.Load();
    private readonly ComboBox windows = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 660 };
    private readonly Button refresh = new() { Text = "Refresh windows", AutoSize = true };
    private readonly Button toggle = new() { Text = "Start host", AutoSize = true };
    private readonly CheckBox audio = new() { Text = "Share PC playback audio", AutoSize = true };
    private readonly CheckBox trayOption = new() { Text = "Move to notification area when minimized", AutoSize = true };
    private readonly CheckBox autoStart = new() { Text = "Start hosting when a GamePad View window is found", AutoSize = true };
    private readonly ComboBox frameRate = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly TrackBar quality = new() { Minimum = 40, Maximum = 90, TickFrequency = 10, Width = 330, Height = 45 };
    private readonly Label qualityLabel = Line("");
    private readonly Label pairing = Line("Start the host to pair a new phone", 20, true);
    private readonly Label status = Line("Host stopped");
    private readonly Label emulatorStatus = Line("Starting Cemu input…");
    private readonly Label inputStatus = Line("Phone input: waiting");
    private readonly Label addresses = Line("");
    private readonly Label windowHint = Line("");
    private readonly TextBox history = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 660, Height = 180, BorderStyle = BorderStyle.FixedSingle };
    private readonly Queue<string> log = new();
    private readonly PadSource source = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly NotifyIcon tray = new() { Icon = SystemIcons.Application, Text = "Cemu Phone GamePad" };
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill, Padding = new Point(20, 10) };
    private PhoneServer? phone;
    private PairingStore? pairings;
    private DsuServer? dsu;
    private bool busy, closing, autoAttempted;
    private int ticks;
    private string lastStatus = "";
    private static Label Line(string text, float size = 10, bool bold = false) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(660, 0), ForeColor = Ink,
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(0, 4, 0, 12)
    };
    private static Button ActionButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(10, 5, 10, 5), Margin = new Padding(0, 0, 10, 8) };
        button.Click += (_, _) => action(); return button;
    }
    private FlowLayoutPanel Page(string title)
    {
        var page = new TabPage(title) { BackColor = Color.FromArgb(246, 248, 251), Padding = new Padding(22) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        page.Controls.Add(flow); tabs.TabPages.Add(page); return flow;
    }
    private static void Section(FlowLayoutPanel page, string title, params Control[] controls)
    {
        page.Controls.Add(Line(title, 13, true));
        foreach (var control in controls) page.Controls.Add(control);
        page.Controls.Add(new Label { Height = 10, Width = 1 });
    }
    public HostForm()
    {
        Text = "Cemu Phone GamePad"; Font = new Font("Segoe UI", 10); ForeColor = Ink;
        ClientSize = new Size(800, 790); MinimumSize = new Size(760, 640); StartPosition = FormStartPosition.CenterScreen;
        var header = new Panel { Dock = DockStyle.Top, Height = 100, BackColor = Ink, Padding = new Padding(24, 14, 24, 10) };
        var title = Line("Cemu Phone GamePad", 23, true); title.ForeColor = Color.White; title.Dock = DockStyle.Top;
        var subtitle = Line("Your phone. The second screen. Every control."); subtitle.ForeColor = Color.FromArgb(157, 224, 212); subtitle.Dock = DockStyle.Bottom;
        header.Controls.Add(title); header.Controls.Add(subtitle); Controls.Add(tabs); Controls.Add(header);
        var home = Page("Dashboard");
        Section(home, "1   Choose your GamePad screen", Line("In Cemu, enable Options → Separate GamePad View. Keep it open; other windows may cover it."), windows, windowHint, refresh);
        toggle.BackColor = Accent; toggle.ForeColor = Color.White; toggle.FlatStyle = FlatStyle.Flat; toggle.Padding = new Padding(18, 8, 18, 8);
        Section(home, "2   Connect your Android phone", toggle, pairing,
            ActionButton("Copy pairing details", CopyPairing), addresses, status);
        Section(home, "3   Set up Cemu controls", ActionButton("Install Cemu button mappings…", InstallProfile),
            Line("Select Cemu's folder containing settings.xml. In Cemu Input settings, choose Controller 1 → CemuPhoneGamePad → Load."));
        Section(home, "Live connection", emulatorStatus, inputStatus);
        var preferences = Page("Settings");
        audio.Checked = settings.Audio; trayOption.Checked = settings.MinimizeToTray; autoStart.Checked = settings.AutoStart;
        frameRate.Items.AddRange(["30 fps · smooth", "15 fps · less bandwidth"]); frameRate.SelectedIndex = settings.FramesPerSecond == 15 ? 1 : 0;
        quality.Value = settings.JpegQuality; UpdateQuality();
        Section(preferences, "Streaming", audio, Line("Audio includes other PC apps. Low delay audio uses a separate UDP stream when supported by the phone."), frameRate, qualityLabel, quality,
            Line("Streaming preferences apply the next time you start the host. Lower quality or 15 fps can help on a busy Wi-Fi network."));
        Section(preferences, "App behavior", trayOption, autoStart, Line("Auto start waits for a window named GamePad. Closing this app exits and releases the controller; minimizing can keep it running."));
        Section(preferences, "Trusted phones", ActionButton("Manage saved phones…", ManagePairings), Line("Pairings survive app updates. Forget a phone here to revoke its access."));
        audio.CheckedChanged += (_, _) => SaveSettings(); trayOption.CheckedChanged += (_, _) => SaveSettings();
        autoStart.CheckedChanged += (_, _) => { autoAttempted = false; SaveSettings(); };
        frameRate.SelectedIndexChanged += (_, _) => SaveSettings(); quality.ValueChanged += (_, _) => { UpdateQuality(); SaveSettings(); };
        var diagnostics = Page("Diagnostics");
        Section(diagnostics, "Connection checklist", Line("Phone → PC: TCP 26762 on a private network.\nPC → Cemu: DSUController at 127.0.0.1:26760, Controller 1.\nAudio: negotiated UDP to the phone, or compatibility TCP audio."),
            ActionButton("Retry Cemu input", () => EnsureDsu()), ActionButton("Copy diagnostics", CopyDiagnostics));
        Section(diagnostics, "Recent activity", history, Line("Diagnostics exclude pairing codes and saved credentials."));
        var help = Page("Help & about");
        Section(help, "Quick start", Line("1. Start Cemu and open Separate GamePad View.\n2. Select that window on the Dashboard and start the host.\n3. On Android, open Menu → Connection and enter the PC address and code once.\n4. Install and load the supplied Cemu controller profile."));
        Section(help, "Troubleshooting", Line("Image but no buttons: load the CemuPhoneGamePad profile. Phone input should show a growing packet count when connected; Cemu input should report controller data requests.\n\nNo image: restore the GamePad window. If Cemu was restarted, stop the host, refresh and select the new window. Input can stay connected during capture errors.\n\nAudio delay: use phone speakers or wired headphones, Low delay audio in phone settings, and a strong Wi-Fi connection. Bluetooth adds output delay.\n\nCannot connect: allow the host on private networks in Windows Firewall, and use the PC address shown on the Dashboard."));
        Section(help, "Cemu Phone GamePad " + Assembly.GetExecutingAssembly().GetName().Version?.ToString(3),
            Line("Android + Windows · Independent software · MIT license\nCamera, microphone, NFC and rumble are not forwarded. Use a trusted LAN; streaming is not encrypted."),
            ActionButton("Downloads & updates", () => OpenUrl("https://github.com/TheStonedGamer/cemu-phone-gamepad/releases/latest")),
            ActionButton("Read setup guide", () => OpenUrl("https://github.com/TheStonedGamer/cemu-phone-gamepad#setup")));
        var menu = new ContextMenuStrip(); menu.Items.Add("Open dashboard", null, (_, _) => Restore());
        menu.Items.Add("Stop host", null, async (_, _) => await StopHost()); menu.Items.Add("Exit", null, (_, _) => Close());
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Restore();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized && trayOption.Checked) { tray.Visible = true; Hide(); } };
        refresh.Click += (_, _) => RefreshWindows(); toggle.Click += async (_, _) => { if (phone == null) StartHost(); else await StopHost(); };
        timer.Tick += (_, _) => Tick();
        Shown += (_, _) => { EnsureDsu(); RefreshWindows(); RefreshAddresses(); timer.Start(); };
        FormClosing += OnClosing;
    }
    private void UpdateQuality() => qualityLabel.Text = "Image quality: " + quality.Value + "%";
    private void SaveSettings()
    {
        settings.Audio = audio.Checked; settings.MinimizeToTray = trayOption.Checked; settings.AutoStart = autoStart.Checked;
        settings.FramesPerSecond = frameRate.SelectedIndex == 1 ? 15 : 30; settings.JpegQuality = quality.Value;
        try { settings.Save(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { AddLog("Settings could not be saved: " + e.Message); }
    }
    private void Tick()
    {
        inputStatus.Text = source.Diagnostic;
        if (++ticks % 12 == 0 && phone == null && !busy) RefreshWindows();
        if (ticks % 40 == 0) RefreshAddresses();
        if (settings.AutoStart && !autoAttempted && !busy && phone == null && windows.SelectedItem is CemuWindow selected && selected.Title.Contains("GamePad", StringComparison.OrdinalIgnoreCase))
        { autoAttempted = true; StartHost(); }
    }
    private void RefreshAddresses()
    {
        try
        {
            var values = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses).Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address.ToString()).Distinct();
            addresses.Text = "PC address: " + string.Join("  •  ", values.DefaultIfEmpty("No network — connect Wi-Fi or Ethernet")) + "\nPhone port: 26762";
        }
        catch (NetworkInformationException e) { addresses.Text = "Network information unavailable: " + e.Message; }
    }
    private void RefreshWindows()
    {
        if (phone != null || windows.DroppedDown) return;
        var selected = (windows.SelectedItem as CemuWindow)?.Handle;
        var available = GamePadScreen.Windows(); windows.Items.Clear(); windows.Items.AddRange(available);
        if (available.Length > 0) windows.SelectedIndex = Math.Max(0, Array.FindIndex(available, w => w.Handle == selected));
        windowHint.Text = available.Length == 0 ? "Waiting for Cemu. The window list refreshes automatically." : "Choose GamePad View, rather than Cemu's main game window.";
    }
    private void UpdateUi(Action action) { if (!closing && !IsDisposed && IsHandleCreated) try { BeginInvoke(action); } catch (InvalidOperationException) { } }
    private void AddLog(string value)
    {
        if (value == lastStatus) return; lastStatus = value;
        log.Enqueue($"{DateTime.Now:T}  {value}"); while (log.Count > 80) log.Dequeue();
        history.Text = string.Join(Environment.NewLine, log); history.SelectionStart = history.TextLength; history.ScrollToCaret();
    }
    private void SetStatus(string value) { status.Text = value; AddLog(value); }
    private void CopyText(string text)
    {
        try { Clipboard.SetText(text); } catch (Exception e) { MessageBox.Show(this, e.Message, "Clipboard unavailable"); }
    }
    private void CopyPairing()
    {
        if (phone == null) { SetStatus("Start the host before copying pairing details."); return; }
        CopyText(addresses.Text + "\nPairing code: " + phone.Code);
    }
    private void CopyDiagnostics() => CopyText("Cemu Phone GamePad " + Assembly.GetExecutingAssembly().GetName().Version + "\n" + Environment.OSVersion + "\n" + addresses.Text + "\n" + status.Text + "\n" + emulatorStatus.Text + "\n" + inputStatus.Text + "\n\n" + history.Text);
    private void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception e) { SetStatus(e.Message); } }
    private void Restore() { Show(); WindowState = FormWindowState.Normal; Activate(); tray.Visible = false; }
    private bool EnsureDsu()
    {
        if (dsu != null) return true;
        try
        {
            var current = new DsuServer(source); dsu = current;
            current.Status += value => UpdateUi(() => { if (dsu == current) { emulatorStatus.Text = "Cemu input: " + value; AddLog(emulatorStatus.Text); } });
            current.Failed += value => UpdateUi(async () =>
            {
                if (dsu != current) return;
                dsu = null; await Task.Run(current.Dispose);
                emulatorStatus.Text = "Cemu input stopped: " + value; AddLog(emulatorStatus.Text);
            });
            emulatorStatus.Text = "Cemu input: Controller 1 ready at 127.0.0.1:26760"; AddLog(emulatorStatus.Text); return true;
        }
        catch (Exception e) { emulatorStatus.Text = "Cannot open Cemu input on port 26760: " + e.Message; AddLog(emulatorStatus.Text); return false; }
    }
    private void StartHost()
    {
        if (busy || closing || phone != null) return;
        if (windows.SelectedItem is not CemuWindow selected) { SetStatus("Open Cemu's Separate GamePad View to get started."); return; }
        try
        {
            if (!EnsureDsu()) return;
            pairings ??= new PairingStore();
            var current = new PhoneServer(source, selected, settings.Audio, pairings, settings.FramesPerSecond, settings.JpegQuality);
            phone = current; current.Status += value => UpdateUi(() => { if (phone == current) SetStatus(value); });
            pairing.Text = "Pairing code   " + current.Code; SetStatus("Ready — connect from your phone. Saved phones reconnect automatically.");
            toggle.Text = "Stop host"; windows.Enabled = refresh.Enabled = false;
        }
        catch (Exception e) { SetStatus("Cannot start: " + e.Message); }
    }
    private async Task StopHost()
    {
        if (busy) return; busy = true; toggle.Enabled = false;
        var old = phone; phone = null; source.Release();
        try { if (old != null) await Task.Run(old.Dispose); }
        catch (Exception e) { AddLog("Stopping host: " + e.Message); }
        finally
        {
            busy = false; toggle.Enabled = true; windows.Enabled = refresh.Enabled = true; toggle.Text = "Start host";
            pairing.Text = "Start the host to pair a new phone"; SetStatus("Host stopped — Cemu controller is neutral");
        }
    }
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (closing) return;
        e.Cancel = true; Enabled = false; closing = true; timer.Stop(); tray.Visible = false;
        // If a Stop operation is already running, allow it to finish first.
        while (busy) await Task.Delay(25);
        await StopHost(); var old = dsu; dsu = null;
        if (old != null) await Task.Run(old.Dispose);
        tray.Dispose(); timer.Dispose(); Close();
    }
    private void InstallProfile()
    {
        using var folder = new FolderBrowserDialog { Description = "Select Cemu's configuration folder containing settings.xml, or its controllerProfiles folder.", UseDescriptionForTitle = true, InitialDirectory = settings.ProfileFolder };
        if (folder.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            string directory = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder.SelectedPath)).Equals("controllerProfiles", StringComparison.OrdinalIgnoreCase)
                ? folder.SelectedPath : Path.Combine(folder.SelectedPath, "controllerProfiles");
            if (!Path.GetFileName(Path.TrimEndingDirectorySeparator(folder.SelectedPath)).Equals("controllerProfiles", StringComparison.OrdinalIgnoreCase)
                && !File.Exists(Path.Combine(folder.SelectedPath, "settings.xml")))
                throw new InvalidOperationException("Choose the active Cemu configuration folder containing settings.xml, or its controllerProfiles folder.");
            Directory.CreateDirectory(directory);
            settings.ProfileFolder = folder.SelectedPath; SaveSettings();
            string destination = Path.Combine(directory, "CemuPhoneGamePad.xml");
            if (File.Exists(destination))
            {
                MessageBox.Show(this, "That profile already exists. Select CemuPhoneGamePad and click Load in Cemu's Input settings. Your existing profile was kept.", "Cemu profile");
                return;
            }
            File.Copy(Path.Combine(AppContext.BaseDirectory, "profiles", "CemuPhoneGamePad.xml"), destination);
            MessageBox.Show(this, "Profile installed. Reopen Cemu's Input settings, select CemuPhoneGamePad in the Profile list, and click Load for Controller 1. Buttons, sticks and motion are mapped.", "Cemu profile installed");
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Could not install Cemu profile"); }
    }
    private void ManagePairings()
    {
        try
        {
            pairings ??= new PairingStore();
            using var dialog = new Form { Text = "Saved phones", ClientSize = new Size(590, 320),
                StartPosition = FormStartPosition.CenterParent, Font = Font, MinimizeBox = false, MaximizeBox = false };
            var list = new ListBox { Dock = DockStyle.Fill };
            var forget = new Button { Text = "Forget selected phone", Dock = DockStyle.Bottom, Height = 44 };
            var info = new Label { Text = "Pairings survive host restarts and updates. Forgetting a connected phone releases its input and disconnects it.",
                Dock = DockStyle.Top, Height = 60, Padding = new Padding(8) };
            void RefreshList() { list.Items.Clear(); list.Items.AddRange(pairings.Devices); if (list.Items.Count > 0) list.SelectedIndex = 0; forget.Enabled = list.Items.Count > 0; }
            forget.Click += (_, _) =>
            {
                if (list.SelectedItem is not PairedDevice device) return;
                try { pairings.Forget(device.Id); RefreshList(); }
                catch (Exception e) { MessageBox.Show(dialog, e.Message, "Could not forget phone"); }
            };
            dialog.Controls.Add(list); dialog.Controls.Add(forget); dialog.Controls.Add(info);
            RefreshList(); dialog.ShowDialog(this);
        }
        catch (Exception e) { status.Text = "Cannot open saved devices: " + e.Message; }
    }
}
