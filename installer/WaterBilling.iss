; Inno Setup script for the Water Supply & Fleet Billing System.
; Build with installer\build-installer.ps1 (it publishes the app first).
;
; What it does:
;  - copies the app to C:\Program Files\Water Billing
;  - creates C:\ProgramData\WaterBilling for the database and backups, writable by the service
;  - registers and starts the "WaterBilling" Windows service (starts with the PC, local service account)
;  - adds Start menu and desktop shortcuts that open http://localhost:5080
;  - on uninstall: stops and removes the service, but KEEPS the data folder

#define AppName "Water Billing"
#define AppVersion "1.0.0"
#define Publisher "Shree Karunambika Traders"
#define ServiceName "WaterBilling"
#define AppUrl "http://localhost:5080"

[Setup]
AppId={{6BE520C6-EBE1-40EA-94D0-AE087DB919A0}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=output
OutputBaseFilename=WaterBillingSetup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayName={#AppName}
WizardStyle=modern

[Files]
Source: "output\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
; Data folder outside Program Files. Kept on uninstall.
Name: "{commonappdata}\WaterBilling"; Flags: uninsneveruninstall
Name: "{commonappdata}\WaterBilling\Backups"; Flags: uninsneveruninstall

[INI]
; Desktop/Start menu shortcut is an internet shortcut to the local app.
Filename: "{app}\Open Water Billing.url"; Section: "InternetShortcut"; Key: "URL"; String: "{#AppUrl}"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Open Water Billing.url"
Name: "{group}\Backups folder"; Filename: "{commonappdata}\WaterBilling\Backups"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Open Water Billing.url"

[Run]
; Only the service account may write the data folder (plus Administrators and SYSTEM).
Filename: "{sys}\icacls.exe"; Parameters: """{commonappdata}\WaterBilling"" /inheritance:r /grant:r ""*S-1-5-18:(OI)(CI)F"" ""*S-1-5-32-544:(OI)(CI)F"" ""*S-1-5-19:(OI)(CI)M"""; Flags: runhidden waituntilterminated; StatusMsg: "Securing data folder..."
; Register the service (LocalService account), start it automatically, restart it if it stops.
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""\""{app}\WaterBilling.exe\"""" start= auto obj= ""NT AUTHORITY\LocalService"" DisplayName= ""{#AppName}"""; Flags: runhidden waituntilterminated; StatusMsg: "Registering service..."
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Water Supply and Fleet Billing System for {#Publisher}"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "failure {#ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/30000"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden waituntilterminated; StatusMsg: "Starting service..."
Filename: "{app}\Open Water Billing.url"; Description: "Open Water Billing now"; Flags: postinstall shellexec skipifsilent nowait

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteService"

[Code]
// On upgrade: stop the running service before files are replaced, then remove the old registration.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(3000);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
