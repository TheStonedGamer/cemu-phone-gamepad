using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CemuPhoneGamePad;

internal static class Program
{
    [STAThread] private static void Main() { ApplicationConfiguration.Initialize(); Application.Run(new HostForm()); }
}

internal sealed class HostForm : Form
{
    private readonly ComboBox windows = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 680 };
    private readonly Button refresh = new() { Text = "Refresh Cemu windows", AutoSize = true };
    private readonly Button toggle = new() { Text = "Start GamePad host", AutoSize = true };
    private readonly CheckBox audio = new() { Text = "Stream PC playback audio to phone (includes other apps)", Checked = true, AutoSize = true };
    private readonly Label pairing = new() { AutoSize = true, Font = new Font("Segoe UI", 18, FontStyle.Bold) };
    private readonly Label status = new() { Text = "Stopped", AutoSize = true, MaximumSize = new Size(690, 0) };
    private readonly PadSource source = new();
    private readonly Label emulatorStatus = new() { Text = "Cemu input: starting…", AutoSize = true, MaximumSize = new Size(690, 0) };
    private readonly Label inputStatus = new() { Text = "Phone input: waiting", AutoSize = true, MaximumSize = new Size(690, 0) };
    private readonly System.Windows.Forms.Timer inputTimer = new() { Interval = 200 };
    private PhoneServer? phone;
    private PairingStore? pairings;
    private DsuServer? dsu;
    public HostForm()
    {
        Text = "Cemu Phone GamePad"; Font = new Font("Segoe UI", 11);
        ClientSize = new Size(790, 740); MinimumSize = new Size(740, 620);
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24),
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        Controls.Add(panel);
        void TextLine(string text) => panel.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(690, 0), Margin = new Padding(0, 0, 0, 16) });
        TextLine("Your Android phone as Cemu's second screen and controller");
        TextLine("1. Run Cemu and enable Options → Separate GamePad View.\n2. Select that window below. It can stay behind other windows.\n3. Start the host. Pair once in Android; saved phones reconnect without a code.");
        panel.Controls.Add(windows); panel.Controls.Add(refresh); panel.Controls.Add(audio);
        panel.Controls.Add(toggle); panel.Controls.Add(pairing); panel.Controls.Add(status);
        panel.Controls.Add(emulatorStatus); panel.Controls.Add(inputStatus);
        var retryInput = new Button { Text = "Retry Cemu input connection", AutoSize = true };
        retryInput.Click += (_, _) => EnsureDsu(); panel.Controls.Add(retryInput);
        var installProfile = new Button { Text = "Install Cemu button mappings…", AutoSize = true };
        installProfile.Click += (_, _) => InstallProfile(); panel.Controls.Add(installProfile);
        var saved = new Button { Text = "Saved devices…", AutoSize = true };
        saved.Click += (_, _) => ManagePairings(); panel.Controls.Add(saved);
        var addresses = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                && !System.Net.IPAddress.IsLoopback(a.Address)).Select(a => a.Address.ToString()).Distinct();
        TextLine("PC addresses: " + string.Join(" • ", addresses.DefaultIfEmpty("No active IPv4 address")) + "\nPhone TCP port: 26762");
        TextLine("Cemu input: Wii U GamePad → DSUController → 127.0.0.1:26760 → Controller 1. Install the included button mappings, then load the CemuPhoneGamePad profile in Cemu. Touch is supplied directly by DSU.");
        TextLine("Capture follows the selected Cemu window, even when covered. Keep GamePad View open and not minimized. Allow TCP 26762 on your private network.");
        refresh.Click += (_, _) => RefreshWindows();
        toggle.Click += (_, _) => { if (phone == null) StartHost(); else StopHost(); };
        inputTimer.Tick += (_, _) => inputStatus.Text = source.Diagnostic;
        inputTimer.Start();
        Shown += (_, _) => EnsureDsu();
        FormClosing += (_, _) => { inputTimer.Stop(); inputTimer.Dispose(); StopHost(); dsu?.Dispose(); dsu = null; };
        RefreshWindows();
    }
    private void RefreshWindows() { windows.Items.Clear(); windows.Items.AddRange(GamePadScreen.Windows()); if (windows.Items.Count > 0) windows.SelectedIndex = 0; }
    private void UpdateUi(Action action) { if (!IsDisposed && !Disposing && IsHandleCreated) try { BeginInvoke(action); } catch (InvalidOperationException) { } }
    private bool EnsureDsu()
    {
        if (dsu != null) return true;
        try
        {
            var current = new DsuServer(source); dsu = current;
            current.Status += value => UpdateUi(() => { if (dsu == current) emulatorStatus.Text = "Cemu input: " + value; });
            current.Failed += value => UpdateUi(() =>
            {
                if (dsu != current) return;
                StopHost(); dsu = null; current.Dispose();
                emulatorStatus.Text = "Cemu input stopped: " + value;
            });
            emulatorStatus.Text = "Cemu input: Controller 1 ready — DSUController at 127.0.0.1:26760";
            return true;
        }
        catch (Exception e)
        {
            emulatorStatus.Text = "Cannot open Cemu input on 127.0.0.1:26760: " + e.Message;
            return false;
        }
    }
    private void StartHost()
    {
        if (windows.SelectedItem is not CemuWindow selected) { status.Text = "Open Cemu's separate GamePad View, then refresh."; return; }
        try
        {
            if (!EnsureDsu()) return;
            pairings ??= new PairingStore();
            var current = new PhoneServer(source, selected, audio.Checked, pairings);
            phone = current;
            current.Status += value => UpdateUi(() => { if (phone == current) status.Text = value; });
            pairing.Text = "New device code: " + current.Code;
            status.Text = "Waiting for Android phone";
            toggle.Text = "Stop host"; windows.Enabled = refresh.Enabled = audio.Enabled = false;
        }
        catch (Exception e) { StopHost(); status.Text = "Cannot start: " + e.Message; }
    }
    private void InstallProfile()
    {
        using var folder = new FolderBrowserDialog { Description = "Select Cemu's configuration folder containing settings.xml, or its controllerProfiles folder.", UseDescriptionForTitle = true };
        if (folder.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            string directory = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder.SelectedPath)).Equals("controllerProfiles", StringComparison.OrdinalIgnoreCase)
                ? folder.SelectedPath : Path.Combine(folder.SelectedPath, "controllerProfiles");
            Directory.CreateDirectory(directory);
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
    private void StopHost()
    {
        var old = phone; phone = null; old?.Dispose(); source.Release();
        pairing.Text = ""; status.Text = "Phone host stopped — Controller 1 remains available with neutral input";
        toggle.Text = "Start GamePad host"; windows.Enabled = refresh.Enabled = audio.Enabled = true;
    }
}
