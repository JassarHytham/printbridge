using System;
using System.IO;
using System.Windows.Forms;
using PrintBridge.Protocol;
using PrintBridge.Spooler;

namespace PrintBridge.App
{
    public class MainForm : Form
    {
        private readonly IPrinterService _printers;
        private readonly DiscoveryService _discovery;
        private readonly SharingService _sharing;
        private readonly CaptureServer _capture;
        private readonly AppConfig _config;
        private readonly string _configPath;

        private CheckedListBox _localList;
        private ListBox _log;
        private ListView _remoteList;
        private NotifyIcon _tray;
        private Timer _refreshTimer;

        public MainForm(IPrinterService printers, DiscoveryService discovery,
            SharingService sharing, CaptureServer capture, AppConfig config, string configPath)
        {
            _printers = printers; _discovery = discovery; _sharing = sharing;
            _capture = capture; _config = config; _configPath = configPath;
            BuildUi();
            _sharing.JobLogged += AppendLog;
            if (_capture != null) _capture.JobLogged += AppendLog;
        }

        private void AppendLog(string line)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    _log.Items.Insert(0, $"{DateTime.Now:T}  {line}");
                    ShowToast(line);
                }));
            }
            catch (InvalidOperationException) { /* handle not created yet */ }
        }

        private void ShowToast(string line)
        {
            // Surface important events as tray balloons.
            if (line.IndexOf("Printed", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("Rejected", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("offline", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _tray?.ShowBalloonTip(3000, "PrintBridge", line, ToolTipIcon.Info);
            }
        }

        private void BuildUi()
        {
            Text = "PrintBridge";
            Width = 640; Height = 460;
            StartPosition = FormStartPosition.CenterScreen;

            var tabs = new TabControl { Dock = DockStyle.Fill };

            // Tab 1 — My Printers
            var myTab = new TabPage("My Printers");
            _localList = new CheckedListBox { Dock = DockStyle.Top, Height = 180, CheckOnClick = true };
            foreach (var p in _printers.ListLocalPrinters())
                _localList.Items.Add(p, _config.SharedPrinters.Contains(p));
            _localList.ItemCheck += OnLocalCheck;
            _log = new ListBox { Dock = DockStyle.Fill };
            myTab.Controls.Add(_log);
            myTab.Controls.Add(_localList);
            myTab.Controls.Add(new Label
            {
                Text = "Tick printers to share on the network:",
                Dock = DockStyle.Top,
                Height = 22
            });

            // Tab 2 — Network Printers
            var netTab = new TabPage("Network Printers");
            _remoteList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
            _remoteList.Columns.Add("PC", 160);
            _remoteList.Columns.Add("Printer", 220);
            _remoteList.Columns.Add("IP", 140);
            var useBtn = new Button { Text = "Use selected printer", Dock = DockStyle.Bottom, Height = 36 };
            useBtn.Click += OnUseClicked;
            netTab.Controls.Add(_remoteList);
            netTab.Controls.Add(useBtn);

            tabs.TabPages.Add(myTab);
            tabs.TabPages.Add(netTab);
            Controls.Add(tabs);

            _tray = new NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Visible = true,
                Text = "PrintBridge"
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Show", null, (s, e) => RestoreWindow());
            menu.Items.Add("Exit", null, (s, e) => { _tray.Visible = false; Application.Exit(); });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => RestoreWindow();

            _refreshTimer = new Timer { Interval = 2000 };
            _refreshTimer.Tick += (s, e) => RefreshRemote();
            _refreshTimer.Start();
        }

        private void RestoreWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void OnLocalCheck(object sender, ItemCheckEventArgs e)
        {
            var name = _localList.Items[e.Index].ToString();
            var nowChecked = e.NewValue == CheckState.Checked;
            BeginInvoke((Action)(() =>
            {
                if (nowChecked)
                {
                    if (!_config.SharedPrinters.Contains(name)) _config.SharedPrinters.Add(name);
                }
                else _config.SharedPrinters.Remove(name);
                _config.Save(_configPath);
            }));
        }

        private void RefreshRemote()
        {
            var servers = _discovery.Registry.GetActive(DateTime.UtcNow);
            _remoteList.BeginUpdate();
            _remoteList.Items.Clear();
            foreach (var s in servers)
                foreach (var printer in s.SharedPrinters)
                {
                    var item = new ListViewItem(s.PcName);
                    item.SubItems.Add(printer);
                    item.SubItems.Add(s.IpAddress);
                    item.Tag = s;
                    _remoteList.Items.Add(item);
                }
            _remoteList.EndUpdate();
        }

        private void OnUseClicked(object sender, EventArgs e)
        {
            if (_remoteList.SelectedItems.Count == 0)
            {
                MessageBox.Show("Select a printer from the list first.", "PrintBridge");
                return;
            }
            var item = _remoteList.SelectedItems[0];
            var server = (ServerEntry)item.Tag;
            _config.ActiveRemotePcId = server.PcId;
            _config.ActiveRemotePrinter = item.SubItems[1].Text;
            _config.Save(_configPath);

            // Ensure the virtual printer exists so any app can print to it.
            var installer = new VirtualPrinterInstaller();
            var script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "installer", "install-virtual-printer.ps1");
            try { installer.EnsureInstalled(script); }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not set up the virtual printer automatically.\n" +
                    "Run the installer as Administrator.\n\nDetails: " + ex.Message,
                    "PrintBridge");
            }

            MessageBox.Show(
                $"Now printing to '{_config.ActiveRemotePrinter}' on {server.PcName}.\n\n" +
                "In any app, choose 'Shared Printer (PrintBridge)' when you print.",
                "PrintBridge");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;     // hide to tray instead of exiting
                Hide();
            }
            base.OnFormClosing(e);
        }
    }
}
