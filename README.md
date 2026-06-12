# PrintBridge

LAN printer sharing for Windows 7 / 8 / 10 / 11. One app on every PC: tick the
printers you want to share, and print to other PCs' shared printers from any app —
**no printer driver needed on the printing PC** (jobs are captured as PostScript
and rendered on the PC that owns the printer).

> Working name. See `docs/superpowers/specs/2026-06-12-printbridge-design.md` for
> the full design and `docs/superpowers/plans/2026-06-12-printbridge.md` for the
> task-by-task plan.

## How it works

```
Any app → File→Print → "Shared Printer (PrintBridge)"
        → mfilemon port monitor captures PostScript
        → PrintBridge.exe --capture → named pipe → running app
        → TCP over the LAN → sharing PC → Ghostscript → real printer
```

Discovery is automatic (UDP broadcast); no IP typing. Optional per-PC PIN.

## Prerequisites (Windows build machine)

- **Visual Studio 2022** (Community is fine) **or Build Tools for VS** with the
  **.NET Framework 4.8 targeting pack**.
- **.NET SDK 8+** (for `dotnet build` / `dotnet test` against `net48`).
- **Inno Setup 6** (`iscc`) to build the installer.
- **Windows SDK** `signtool` (for signing the port monitor DLL).

> .NET Framework 4.8 **runtime** is needed to RUN the app. It ships with Windows 10/11.
> For Windows 7 SP1 / 8.1, install it first:
> https://dotnet.microsoft.com/download/dotnet-framework/net48

## Build & test (run on Windows)

```bat
:: from the repo root
dotnet restore PrintBridge.sln
dotnet build  PrintBridge.sln -c Release

:: unit tests (no hardware needed)
dotnet test tests\PrintBridge.Protocol.Tests
dotnet test tests\PrintBridge.Spooler.Tests

:: app config + loopback integration tests
dotnet test tests\PrintBridge.App.Tests
```

All unit tests should pass with no printer or network involved. The loopback test
in `PrintBridge.App.Tests` exercises the full job path on a single machine using a
fake printer.

## Add the bundled binaries (before building the installer)

These are not committed (size/licensing). See the SOURCE.txt in each folder:

- `third_party/ghostscript/` — Ghostscript console binary + lib.
- `third_party/mfilemon/x64/` and `/x86/` — the mfilemon port monitor DLL.

## Signing (self-signed / test mode — for your own LAN)

```bat
:: 1. make a self-signed code-signing cert
powershell -ExecutionPolicy Bypass -File installer\make-test-cert.ps1 -PfxPassword "<pw>"

:: 2. sign the port monitor DLL
signtool sign /f installer\certs\PrintBridge.pfx /p "<pw>" /fd SHA256 third_party\mfilemon\x64\mfilemon.dll

:: 3. enable test-signing on each client PC (one-time; needs reboot)
bcdedit /set testsigning on
```

## Build the installer

```bat
dotnet build src\PrintBridge.App\PrintBridge.App.csproj -c Release
iscc installer\PrintBridge.iss
:: → installer\Output\PrintBridge-Setup.exe
```

Run `PrintBridge-Setup.exe` on each PC (it sets up the virtual printer).

## Verify end-to-end (two PCs)

1. On PC-A, open PrintBridge → **My Printers** → tick a printer (start with
   "Microsoft Print to PDF" to avoid wasting paper).
2. On PC-B → **Network Printers** → the shared printer appears within ~5s → select
   it → **Use selected printer**.
3. On PC-B, print anything to **"Shared Printer (PrintBridge)"**. Confirm it lands
   on PC-A (a PDF for Print-to-PDF, or paper for a real printer). PC-B's log shows
   "Printed".

See the plan's Phase 6 for the full test matrix (Win 7/8/10/11, PIN mismatch,
offline server, offline printer).
