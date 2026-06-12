# PrintBridge — LAN Printer Sharing for Windows 7/8/10/11

**Status:** Design approved (pending spec review)
**Date:** 2026-06-12
**Author:** Jassar + Claude

> Working name "PrintBridge" is a placeholder and can be renamed. We do **not**
> reuse the name "PrinterShare" — it is a trademarked commercial product.

---

## 1. Purpose

A single Windows desktop app, installed on every PC on a local network, that lets
any machine **share its printers** and **print to printers shared by other
machines** — without installing the printer's driver on the machines that print.

This is the "Approach B" model: each PC exposes a **virtual printer**. A user
prints to it from any application's normal `File → Print`. The print job is
captured, sent over the LAN to the PC that owns the real printer, and printed
there. Clients never need the real printer's driver.

### Goals
- One app, installed identically on every PC, acting as **both** sharer and user.
- Pick which local printers are **public**; only those are visible to other PCs.
- Print to a remote shared printer from **any** application via the normal print dialog.
- Works on **Windows 7 SP1, 8/8.1, 10, and 11** (32- and 64-bit).
- Simple, clear GUI. Zero manual IP configuration (automatic LAN discovery).

### Non-goals (explicitly out of scope for v1)
- Phone / tablet printing (Android/iOS).
- Internet / cloud printing — **LAN only**.
- General file transfer between PCs.
- Scanning.
- A publicly-distributable, EV-signed installer (we use self-signed / test-signing
  for our own LAN; a paid EV cert is a future option if the app is ever sold).

---

## 2. User Experience

### Single app, two roles
Every PC runs the same `PrintBridge` tray app. The main window has two tabs:

**Tab 1 — "My Printers" (sharing role)**
- Lists every printer installed locally.
- A checkbox next to each. Tick = **shared/public**; untick = private.
- Only ticked printers are advertised on the LAN. The user "selects the printers
  they want to make public so other PCs can see them."
- Shows a live job log (who printed what, success/failure).

**Tab 2 — "Network Printers" (using role)**
- Auto-discovered list of printers shared by other PCs on the LAN (PC name +
  printer name + online status).
- Select one and click **Use** → the local virtual printer is bound to it.
- From then on, any app can print to "Shared Printer (PrintBridge)" and it lands
  on that remote printer.
- Shows status of jobs this PC has sent (queued / printing / done / error).

### Print flow for the end user
1. (Once) On Tab 2, pick a remote printer and click **Use**.
2. In any app (Word, browser, PDF reader…), `File → Print` →
   choose **"Shared Printer (PrintBridge)"** → Print.
3. The job prints on the remote printer. A tray notification confirms success or
   shows the failure reason.

> If multiple remote printers are configured, the user picks the target on Tab 2
> before printing (v1 keeps it to one active mapping for simplicity; switching is
> one click). Supporting several simultaneous virtual printers is a future option.

---

## 3. Core Mechanism (how a job flows)

```
Any app  →  File→Print  →  "Shared Printer (PrintBridge)"
                              │  (Microsoft in-box PostScript driver)
                              ▼
                       mfilemon port monitor      ← captures the PostScript stream
                              │  (spawns PrintBridge.exe --capture, pipes job via stdin)
                              ▼
                    PrintBridge (capture mode)     ← attaches metadata, hands to running app
                              │  framed TCP over LAN
                              ▼
                    PrintBridge (sharing PC)       ← receives job, authorizes
                              │  bundled Ghostscript prints the PostScript to target printer
                              ▼
                       Real physical printer  →  paper
```

- **Virtual printer** on each PC = **Microsoft in-box PostScript driver**
  (pscript5, present on all Windows 7–11) bound to an **mfilemon** redirection
  port. This is the same proven chain CutePDF / PDFCreator / Bullzip use, run in
  reverse.
- **Wire payload** = the captured **PostScript** stream + a small **JSON header**
  (job id, target PC, target printer name, copies, paper size, requesting user).
- **Server-side print** = the sharing PC runs **Ghostscript** (bundled with the
  app) to render the PostScript onto the chosen real printer. Ghostscript lives on
  every install, so whichever PC owns the printer can always replay a job. The
  printing PC stays thin — it only needs to produce PostScript, which the in-box
  driver does.

---

## 4. Components

All components ship inside the one app/installer. Each has one clear purpose and a
defined interface so it can be built and tested independently.

| Component | Purpose | Depends on |
|---|---|---|
| **PrintBridge.App** (WinForms tray app) | The GUI + the long-running background service. Runs the sharing listener and discovery beacon, and the discovery listener + job sender for the using role. Two-tab window. | Protocol, Spooler, Ghostscript (when sharing) |
| **PrintBridge.App `--capture` mode** | Same exe, launched per-job by the port monitor. Reads PostScript from stdin, attaches metadata, forwards to the running app instance (via a local named pipe) to send. | Protocol |
| **PrintBridge.Protocol** (library) | UDP discovery beacons + framed TCP job transfer + JSON metadata models. Pure logic, no hardware → fully unit-testable. | — |
| **PrintBridge.Spooler** (library) | Wraps Windows spooler APIs (winspool / PrintUI / P/Invoke): enumerate local printers, install/remove the virtual printer + mfilemon port, print a PostScript file to a named printer via Ghostscript. | Windows print APIs, Ghostscript |
| **PrintBridge.Setup** (installer) | Installs the app + Ghostscript + mfilemon, applies the self-signed test cert, registers the virtual printer + port, and cleanly uninstalls all of it. | Spooler, signing |

> Keeping `--capture` as a mode of the same exe (not a second program) honors the
> "all PCs share the same app" requirement: there is literally one executable.

---

## 5. Discovery & Transport Protocol

### Discovery (UDP)
- Each app, while it has ≥1 shared printer, **broadcasts a UDP beacon** every ~3s
  on a fixed port (e.g. 49152). Beacon payload (JSON): app version, PC name, PC id,
  and the list of **shared** printer names. Private printers are never included.
- Each app also **listens** for beacons and maintains a live table of available
  remote printers, expiring entries not seen for ~10s (so offline PCs disappear).
- No manual IP entry. Pure broadcast on the local subnet.

### Job transfer (TCP)
- One TCP connection per job to the sharing PC's listener port (e.g. 49153).
- Framing: `[4-byte length][JSON header][4-byte length][PostScript payload]`.
- Header fields: `jobId`, `targetPrinter`, `copies`, `paperSize`, `requestingUser`,
  `requestingPc`, optional `pin`.
- Server responds with a JSON result: `accepted` / `rejected` + reason, then a
  final status: `printed` / `error` + detail.

### Auth (optional, LAN-scoped)
- Per-PC optional **shared PIN**. Off by default ("just works"). When set, a job
  missing/with-wrong PIN is rejected with a clear reason.
- Listener binds to the LAN interface(s) only. This is a trusted-LAN tool, not an
  internet service.

---

## 6. Tech Stack

- **Language/runtime:** C# on **.NET Framework 4.8** — the last runtime covering
  **Windows 7 SP1 → 11** with a single binary. (.NET 5+ dropped Windows 7/8.)
- **UI:** WinForms (light, simple, fast startup — good for a tray app and the
  per-job capture launches).
- **Port monitor:** **mfilemon** (open-source x64/x86 redirection port monitor),
  **self-signed** and installed under Windows **test-signing mode** per the chosen
  signing path. Free, valid for the user's own machines on their own LAN.
- **PostScript rendering on the sharing PC:** **Ghostscript** (bundled), invoked to
  print to a named Windows printer.
- **Build:** Visual Studio 2022 Community or Build Tools for VS, on the user's
  Windows PCs. Repo provides a `.sln` with all projects + exact build/install steps.

> Authoring happens on macOS; **compilation and all hardware testing happen on the
> user's Windows PCs.** The code is structured so protocol/logic can be reasoned
> about without Windows, while spooler/printer code is isolated behind interfaces.

---

## 7. Error Handling

Every failure is reported back to the printing PC and surfaced as a Windows tray
notification, and logged locally on both ends. Failure modes covered:

- Target PC unreachable / offline → job fails fast with "server offline".
- Target printer offline, out of paper, or removed → server reports the printer error.
- Auth (PIN) mismatch → "authentication failed".
- Ghostscript render error → server reports "could not render document" + detail.
- Oversized job (configurable cap) → rejected before transfer.
- Discovery: stale servers expire from the list automatically.

Jobs are **atomic**: either fully accepted and printed, or rejected with a clear,
human-readable reason. No silent partial prints.

---

## 8. Testing Strategy

### Unit tests (no Windows hardware needed)
- Protocol framing/encode/decode (TCP frames, JSON header round-trips).
- Discovery beacon encode/decode + the live server-table expiry logic.
- Ghostscript command-line construction for a given printer + options.
- Config load/save (shared-printer selections, PIN, active remote mapping).

### Integration / end-to-end (on the user's 2+ Windows PCs)
- **Paperless first:** set the sharing PC's target printer to **"Microsoft Print to
  PDF"**; print a known document from a client and verify the resulting PDF is
  correct. This proves the full capture → transport → render chain without wasting
  paper.
- **Real printer:** switch to the physical printer; verify a real page prints.
- **Cross-version matrix:** verify client→server printing across the Windows
  versions available (10/11 at minimum; 7/8 if present).
- **Discovery:** confirm a shared printer appears on other PCs within seconds and
  disappears when the sharing app closes.
- **Negative cases:** wrong PIN, server offline mid-job, printer offline.

---

## 9. Build Order (for the implementation plan)

A natural dependency order (the writing-plans step will detail tasks):
1. **Protocol** library + its unit tests (no hardware).
2. **Spooler** library: enumerate printers, print a `.ps` file via Ghostscript to a
   named printer (testable on one Windows PC with Print-to-PDF).
3. **App skeleton**: tray app, two-tab GUI, config, run sharing listener + beacon
   and discovery listener.
4. **Virtual printer install**: mfilemon + PostScript virtual printer + `--capture`
   mode wired through a named pipe to the running app.
5. **Installer**: bundle app + Ghostscript + mfilemon, self-signed test cert,
   register/unregister everything.
6. **End-to-end hardening**: error reporting, notifications, the Windows-version
   test matrix.

---

## 10. Risks & Honest Caveats

- **Test-signing mode** must be enabled once per client PC (reversible) so the
  self-signed mfilemon port monitor loads on 64-bit Win10/11. Removed only by
  buying an EV code-signing cert later.
- **PostScript fidelity:** the MS PostScript driver + Ghostscript chain reproduces
  the large majority of documents faithfully; very exotic output could differ.
  Verified during E2E with real documents.
- **Per-job process launch:** the port monitor spawns the capture process per job;
  startup must stay fast (small exe / mode), validated under load.
- **macOS authoring:** all Windows-specific code is written here but only compiled
  and proven on the user's Windows machines — the plan must schedule those Windows
  build/test checkpoints explicitly.
