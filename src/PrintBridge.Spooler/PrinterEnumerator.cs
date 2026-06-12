using System.Collections.Generic;
using System.Drawing.Printing;

namespace PrintBridge.Spooler
{
    public class PrinterEnumerator
    {
        public IReadOnlyList<string> ListLocalPrinters()
        {
            var names = new List<string>();
            foreach (string name in PrinterSettings.InstalledPrinters)
                names.Add(name);
            return names;
        }
    }
}
