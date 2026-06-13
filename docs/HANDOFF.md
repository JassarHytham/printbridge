# PrintBridge — Technical Handoff

> This document is a complete handoff for a Claude agent continuing work on the PrintBridge project on a Windows machine. Read it fully before touching any code.

---

## What This App Does

**PrintBridge** is a LAN printer sharing tool for Windows 7/8/10/11 — similar to the commercial "PrinterShare" product. One app runs on every PC. Users tick which local printers they want to share; other PCs on the same network see those printers and can print to them from any application.

**Key design goals:**
- Single `.exe` on every PC — no separate server/client apps
- No cloud, no internet — pure LAN
- Works on x64 and ARM64 Windows (tested on ARM64 Windows 11)
- No driver signing, no bcdedit test-signing, no kernel DLLs

---

## Architecture

```
SENDING PC                              RECEIVING PC
----------                              ------------
Any app
  → File → Print
  → "Shared Printer (PrintBridge)"      
  → Windows PS Class Driver             
  → Standard TCP/IP RAW port            
  → localhost:9100                       
  → LocalRawListener (in our app)       
  → JobSender (TCP, port 49153)    -->  SharingService (TCP listener)
                                         → Ghostscript (renders PS)
                                         → Real printer
```

**Discovery:** UDP broadcast on port 49152. Each PC broadcasts a beacon every 3 seconds listing its shared printers. Beacons expire after 10 seconds. The "Network Printers" tab refreshes every 2 seconds.

**Why Standard TCP/IP RAW port instead of mfilemon:**  
The original design used mfilemon (a port monitor DLL). mfilemon has no ARM64 build — port monitor DLLs must be native to the CPU running the print spooler. Standard TCP/IP RAW port (Windows built-in TCPMON.DLL) works on all architectures. This also eliminates test-signing mode and bcdedit.

---

## Repository

**GitHub:** https://github.com/JassarHytham/printbridge  
**Working branch:** `feature/printbridge-implementation`  
**Local path on Windows:** `C:\printbridge`

```
git clone https://github.com/JassarHytham/printbridge.git C:\printbridge
cd C:\printbridge
git checkout feature/printbridge-implementation
```

---

## Project Structure

```
PrintBridge.sln                          Visual Studio solution (6 projects)

src/
  PrintBridge.Protocol/                  Network message types (no Windows deps)
    Json.cs                              Newtonsoft.Json wrapper
    FrameCodec.cs                        Length-prefixed TCP framing
    DiscoveryBeacon.cs                   UDP broadcast message
    JobHeader.cs                         Print job metadata
    JobResult.cs                         Job outcome (Accepted/Rejected/Printed/Error)
    ServerEntry.cs                       A discovered peer PC
    ServerRegistry.cs                    TTL-based in-memory peer registry

  PrintBridge.Spooler/                   Printing layer (Windows-specific)
    IPrinterService.cs                   Interface: ListLocalPrinters + PrintPostScript
    PrinterEnumerator.cs                 Wraps PrinterSettings.InstalledPrinters
    GhostscriptCommand.cs                Builds gswin64c.exe argument string
    GhostscriptPrinter.cs               Implements IPrinterService via Ghostscript
    VirtualPrinterInstaller.cs           Checks/installs "Shared Printer (PrintBridge)"
    EnumOnlyPrinterService.cs           Fallback when Ghostscript isn't bundled yet

  PrintBridge.App/                       WinForms app (the single exe users run)
    Program.cs                           Entry point, wires all services, runs GUI
    AppConfig.cs                         JSON config in %LOCALAPPDATA%\PrintBridge\
    MachineIdentity.cs                   Stable per-machine GUID (pcid.txt)
    SharingService.cs                    UDP beacons + TCP job receiver (port 49153)
    DiscoveryService.cs                  UDP listener, maintains ServerRegistry
    LocalRawListener.cs                  TCP listener on 127.0.0.1:9100 (captures jobs)
    JobSender.cs                         Sends job to remote PC over TCP
    MainForm.cs                          Two-tab WinForms GUI + system tray

tests/
  PrintBridge.Protocol.Tests/            13 unit tests (FrameCodec, Beacon, Header, etc.)
  PrintBridge.Spooler.Tests/             3 unit tests (GhostscriptCommand)
  PrintBridge.App.Tests/                 3 tests including loopback integration test

installer/
  install-virtual-printer.ps1           Creates "Shared Printer (PrintBridge)" printer
  uninstall-virtual-printer.ps1         Removes it
  PrintBridge.iss                        Inno Setup 6 installer script

third_party/
  ghostscript/bin/gswin64c.exe          NOT in git — copy from Ghostscript install
  ghostscript/bin/gsdll64.dll           NOT in git
  ghostscript/lib/*                      NOT in git
  ghostscript/SOURCE.txt                Instructions for adding these files
```

---

## Ports Used

| Port | Protocol | Purpose |
|------|----------|---------|
| 49152 | UDP broadcast | Discovery beacons (3s interval, 10s TTL) |
| 49153 | TCP | Print job transfer (one connection per job) |
| 9100 | TCP loopback only | Capture from virtual printer (localhost only) |

---

## Current State (as of June 13, 2026)

### What is fully working ✅
- All 19 unit + integration tests pass (`dotnet test`)
- App builds cleanly: `dotnet build PrintBridge.sln -c Release`
- App runs on ARM64 Windows 11
- Virtual printer "Shared Printer (PrintBridge)" installed using Microsoft PS Class Driver (ARM64 native)
- Printing to the virtual printer from Notepad successfully captures the job (log shows "No remote printer selected — job dropped" as expected for single-PC test)
- Full local pipeline confirmed: virtual printer → port 9100 → LocalRawListener → job routing logic
- Installer built: `C:\printbridge\installer\Output\PrintBridge-Setup.exe`
- Ghostscript 10.07.1 bundled in `third_party/ghostscript/`

### What has NOT been tested yet ❌
- **Two-PC end-to-end test** — this is the immediate next step
- Ghostscript actually rendering a job and sending it to a real printer
- Discovery (seeing another PC's shared printers in "Network Printers" tab)
- PIN authentication
- Offline server fallback behavior
- Windows 10/11 x64 (only ARM64 tested so far)
- Windows 7/8 (low priority)

---

## Immediate Next Step: Two-PC Test

### Setup
1. Copy `C:\printbridge\installer\Output\PrintBridge-Setup.exe` to a second PC (USB or network share)
2. On the second PC: run `PrintBridge-Setup.exe` — it installs everything automatically

### Test procedure
**On PC-A (this ARM machine):**
- Open PrintBridge → My Printers tab → tick "Microsoft Print to PDF" (already done)

**On PC-B:**
- Open PrintBridge → Network Printers tab
- Within ~5 seconds, PC-A's name + "Microsoft Print to PDF" should appear
- Select it → click "Use selected printer"
- Open Notepad → File → Print → "Shared Printer (PrintBridge)" → Print

**Expected result on PC-A:**
- Log shows: "Printed on [PC-B name]"
- A "Save PDF" dialog appears (because the target is Microsoft Print to PDF)

**If PC-B's Network Printers tab is empty after 10 seconds:**
- Check Windows Firewall on PC-A — open UDP 49152 and TCP 49153:
  ```powershell
  New-NetFirewallRule -DisplayName "PrintBridge Discovery" -Direction Inbound -Protocol UDP -LocalPort 49152 -Action Allow
  New-NetFirewallRule -DisplayName "PrintBridge Jobs"      -Direction Inbound -Protocol TCP -LocalPort 49153 -Action Allow
  ```
- Both PCs must be on the same LAN/WiFi (UDP broadcast doesn't cross routers)

---

## Build Commands

```powershell
# From C:\printbridge

# Restore + build
dotnet restore PrintBridge.sln
dotnet build PrintBridge.sln -c Release

# Run tests (no hardware needed)
dotnet test tests\PrintBridge.Protocol.Tests
dotnet test tests\PrintBridge.Spooler.Tests
dotnet test tests\PrintBridge.App.Tests

# Build installer (requires Ghostscript in third_party\ first)
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\PrintBridge.iss
# Output: installer\Output\PrintBridge-Setup.exe

# Run the app directly (without installing)
src\PrintBridge.App\bin\Release\net48\PrintBridge.App.exe

# Set up virtual printer manually (run as Administrator)
powershell -ExecutionPolicy Bypass -File installer\install-virtual-printer.ps1
```

---

## Build Machine Requirements

These are only needed on the machine that BUILDS the installer — not on end-user PCs:

| Tool | Purpose | Notes |
|------|---------|-------|
| .NET SDK 9+ | Build + test | Any recent SDK works; targets net48 |
| VS Build Tools 2022 | .NET 4.8 targeting pack | Needed for net48 target |
| Ghostscript 10.x (installed) | Copy files to third_party/ | Only for building installer |
| Inno Setup 6 | Build `PrintBridge-Setup.exe` | `C:\Program Files (x86)\Inno Setup 6\` |
| Git | Source control | — |

End-user PCs need NONE of these — just the installer.

---

## Key Fixes Applied During Development

| # | Problem | Fix |
|---|---------|-----|
| 1 | `CS0160`: unreachable `catch(EndOfStreamException)` | Removed — it's a subclass of `IOException` |
| 2 | `CS1061`: `IReadOnlyList<string>` no `.Contains()` | Added `using System.Linq;` |
| 3 | mfilemon has no ARM64 build | Replaced with Standard TCP/IP RAW port on localhost:9100 |
| 4 | Project cloned into `C:\Windows\System32` | 32-bit iscc.exe got WOW64-redirected; moved to `C:\printbridge` |
| 5 | Em dash `—` in PowerShell `throw` string | Replaced with ASCII hyphen `-` |
| 6 | Ghostscript version path wrong | Found actual path with `Get-ChildItem "C:\Program Files\gs"` |
| 7 | `Microsoft PS Class Driver` not in `Get-PrinterDriver` | `Add-PrinterDriver -Name "Microsoft PS Class Driver"` installs it from driver store |

---

## Config File

Stored at: `%LOCALAPPDATA%\PrintBridge\config.json`

```json
{
  "SharedPrinters": ["Microsoft Print to PDF"],
  "Pin": null,
  "ActiveRemotePcId": "...",
  "ActiveRemotePrinter": "..."
}
```

- `SharedPrinters` — printers this PC broadcasts (ticked in My Printers tab)
- `ActiveRemotePcId` / `ActiveRemotePrinter` — the remote printer this PC currently prints to

---

## Ghostscript Setup (build machine only)

```powershell
$gs = "C:\Program Files\gs\gs10.07.1"   # adjust version

New-Item -ItemType Directory -Force "third_party\ghostscript\bin"
New-Item -ItemType Directory -Force "third_party\ghostscript\lib"
Copy-Item "$gs\bin\gswin64c.exe" "third_party\ghostscript\bin\"
Copy-Item "$gs\bin\gsdll64.dll"  "third_party\ghostscript\bin\"
Copy-Item "$gs\lib\*"            "third_party\ghostscript\lib\" -Recurse
```

Ghostscript binaries are excluded from git (`.gitignore`). They must be re-copied whenever you clone fresh.

---

## Notes for the Continuing Agent

1. **The virtual printer on the current machine is already installed.** Do not run the install script again unless something is broken.

2. **The installer is already built** at `C:\printbridge\installer\Output\PrintBridge-Setup.exe`. You don't need to rebuild it unless code changes.

3. **Focus: two-PC end-to-end test.** This is the only remaining unverified piece. If it works, the app is feature-complete.

4. **If Ghostscript fails to print:** Check that the job is arriving (log shows "Sending job to..."), then check Ghostscript stderr (GhostscriptPrinter.cs captures it and throws `InvalidOperationException` with the message).

5. **If discovery doesn't work:** Windows Firewall is the first suspect. The app doesn't auto-create firewall rules — the installer should do this. Consider adding firewall rules to `install-virtual-printer.ps1`.

6. **The app hides to tray on close.** Right-click tray icon → Exit to actually quit. Or kill via Task Manager.

7. **Single instance guard:** Only one copy of the app runs at a time (Mutex). If the app seems unresponsive, check the tray or Task Manager.
