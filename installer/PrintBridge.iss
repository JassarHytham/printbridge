; PrintBridge installer (Inno Setup 6).
; Build with:  iscc installer\PrintBridge.iss
; Expects the app built (Release) to:  src\PrintBridge.App\bin\Release\net48\
; and Ghostscript present under third_party\ghostscript\ (see SOURCE.txt).
; No custom DLLs, no signing, no test-signing mode required.

#define AppName "PrintBridge"
#define AppVersion "1.0.0"
#define Publisher "PrintBridge"
#define AppExe "PrintBridge.App.exe"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={autopf}\PrintBridge
DefaultGroupName=PrintBridge
DisableProgramGroupPage=yes
OutputBaseFilename=PrintBridge-Setup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
; ARM64 Windows 11 runs x64 user-mode code via the Prism emulation layer.
; Ghostscript x64 works fine there. Leave ArchitecturesInstallIn64BitMode
; unset so the installer runs on both x64 and ARM64.
; .NET Framework 4.8 ships with Windows 10/11. For Win7/8, users install it first.

[Files]
; App + dependencies (Newtonsoft.Json.dll, PrintBridge.*.dll, etc.)
Source: "..\src\PrintBridge.App\bin\Release\net48\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
; Ghostscript x64 (user-mode; works on x64 and ARM64 via emulation).
Source: "..\third_party\ghostscript\*"; DestDir: "{app}\ghostscript"; Flags: recursesubdirs ignoreversion
; Installer scripts (no cert needed — Standard TCP/IP port requires no signing).
Source: "install-virtual-printer.ps1";   DestDir: "{app}\installer"; Flags: ignoreversion
Source: "uninstall-virtual-printer.ps1"; DestDir: "{app}\installer"; Flags: ignoreversion

[Icons]
Name: "{group}\PrintBridge"; Filename: "{app}\{#AppExe}"
Name: "{userstartup}\PrintBridge"; Filename: "{app}\{#AppExe}"; Tasks: startup

[Tasks]
Name: "startup"; Description: "Start PrintBridge automatically when I sign in"; GroupDescription: "Startup"

[Run]
; Create the virtual printer (Standard TCP/IP RAW port — no reboot needed).
Filename: "powershell.exe"; \
  Parameters: "-ExecutionPolicy Bypass -File ""{app}\installer\install-virtual-printer.ps1"" -InstallRoot ""{app}"""; \
  StatusMsg: "Setting up the shared virtual printer..."; \
  Flags: runhidden waituntilterminated
; Launch the app after install.
Filename: "{app}\{#AppExe}"; Description: "Launch PrintBridge"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "powershell.exe"; \
  Parameters: "-ExecutionPolicy Bypass -File ""{app}\installer\uninstall-virtual-printer.ps1"""; \
  Flags: runhidden waituntilterminated; RunOnceId: "RemoveVirtualPrinter"
