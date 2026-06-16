using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        private readonly LocalRawListener _rawListener;
        private readonly AppConfig _config;
        private readonly string _configPath;

        private CheckedListBox _localList;
        private ListBox _log;
        private ListView _remoteList;
        private NotifyIcon _tray;
        private Timer _refreshTimer;

        // Formats tab
        private CheckBox _askCheck;
        private ComboBox _defaultFormatCombo;
        private ListView _formatsList;

        // About / Updates tab
        private Button _updateBtn;
        private Label _updateStatus;

        public MainForm(IPrinterService printers, DiscoveryService discovery,
            SharingService sharing, LocalRawListener rawListener, AppConfig config, string configPath)
        {
            _printers = printers; _discovery = discovery; _sharing = sharing;
            _rawListener = rawListener; _config = config; _configPath = configPath;
            BuildUi();
            _sharing.JobLogged += AppendLog;
            if (_rawListener != null)
            {
                _rawListener.JobLogged += AppendLog;
                _rawListener.ResolveRouting = ResolveRouting;   // pop the picker at print time
            }
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
            tabs.TabPages.Add(BuildFormatsTab());
            tabs.TabPages.Add(BuildAboutTab());
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

            SyncFormatsTab();

            MessageBox.Show(
                $"Default set to '{_config.ActiveRemotePrinter}' on {server.PcName}.\n\n" +
                "In any app, choose 'Shared Printer (PrintBridge)' when you print. " +
                (_config.AskBeforeEachJob
                    ? "You'll be asked to confirm the printer and format for each job."
                    : "Jobs will go straight to this default."),
                "PrintBridge");
        }

        // --- Print-time routing -----------------------------------------------------

        /// <summary>
        /// Called by LocalRawListener (on a worker thread) for each captured job. Marshals
        /// to the UI thread, then either auto-resolves the saved default or shows the picker.
        /// Returns null to cancel the job.
        /// </summary>
        private JobRouting ResolveRouting()
        {
            if (IsDisposed) return null;
            if (InvokeRequired)
                return (JobRouting)Invoke((Func<JobRouting>)ResolveRouting);

            var servers = _discovery.Registry.GetActive(DateTime.UtcNow);
            var formats = _config.AllFormats();

            // "Don't ask" + a reachable default => send silently.
            if (!_config.AskBeforeEachJob && !string.IsNullOrEmpty(_config.ActiveRemotePrinter))
            {
                var def = servers.FirstOrDefault(s => s.PcId == _config.ActiveRemotePcId);
                if (def != null && def.SharedPrinters.Contains(_config.ActiveRemotePrinter))
                    return new JobRouting
                    {
                        Server = def,
                        PrinterName = _config.ActiveRemotePrinter,
                        Copies = 1,
                        Format = ResolveFormat(formats, _config.DefaultFormatName)
                    };
                // default offline — fall through and ask.
            }

            using (var dlg = new JobRoutingForm(servers, formats, _config))
            {
                dlg.ShowDialog();
                if (dlg.Result == null) return null;     // cancelled / no printers

                if (dlg.Result.RememberAsDefault)
                {
                    _config.AskBeforeEachJob = false;
                    _config.ActiveRemotePcId = dlg.Result.Server.PcId;
                    _config.ActiveRemotePrinter = dlg.Result.PrinterName;
                    _config.DefaultFormatName = dlg.Result.Format?.Name;
                    _config.Save(_configPath);
                    SyncFormatsTab();
                }
                return dlg.Result;
            }
        }

        private static PrintFormat ResolveFormat(List<PrintFormat> formats, string name) =>
            formats.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? formats.FirstOrDefault();

        // --- Formats tab ------------------------------------------------------------

        private TabPage BuildFormatsTab()
        {
            var tab = new TabPage("Formats");

            _askCheck = new CheckBox
            {
                Text = "Ask which printer and format before every job",
                Dock = DockStyle.Top,
                Height = 28,
                Checked = _config.AskBeforeEachJob,
                Padding = new Padding(6, 4, 0, 0)
            };
            _askCheck.CheckedChanged += (s, e) =>
            {
                _config.AskBeforeEachJob = _askCheck.Checked;
                _config.Save(_configPath);
            };

            var defaultPanel = new Panel { Dock = DockStyle.Top, Height = 30 };
            defaultPanel.Controls.Add(new Label { Text = "Default format:", Dock = DockStyle.Left, Width = 100, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });
            _defaultFormatCombo = new ComboBox { Dock = DockStyle.Left, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            _defaultFormatCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_defaultFormatCombo.SelectedItem is PrintFormat f)
                {
                    _config.DefaultFormatName = f.Name;
                    _config.Save(_configPath);
                }
            };
            defaultPanel.Controls.Add(_defaultFormatCombo);

            _formatsList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
            _formatsList.Columns.Add("Format", 150);
            _formatsList.Columns.Add("Width (mm)", 90);
            _formatsList.Columns.Add("Height (mm)", 90);
            _formatsList.Columns.Add("Fit", 50);
            _formatsList.Columns.Add("Source", 80);

            var buttons = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            var addBtn = new Button { Text = "Add…", Dock = DockStyle.Left, Width = 100 };
            var editBtn = new Button { Text = "Edit…", Dock = DockStyle.Left, Width = 100 };
            var removeBtn = new Button { Text = "Remove", Dock = DockStyle.Left, Width = 100 };
            addBtn.Click += (s, e) => AddOrEditFormat(null);
            editBtn.Click += (s, e) => EditSelectedFormat();
            removeBtn.Click += (s, e) => RemoveSelectedFormat();
            buttons.Controls.Add(removeBtn);
            buttons.Controls.Add(editBtn);
            buttons.Controls.Add(addBtn);

            // Order matters for docking: Fill first, then top/bottom bars.
            tab.Controls.Add(_formatsList);
            tab.Controls.Add(buttons);
            tab.Controls.Add(defaultPanel);
            tab.Controls.Add(_askCheck);

            RefreshFormatsList();
            return tab;
        }

        private bool IsBuiltIn(string name) =>
            PrintFormat.BuiltIns().Any(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

        private void RefreshFormatsList()
        {
            var formats = _config.AllFormats();

            _formatsList.BeginUpdate();
            _formatsList.Items.Clear();
            foreach (var f in formats)
            {
                var item = new ListViewItem(f.Name);
                item.SubItems.Add(f.WidthPoints > 0 ? Math.Round(f.WidthPoints * 25.4 / 72.0, 1).ToString() : "—");
                item.SubItems.Add(f.HeightPoints > 0 ? Math.Round(f.HeightPoints * 25.4 / 72.0, 1).ToString() : "—");
                item.SubItems.Add(f.FitToPage ? "yes" : "");
                var custom = _config.CustomFormats.Any(c => string.Equals(c.Name, f.Name, StringComparison.OrdinalIgnoreCase));
                item.SubItems.Add(custom ? "custom" : "built-in");
                item.Tag = f;
                _formatsList.Items.Add(item);
            }
            _formatsList.EndUpdate();

            // Rebuild the default-format dropdown.
            _defaultFormatCombo.Items.Clear();
            foreach (var f in formats) _defaultFormatCombo.Items.Add(f);
            var sel = formats.FindIndex(f => string.Equals(f.Name, _config.DefaultFormatName, StringComparison.OrdinalIgnoreCase));
            _defaultFormatCombo.SelectedIndex = sel >= 0 ? sel : 0;
        }

        private void SyncFormatsTab()
        {
            if (_askCheck == null) return;
            _askCheck.Checked = _config.AskBeforeEachJob;
            RefreshFormatsList();
        }

        private void AddOrEditFormat(PrintFormat existing)
        {
            using (var dlg = new FormatEditorForm(existing))
            {
                if (dlg.ShowDialog() != DialogResult.OK || dlg.Result == null) return;
                // Custom formats override built-ins by name.
                _config.CustomFormats.RemoveAll(c => string.Equals(c.Name, dlg.Result.Name, StringComparison.OrdinalIgnoreCase));
                _config.CustomFormats.Add(dlg.Result);
                _config.Save(_configPath);
                RefreshFormatsList();
            }
        }

        private void EditSelectedFormat()
        {
            if (_formatsList.SelectedItems.Count == 0)
            {
                MessageBox.Show("Select a format to edit.", "PrintBridge");
                return;
            }
            AddOrEditFormat((PrintFormat)_formatsList.SelectedItems[0].Tag);
        }

        private void RemoveSelectedFormat()
        {
            if (_formatsList.SelectedItems.Count == 0)
            {
                MessageBox.Show("Select a format to remove.", "PrintBridge");
                return;
            }
            var name = _formatsList.SelectedItems[0].Text;
            var removed = _config.CustomFormats.RemoveAll(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                MessageBox.Show($"'{name}' is a built-in format and can't be removed.\n" +
                                "You can override it by adding a custom format with the same name.",
                    "PrintBridge");
                return;
            }
            _config.Save(_configPath);
            RefreshFormatsList();
        }

        // --- About / Updates tab ----------------------------------------------------

        private static string VersionText(Version v) => $"{v.Major}.{v.Minor}.{v.Build}";

        private TabPage BuildAboutTab()
        {
            var tab = new TabPage("About");
            var current = new UpdateService().CurrentVersion;

            var title = new Label
            {
                Text = $"PrintBridge\nVersion {VersionText(current)}",
                Dock = DockStyle.Top,
                Height = 56,
                Padding = new Padding(10, 10, 0, 0)
            };

            _updateBtn = new Button { Text = "Check for updates", Dock = DockStyle.Top, Height = 40 };
            _updateBtn.Click += OnCheckForUpdates;

            _updateStatus = new Label
            {
                Text = "",
                Dock = DockStyle.Top,
                Height = 80,
                Padding = new Padding(10, 8, 8, 0)
            };

            // Docking: add inner-most last so the status label sits under the button.
            tab.Controls.Add(_updateStatus);
            tab.Controls.Add(_updateBtn);
            tab.Controls.Add(title);
            return tab;
        }

        private async void OnCheckForUpdates(object sender, EventArgs e)
        {
            _updateBtn.Enabled = false;
            _updateStatus.Text = "Checking GitHub for the latest release…";
            try
            {
                var svc = new UpdateService();
                var info = await svc.GetLatestAsync();

                if (info?.Version == null)
                {
                    _updateStatus.Text = "No releases have been published yet.";
                    return;
                }

                if (!svc.IsNewer(info))
                {
                    _updateStatus.Text = $"This is the latest version ({VersionText(svc.CurrentVersion)}).";
                    return;
                }

                var notes = string.IsNullOrWhiteSpace(info.Notes) ? "" : "\n\n" + info.Notes.Trim();
                var prompt =
                    $"Version {VersionText(info.Version)} is available (you have {VersionText(svc.CurrentVersion)}).{notes}\n\n" +
                    "Download and install it now? PrintBridge will close while the installer runs.";

                if (MessageBox.Show(prompt, "PrintBridge update", MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    _updateStatus.Text = "Update postponed.";
                    return;
                }

                if (string.IsNullOrEmpty(info.DownloadUrl))
                {
                    _updateStatus.Text = "That release has no installer attached.";
                    MessageBox.Show(
                        "The latest release doesn't include a PrintBridge-Setup.exe asset, so it can't be " +
                        "installed automatically. Attach the installer to the GitHub release and try again.",
                        "PrintBridge");
                    return;
                }

                _updateStatus.Text = "Downloading the installer…";
                var installerPath = await svc.DownloadInstallerAsync(info);

                _updateStatus.Text = "Launching the installer…";
                Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    // Silent install, close+restart the app for us. UseShellExecute lets the
                    // installer's admin manifest trigger the UAC prompt.
                    Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /NORESTART",
                    UseShellExecute = true
                });

                // Exit so the installer can overwrite our files.
                _tray.Visible = false;
                Application.Exit();
            }
            catch (Exception ex)
            {
                _updateStatus.Text = "Update check failed.";
                MessageBox.Show(
                    "Couldn't check for or download the update.\n\nDetails: " + ex.Message,
                    "PrintBridge");
            }
            finally
            {
                if (!IsDisposed) _updateBtn.Enabled = true;
            }
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
