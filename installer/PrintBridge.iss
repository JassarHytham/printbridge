; PrintBridge installer (Inno Setup 6).
; Build with:  iscc installer\PrintBridge.iss
; Expects the app to be published first (Release build) to:  src\PrintBridge.App\bin\Release\net48\
; and Ghostscript + mfilemon present under third_party\.

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
ArchitecturesInstallIn64BitMode=x64
; .NET Framework 4.8 is required (ships in Win10+; Win7/8 users may need it).
; See README for the offline installer link.

[Files]
; The app + its dependencies (Newtonsoft.Json.dll, PrintBridge.*.dll).
Source: "..\src\PrintBridge.App\bin\Release\net48\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
; Bundled Ghostscript (resolved by the app at {app}\ghostscript\bin\gswin64c.exe).
Source: "..\third_party\ghostscript\*"; DestDir: "{app}\ghostscript"; Flags: recursesubdirs ignoreversion
; Bundled mfilemon DLLs (x64 + x86).
Source: "..\third_party\mfilemon\*"; DestDir: "{app}\third_party\mfilemon"; Flags: recursesubdirs ignoreversion
; Installer scripts + public test cert (the app calls install-virtual-printer.ps1).
Source: "install-virtual-printer.ps1";   DestDir: "{app}\installer"; Flags: ignoreversion
Source: "uninstall-virtual-printer.ps1"; DestDir: "{app}\installer"; Flags: ignoreversion
Source: "certs\PrintBridge.cer";         DestDir: "{app}\installer\certs"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\PrintBridge"; Filename: "{app}\{#AppExe}"
Name: "{userstartup}\PrintBridge"; Filename: "{app}\{#AppExe}"; Tasks: startup

[Tasks]
Name: "startup"; Description: "Start PrintBridge automatically when I sign in"; GroupDescription: "Startup"

[Run]
; Set up the virtual printer + port monitor at the end of install (elevated).
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
