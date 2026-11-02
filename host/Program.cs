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
    private PhoneServer? phone;
    private DsuServer? dsu;
    public HostForm()
    {
        Text = "Cemu Phone GamePad"; Font = new Font("Segoe UI", 11);
        ClientSize = new Size(760, 590); MinimumSize = new Size(720, 560);
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24),
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        Controls.Add(panel);
        void TextLine(string text) => panel.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(690, 0), Margin = new Padding(0, 0, 0, 16) });
        TextLine("Your Android phone as Cemu's second screen and controller");
        TextLine("1. Run Cemu and enable Options → Separate GamePad View.\n2. Select that window below. Keep it visible and unobscured.\n3. Start the host, then enter the PC address and pairing code in the Android app.");
        panel.Controls.Add(windows); panel.Controls.Add(refresh); panel.Controls.Add(audio);
        panel.Controls.Add(toggle); panel.Controls.Add(pairing); panel.Controls.Add(status);
        var addresses = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                && !System.Net.IPAddress.IsLoopback(a.Address)).Select(a => a.Address.ToString()).Distinct();
        TextLine("PC addresses: " + string.Join(" • ", addresses.DefaultIfEmpty("No active IPv4 address")) + "\nPhone TCP port: 26762");
        TextLine("Cemu input: Wii U GamePad → DSUController → 127.0.0.1:26760 → Controller 1. Enable motion in Settings and map buttons/sticks once. Touch is supplied directly by DSU.");
        TextLine("Screen sharing captures the selected window's visible desktop area. Anything covering it will also appear on the phone. Allow TCP 26762 on your private network.");
        refresh.Click += (_, _) => RefreshWindows();
        toggle.Click += (_, _) => { if (phone == null) StartHost(); else StopHost(); };
        FormClosing += (_, _) => StopHost(); RefreshWindows();
    }
    private void RefreshWindows() { windows.Items.Clear(); windows.Items.AddRange(GamePadScreen.Windows()); if (windows.Items.Count > 0) windows.SelectedIndex = 0; }
    private void UpdateUi(Action action) { if (!IsDisposed && !Disposing && IsHandleCreated) try { BeginInvoke(action); } catch (InvalidOperationException) { } }
    private void StartHost()
    {
        if (windows.SelectedItem is not CemuWindow selected) { status.Text = "Open Cemu's separate GamePad View, then refresh."; return; }
        try
        {
            var source = new PadSource();
            dsu = new DsuServer(source);
            var current = new PhoneServer(source, selected, audio.Checked);
            phone = current;
            current.Status += value => UpdateUi(() => { if (phone == current) status.Text = value; });
            dsu.Failed += value => UpdateUi(() => { if (phone == current) { StopHost(); status.Text = "DSU stopped: " + value; } });
            pairing.Text = "Pairing code: " + current.Code;
            status.Text = "Waiting for Android phone";
            toggle.Text = "Stop host"; windows.Enabled = refresh.Enabled = audio.Enabled = false;
        }
        catch (Exception e) { StopHost(); status.Text = "Cannot start: " + e.Message; }
    }
    private void StopHost()
    {
        var old = phone; phone = null; old?.Dispose(); dsu?.Dispose(); dsu = null;
        pairing.Text = ""; status.Text = "Stopped — all inputs released";
        toggle.Text = "Start GamePad host"; windows.Enabled = refresh.Enabled = audio.Enabled = true;
    }
}
