# PrintBridge

**PrintBridge** lets any PC on your local network print to a printer that's physically connected to a different PC — without sharing drivers, without VPNs, and without touching Windows printer sharing settings.

You install PrintBridge on the PC that has the printer. Every other PC on the network gets a virtual printer called **"Shared Printer (PrintBridge)"**. Print to it from any app and the job lands on the real printer, automatically.

---

## Who needs it

- Small offices or homes where the printer is USB-connected to one PC and others need to use it
- POS / receipt printer setups — the printer is on one machine, but multiple terminals need to print receipts
- Anyone who has tried Windows printer sharing and given up

---

## How it works

```
Your app  →  Virtual Printer  →  RAW port 9100  →  LAN  →  PrintBridge host  →  Ghostscript  →  Real printer
           (any Windows app)    (localhost TCP)            (TCP port 49153)    (PostScript → GDI)
```

1. The installer creates a local virtual printer backed by a Standard TCP/IP RAW port on `127.0.0.1:9100`.
2. When you print, Windows sends PostScript to that port.
3. PrintBridge's **listener** picks it up and asks you which networked printer to send it to (or auto-routes to your saved default).
4. The job travels over LAN to the **PrintBridge host** on the target machine.
5. The host feeds it through **Ghostscript** (bundled — no install needed) and out to the real printer.
6. Discovery is automatic: PrintBridge hosts broadcast UDP beacons on port 49152 so every client finds them without any manual IP entry.

---

## Features

| Feature | Details |
|---|---|
| Auto-discovery | Finds all PrintBridge hosts on the LAN automatically via UDP broadcast |
| Print-time picker | A dialog pops up before each job so you can choose the target printer, copies, and paper format on the fly |
| Paper format presets | Built-in: A4, Letter, A5, Receipt 80 mm, Receipt 58 mm. Add your own custom sizes in the Formats tab |
| Remember defaults | Set a default printer + format — jobs route silently without the picker dialog |
| In-app updater | About tab → Check for Updates fetches the latest GitHub release and downloads the installer for you |
| No driver signing | Uses a Standard TCP/IP RAW port — no test-signing mode, no driver certificates required |
| No reboot needed | Installs and uninstalls cleanly without prompting for a restart |
| Tray app | Runs quietly in the system tray; starts automatically at sign-in (optional) |

---

## Requirements

- Windows 10 or Windows 11 (x64 or ARM64)
- .NET Framework 4.8 — already included in Windows 10/11, no separate install needed
- Administrator rights during installation
- The printer must be installed and working on the host PC

---

## Installation

### Quick install (recommended)

1. Go to the [Releases page](https://github.com/JassarHytham/printbridge/releases/latest) and download **`PrintBridge-Setup.exe`**
2. Right-click → **Run as administrator**
3. Follow the installer — it sets up the virtual printer automatically
4. PrintBridge starts in the system tray when the installer finishes

Do this on **every PC** that needs to print. On the PC with the real printer, make sure PrintBridge is running so it can receive jobs.

### Updating

Open PrintBridge → **About** tab → **Check for Updates**. If a newer release is available, click **Download & Install** and the installer runs automatically.

---

## Using PrintBridge

### Sharing your printer (host PC)

1. Open PrintBridge from the tray icon
2. Go to the **Share** tab
3. Tick the checkboxes next to the printers you want to share
4. PrintBridge starts broadcasting them on the LAN immediately — no further setup needed

### Printing from another PC (client)

Just print normally from any application. Select **"Shared Printer (PrintBridge)"** as the printer.

- The **Job Routing** dialog will appear, showing all printers found on the LAN
- Pick a printer, set the number of copies, choose a paper format, and click **Print**
- Tick **"Remember as default"** to skip the dialog for future jobs

### Setting a default printer

1. Open PrintBridge → **Formats** tab
2. Pick your preferred printer and format from the dropdowns
3. Uncheck **"Ask before each job"** to route silently without a dialog

### Adding custom paper formats

1. Open PrintBridge → **Formats** tab → **Add**
2. Enter a name, width (mm), and height (mm)
3. Tick **Fit to page** if you want Ghostscript to scale the content to fill the paper

---

## Ports used

| Port | Protocol | Purpose |
|---|---|---|
| 9100 | TCP (localhost only) | Virtual printer RAW input |
| 49152 | UDP | Discovery beacons (broadcast) |
| 49153 | TCP | Print job delivery over LAN |

No ports need to be forwarded — all communication is LAN-local.

---

## Building from source

### Prerequisites

- [.NET SDK 8+](https://dotnet.microsoft.com/download) (targets net48, SDK 8 builds it fine)
- [Ghostscript 10.07.1 x64](https://www.ghostscript.com/releases/gsdnld.html) — copy `gswin64c.exe`, `gsdll64.dll`, and the `lib/` folder into `third_party/ghostscript/`
- [Inno Setup 6](https://jrsoftware.org/isdl.php) — only needed to build the installer

### Build

```powershell
# Build the app
dotnet build src/PrintBridge.sln -c Release

# Run tests
dotnet test tests/

# Build the installer (requires Inno Setup on PATH)
iscc installer/PrintBridge.iss
# Output: installer/Output/PrintBridge-Setup.exe
```

### Project structure

```
src/
  PrintBridge.App/          WinForms tray app, UI, update service
  PrintBridge.Protocol/     Shared types: JobHeader, PrintFormat
  PrintBridge.Spooler/      Ghostscript integration, printer enumeration
installer/
  PrintBridge.iss           Inno Setup script
  install-virtual-printer.ps1
  uninstall-virtual-printer.ps1
third_party/
  ghostscript/              Ghostscript binaries (not in git — see SOURCE.txt)
tests/
  PrintBridge.Protocol.Tests/
  PrintBridge.Spooler.Tests/
```

---

## Troubleshooting

**Virtual printer not appearing after install**
Run `install-virtual-printer.ps1` manually as Administrator and check the log at `C:\ProgramData\PrintBridge\install-printer.log`.

**No printers showing in the Job Routing dialog**
Make sure PrintBridge is running on the host PC and that the **Share** tab has at least one printer ticked. Check that Windows Firewall isn't blocking UDP port 49152.

**Jobs stuck / not printing**
Open the PrintBridge tray on the host PC — the status bar shows the last job. If Ghostscript fails, the error message appears there.

**Update check fails**
Requires an internet connection to reach `api.github.com`. Corporate proxies may block it.

---

## License

MIT — see [LICENSE](LICENSE).
