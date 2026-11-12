; Inno Setup script for the Water Supply & Fleet Billing System.
; Build with installer\build-installer.ps1 (it tests and publishes the app first).
;
; What it does (the user only double-clicks WaterBillingSetup.exe and clicks Next / Install / Finish):
;  - copies the app to C:\Program Files\Water Billing
;  - creates C:\ProgramData\WaterBilling for the database and backups, writable by the service
;  - registers and starts the "WaterBilling" Windows service (starts with the PC, local service account);
;    the service holds all screens, rules and data and is only reachable from this computer
;  - installs the Microsoft Edge WebView2 Runtime if the PC does not have it yet (Windows 11 has it)
;  - adds ONE "Water Billing" icon (desktop and Start menu) that opens the Water Billing window
;    (WaterBillingApp.exe): no browser, no address bar
;  - on uninstall: stops and removes the service, but KEEPS the data folder

#define AppName "Water Billing"
#define AppVersion "1.0.0"
#define Publisher "Shree Karunambika Traders"
#define ServiceName "WaterBilling"
#define WindowExe "WaterBillingApp.exe"
; Microsoft's fixed id for the WebView2 Runtime in the registry.
#define WebView2Id "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"

[Setup]
AppId={{6BE520C6-EBE1-40EA-94D0-AE087DB919A0}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=WaterBillingSetup
SetupIconFile=..\desktop\WaterBilling.Desktop\app.ico
UninstallDisplayIcon={app}\{#WindowExe}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayName={#AppName}
WizardStyle=modern
CloseApplications=yes

[Files]
; The service (screens, rules, database access).
Source: "output\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; The app window the user opens.
Source: "output\desktop\{#WindowExe}"; DestDir: "{app}"; Flags: ignoreversion
; WebView2 Runtime installer, used only when the PC does not have the runtime (downloaded by the build script).
Source: "output\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist; Check: NeedsWebView2

[InstallDelete]
; Older versions opened the browser through this shortcut file.
Type: files; Name: "{app}\Open Water Billing.url"

[Dirs]
; Data folder outside Program Files. Kept on uninstall.
Name: "{commonappdata}\WaterBilling"; Flags: uninsneveruninstall
Name: "{commonappdata}\WaterBilling\Backups"; Flags: uninsneveruninstall

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#WindowExe}"
Name: "{group}\Backups folder"; Filename: "{commonappdata}\WaterBilling\Backups"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#WindowExe}"

[Run]
; WebView2 Runtime first, only if missing (shows nothing; needs internet on the shop PC that once).
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; Flags: waituntilterminated skipifdoesntexist; Check: NeedsWebView2; StatusMsg: "Installing the Microsoft Edge WebView2 Runtime..."
; Only the service account may write the data folder (plus Administrators and SYSTEM).
Filename: "{sys}\icacls.exe"; Parameters: """{commonappdata}\WaterBilling"" /inheritance:r /grant:r ""*S-1-5-18:(OI)(CI)F"" ""*S-1-5-32-544:(OI)(CI)F"" ""*S-1-5-19:(OI)(CI)M"""; Flags: runhidden waituntilterminated; StatusMsg: "Securing data folder..."
; Register the service (LocalService account), start it automatically, restart it if it stops.
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""\""{app}\WaterBilling.exe\"""" start= auto obj= ""NT AUTHORITY\LocalService"" DisplayName= ""{#AppName}"""; Flags: runhidden waituntilterminated; StatusMsg: "Registering service..."
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Water Supply and Fleet Billing System for {#Publisher}"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "failure {#ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/30000"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden waituntilterminated; StatusMsg: "Starting Water Billing..."
; "Open Water Billing now" on the last page. Runs as the signed-in user, not as administrator.
Filename: "{app}\{#WindowExe}"; Description: "Open Water Billing now"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#WindowExe} /F"; Flags: runhidden waituntilterminated; RunOnceId: "CloseWindow"
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteService"

[Code]
// True when the WebView2 Runtime is not installed (machine-wide or for this user).
function WebView2Version(RootKey: Integer; Path: String): String;
begin
  if not RegQueryStringValue(RootKey, Path, 'pv', Result) then Result := '';
end;

function NeedsWebView2: Boolean;
var
  V: String;
begin
  V := WebView2Version(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{#WebView2Id}');
  if (V = '') or (V = '0.0.0.0') then
    V := WebView2Version(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{#WebView2Id}');
  Result := (V = '') or (V = '0.0.0.0');
end;

// On upgrade: close the app window, stop the running service before files are replaced,
// then remove the old registration (it is created again in [Run]).
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#WindowExe} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(3000);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
