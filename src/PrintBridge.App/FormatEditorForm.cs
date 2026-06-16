using System;
using System.Drawing;
using System.Windows.Forms;
using PrintBridge.Protocol;

namespace PrintBridge.App
{
    /// <summary>Add or edit a custom paper format. Dimensions are entered in millimetres
    /// (0 = use the printer's default media for that axis).</summary>
    public class FormatEditorForm : Form
    {
        private readonly TextBox _name = new TextBox();
        private readonly NumericUpDown _width = new NumericUpDown();
        private readonly NumericUpDown _height = new NumericUpDown();
        private readonly CheckBox _fit = new CheckBox();

        public PrintFormat Result { get; private set; }

        public FormatEditorForm(PrintFormat existing = null)
        {
            Text = existing == null ? "Add format" : "Edit format";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false;
            ClientSize = new Size(320, 200);

            Controls.Add(new Label { Text = "Name:", Left = 16, Top = 18, Width = 90 });
            _name.Left = 110; _name.Top = 15; _name.Width = 190;
            Controls.Add(_name);

            Controls.Add(new Label { Text = "Width (mm):", Left = 16, Top = 56, Width = 90 });
            _width.Left = 110; _width.Top = 53; _width.Width = 90;
            _width.Minimum = 0; _width.Maximum = 2000; _width.DecimalPlaces = 1;
            Controls.Add(_width);

            Controls.Add(new Label { Text = "Height (mm):", Left = 16, Top = 96, Width = 90 });
            _height.Left = 110; _height.Top = 93; _height.Width = 90;
            _height.Minimum = 0; _height.Maximum = 2000; _height.DecimalPlaces = 1;
            Controls.Add(_height);

            _fit.Text = "Scale to fit (good for receipts)";
            _fit.Left = 110; _fit.Top = 128; _fit.Width = 200;
            Controls.Add(_fit);

            var ok = new Button { Text = "OK", Left = 124, Top = 160, Width = 80, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 220, Top = 160, Width = 80, DialogResult = DialogResult.Cancel };
            ok.Click += OnOk;
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;

            if (existing != null)
            {
                _name.Text = existing.Name;
                _width.Value = (decimal)Math.Min(2000, Math.Round(existing.WidthPoints * 25.4 / 72.0, 1));
                _height.Value = (decimal)Math.Min(2000, Math.Round(existing.HeightPoints * 25.4 / 72.0, 1));
                _fit.Checked = existing.FitToPage;
            }
        }

        private void OnOk(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_name.Text))
            {
                MessageBox.Show("Give the format a name.", "PrintBridge");
                DialogResult = DialogResult.None;
                return;
            }
            Result = new PrintFormat
            {
                Name = _name.Text.Trim(),
                WidthPoints = _width.Value > 0 ? PrintFormat.Mm((double)_width.Value) : 0,
                HeightPoints = _height.Value > 0 ? PrintFormat.Mm((double)_height.Value) : 0,
                FitToPage = _fit.Checked
            };
        }
    }
}
