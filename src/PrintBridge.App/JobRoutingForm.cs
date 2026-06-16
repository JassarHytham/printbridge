using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PrintBridge.Protocol;

namespace PrintBridge.App
{
    /// <summary>The target + settings the user picked for a single print job.</summary>
    public class JobRouting
    {
        public ServerEntry Server { get; set; }
        public string PrinterName { get; set; }
        public int Copies { get; set; } = 1;
        public PrintFormat Format { get; set; }
        public bool RememberAsDefault { get; set; }
    }

    /// <summary>One pickable target: a shared printer on a discovered PC.</summary>
    internal class RoutingOption
    {
        public ServerEntry Server { get; set; }
        public string PrinterName { get; set; }
        public override string ToString() => $"{PrinterName}   —   {Server.PcName} ({Server.IpAddress})";
    }

    /// <summary>
    /// Shown when a job hits the virtual printer (if "ask before each job" is on):
    /// lets the user choose which shared printer to send to, the number of copies,
    /// and the paper format, with an option to remember the choice as the default.
    /// </summary>
    public class JobRoutingForm : Form
    {
        private readonly ComboBox _target = new ComboBox();
        private readonly NumericUpDown _copies = new NumericUpDown();
        private readonly ComboBox _format = new ComboBox();
        private readonly CheckBox _remember = new CheckBox();

        public JobRouting Result { get; private set; }

        public JobRoutingForm(IReadOnlyList<ServerEntry> servers, IReadOnlyList<PrintFormat> formats,
            AppConfig config)
        {
            Text = "PrintBridge — where should this go?";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false; MaximizeBox = false;
            ClientSize = new Size(440, 230);
            TopMost = true;

            var options = BuildOptions(servers);

            // Target printer
            Controls.Add(new Label { Text = "Send to:", Left = 16, Top = 18, Width = 80 });
            _target.DropDownStyle = ComboBoxStyle.DropDownList;
            _target.Left = 100; _target.Top = 15; _target.Width = 320;
            foreach (var o in options) _target.Items.Add(o);
            Controls.Add(_target);

            // Copies
            Controls.Add(new Label { Text = "Copies:", Left = 16, Top = 58, Width = 80 });
            _copies.Left = 100; _copies.Top = 55; _copies.Width = 80;
            _copies.Minimum = 1; _copies.Maximum = 99; _copies.Value = 1;
            Controls.Add(_copies);

            // Format
            Controls.Add(new Label { Text = "Format:", Left = 16, Top = 98, Width = 80 });
            _format.DropDownStyle = ComboBoxStyle.DropDownList;
            _format.Left = 100; _format.Top = 95; _format.Width = 320;
            foreach (var f in formats) _format.Items.Add(f);
            Controls.Add(_format);

            // Remember
            _remember.Text = "Always use this (don't ask again)";
            _remember.Left = 100; _remember.Top = 132; _remember.Width = 320;
            Controls.Add(_remember);

            // Buttons
            var ok = new Button { Text = "Print", Left = 244, Top = 178, Width = 80, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 340, Top = 178, Width = 80, DialogResult = DialogResult.Cancel };
            ok.Click += OnOk;
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;

            // Pre-select the remembered default, else first item.
            SelectDefault(options, formats, config);

            if (options.Count == 0)
            {
                _target.Items.Add("(no shared printers found on the network)");
                _target.SelectedIndex = 0;
                _target.Enabled = false;
                ok.Enabled = false;
            }
        }

        private static List<RoutingOption> BuildOptions(IReadOnlyList<ServerEntry> servers)
        {
            var list = new List<RoutingOption>();
            foreach (var s in servers)
                foreach (var printer in s.SharedPrinters)
                    list.Add(new RoutingOption { Server = s, PrinterName = printer });
            return list;
        }

        private void SelectDefault(List<RoutingOption> options, IReadOnlyList<PrintFormat> formats,
            AppConfig config)
        {
            int targetIdx = 0;
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Server.PcId == config.ActiveRemotePcId
                    && options[i].PrinterName == config.ActiveRemotePrinter)
                {
                    targetIdx = i;
                    break;
                }
            }
            if (options.Count > 0) _target.SelectedIndex = targetIdx;

            int formatIdx = 0;
            for (int i = 0; i < formats.Count; i++)
            {
                if (string.Equals(formats[i].Name, config.DefaultFormatName, StringComparison.OrdinalIgnoreCase))
                {
                    formatIdx = i;
                    break;
                }
            }
            if (formats.Count > 0) _format.SelectedIndex = formatIdx;
        }

        private void OnOk(object sender, EventArgs e)
        {
            var opt = _target.SelectedItem as RoutingOption;
            if (opt == null)
            {
                MessageBox.Show("Choose a printer first.", "PrintBridge");
                DialogResult = DialogResult.None;
                return;
            }
            Result = new JobRouting
            {
                Server = opt.Server,
                PrinterName = opt.PrinterName,
                Copies = (int)_copies.Value,
                Format = _format.SelectedItem as PrintFormat,
                RememberAsDefault = _remember.Checked
            };
        }
    }
}
