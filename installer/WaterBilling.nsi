; NSIS script for the Water Supply & Fleet Billing System: builds WaterBillingSetup.exe.
; It does the same as WaterBilling.iss (Inno Setup), but NSIS also runs on Linux, so the setup file
; can be built on any computer. Build with installer/build-installer.sh (it tests and publishes first).
;
; What the setup does (the user double-clicks WaterBillingSetup.exe and clicks Next / Install / Finish):
;  - copies the app to C:\Program Files\Water Billing
;  - creates C:\ProgramData\WaterBilling for the database and backups, writable by the service only
;  - registers and starts the "WaterBilling" Windows service (starts with the PC, Local Service account);
;    the service holds all screens, rules and data and is reachable only from this computer
;  - installs the Microsoft Edge WebView2 Runtime if the PC does not have it (Windows 11 has it)
;  - adds ONE "Water Billing" icon (desktop and Start menu) that opens the Water Billing window
;    (WaterBillingApp.exe): an app window, no browser, no address bar
;  - adds Water Billing to Settings > Apps, with an uninstaller
;  - on uninstall: stops and removes the service and the program, but KEEPS the data folder

Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

!define AppName      "Water Billing"
!define AppVersion   "1.0.0"
!define Publisher    "Shree Karunambika Traders"
!define ServiceName  "WaterBilling"
!define WindowExe    "WaterBillingApp.exe"
!define UninstKey    "Software\Microsoft\Windows\CurrentVersion\Uninstall\WaterBilling"
; Microsoft's fixed id for the WebView2 Runtime in the registry.
!define WebView2Id   "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"

; Code signing (build-installer.sh passes -DSIGN when a certificate is configured): the uninstaller
; and the setup file itself get the publisher's digital signature, so Windows shows
; "Verified publisher: Shree Karunambika Traders" instead of "Unknown publisher".
!ifdef SIGN
  !uninstfinalize 'bash sign.sh "%1"' = 0
  !finalize 'bash sign.sh "%1"' = 0
!endif

Name "${AppName}"
OutFile "output\WaterBillingSetup.exe"
InstallDir "$PROGRAMFILES64\${AppName}"
InstallDirRegKey HKLM "${UninstKey}" "InstallLocation"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
SetCompressorDictSize 64
BrandingText "${Publisher}"

VIProductVersion "${AppVersion}.0"
VIAddVersionKey "ProductName" "${AppName}"
VIAddVersionKey "CompanyName" "${Publisher}"
VIAddVersionKey "FileDescription" "${AppName} Setup"
VIAddVersionKey "FileVersion" "${AppVersion}"
VIAddVersionKey "ProductVersion" "${AppVersion}"
VIAddVersionKey "LegalCopyright" "${Publisher}"

!define MUI_ICON   "..\desktop\WaterBilling.Desktop\app.ico"
!define MUI_UNICON "..\desktop\WaterBilling.Desktop\app.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "Install ${AppName}"
!define MUI_WELCOMEPAGE_TEXT "This installs the Water Supply & Fleet Billing System for ${Publisher} on this computer.$\r$\n$\r$\nIt runs as an app on this computer only: no browser and no internet are needed to use it.$\r$\n$\r$\nClick Next to continue."
!define MUI_FINISHPAGE_TITLE "${AppName} is installed"
!define MUI_FINISHPAGE_TEXT "Open it any time from the Water Billing icon on the desktop or in the Start menu.$\r$\n$\r$\nFirst sign-in: user name admin, password Admin@123. You will be asked to choose a new password."
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Open Water Billing now"
!define MUI_FINISHPAGE_RUN_FUNCTION OpenAppAsUser

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

; ---------------------------------------------------------------- checks before installing
Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "${AppName} needs 64-bit Windows 10 or Windows 11."
    Abort
  ${EndIf}
  SetRegView 64
FunctionEnd

; Close the app window and stop the old service, so an upgrade can replace the files.
!macro StopApp
  nsExec::Exec '"$SYSDIR\taskkill.exe" /IM ${WindowExe} /F'
  Pop $0
  nsExec::Exec '"$SYSDIR\sc.exe" stop ${ServiceName}'
  Pop $0
  Sleep 3000
  nsExec::Exec '"$SYSDIR\sc.exe" delete ${ServiceName}'
  Pop $0
  Sleep 1000
!macroend

; ---------------------------------------------------------------- install
Section "Install"
  SetShellVarContext all                 ; shortcuts for every Windows user, $APPDATA = C:\ProgramData
  DetailPrint "Closing any running copy of ${AppName}..."
  !insertmacro StopApp

  ; Program files: the service (screens, rules, database access) and the app window.
  SetOutPath "$INSTDIR"
  File /r "output\app\*.*"
  File "output\desktop\${WindowExe}"
  Delete "$INSTDIR\Open Water Billing.url"     ; older versions opened the browser through this file

  ; Data folder outside Program Files. Never removed by the uninstaller.
  CreateDirectory "$APPDATA\WaterBilling"
  CreateDirectory "$APPDATA\WaterBilling\Backups"
  DetailPrint "Securing the data folder..."
  ; Only SYSTEM, Administrators (full) and the service's Local Service account (modify) may use it.
  nsExec::ExecToLog '"$SYSDIR\icacls.exe" "$APPDATA\WaterBilling" /inheritance:r /grant:r "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-19:(OI)(CI)M"'
  Pop $0

  ; WebView2 Runtime (shows the app's screens inside the window). Windows 11 already has it.
  Call InstallWebView2IfMissing

  ; The Windows service: starts with the PC, runs as Local Service, restarts itself if it stops.
  DetailPrint "Registering the ${AppName} service..."
  nsExec::ExecToLog '"$SYSDIR\sc.exe" create ${ServiceName} binPath= "\"$INSTDIR\WaterBilling.exe\"" start= auto obj= "NT AUTHORITY\LocalService" DisplayName= "${AppName}"'
  Pop $0
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "The ${AppName} service could not be registered (code $0). Run the setup again as administrator."
    Abort
  ${EndIf}
  nsExec::Exec '"$SYSDIR\sc.exe" description ${ServiceName} "Water Supply and Fleet Billing System for ${Publisher}"'
  Pop $0
  nsExec::Exec '"$SYSDIR\sc.exe" failure ${ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/30000'
  Pop $0
  DetailPrint "Starting ${AppName}..."
  nsExec::ExecToLog '"$SYSDIR\sc.exe" start ${ServiceName}'
  Pop $0

  ; One icon on the desktop and in the Start menu, plus a link to the backups folder.
  CreateDirectory "$SMPROGRAMS\${AppName}"
  CreateShortcut "$SMPROGRAMS\${AppName}\${AppName}.lnk" "$INSTDIR\${WindowExe}" "" "$INSTDIR\${WindowExe}" 0
  CreateShortcut "$SMPROGRAMS\${AppName}\Backups folder.lnk" "$APPDATA\WaterBilling\Backups"
  CreateShortcut "$DESKTOP\${AppName}.lnk" "$INSTDIR\${WindowExe}" "" "$INSTDIR\${WindowExe}" 0

  ; Settings > Apps entry and the uninstaller.
  WriteUninstaller "$INSTDIR\Uninstall Water Billing.exe"
  WriteRegStr HKLM "${UninstKey}" "DisplayName" "${AppName}"
  WriteRegStr HKLM "${UninstKey}" "DisplayVersion" "${AppVersion}"
  WriteRegStr HKLM "${UninstKey}" "Publisher" "${Publisher}"
  WriteRegStr HKLM "${UninstKey}" "DisplayIcon" "$INSTDIR\${WindowExe}"
  WriteRegStr HKLM "${UninstKey}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UninstKey}" "UninstallString" '"$INSTDIR\Uninstall Water Billing.exe"'
  WriteRegStr HKLM "${UninstKey}" "QuietUninstallString" '"$INSTDIR\Uninstall Water Billing.exe" /S'
  WriteRegDWORD HKLM "${UninstKey}" "NoModify" 1
  WriteRegDWORD HKLM "${UninstKey}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKLM "${UninstKey}" "EstimatedSize" $0
SectionEnd

; True when the WebView2 Runtime is missing (machine-wide or for this user).
Function InstallWebView2IfMissing
  ReadRegStr $0 HKLM "SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\${WebView2Id}" "pv"
  ${If} $0 == ""
  ${OrIf} $0 == "0.0.0.0"
    ReadRegStr $0 HKCU "Software\Microsoft\EdgeUpdate\Clients\${WebView2Id}" "pv"
  ${EndIf}
  ${If} $0 != ""
  ${AndIf} $0 != "0.0.0.0"
    Return
  ${EndIf}
  DetailPrint "Installing the Microsoft Edge WebView2 Runtime..."
  SetOutPath "$PLUGINSDIR"
  !if /FileExists "output\MicrosoftEdgeWebview2Setup.exe"
    File "output\MicrosoftEdgeWebview2Setup.exe"
  !else
    ; Not bundled: download Microsoft's small setup with Windows' own curl (needs internet this once).
    nsExec::ExecToLog '"$SYSDIR\curl.exe" -sSL -o "$PLUGINSDIR\MicrosoftEdgeWebview2Setup.exe" "https://go.microsoft.com/fwlink/p/?LinkId=2124703"'
    Pop $0
  !endif
  ${If} ${FileExists} "$PLUGINSDIR\MicrosoftEdgeWebview2Setup.exe"
    ExecWait '"$PLUGINSDIR\MicrosoftEdgeWebview2Setup.exe" /silent /install' $0
  ${Else}
    MessageBox MB_ICONEXCLAMATION "The Microsoft Edge WebView2 Runtime is missing and could not be downloaded.$\r$\nConnect to the internet and run this setup again, or install it from Microsoft's website."
  ${EndIf}
FunctionEnd

; "Open Water Billing now": opened through Explorer so it runs as the signed-in user, not as administrator.
Function OpenAppAsUser
  Exec '"$WINDIR\explorer.exe" "$INSTDIR\${WindowExe}"'
FunctionEnd

; ---------------------------------------------------------------- uninstall
Function un.onInit
  SetRegView 64
FunctionEnd

Section "Uninstall"
  SetShellVarContext all
  !insertmacro StopApp
  RMDir /r "$INSTDIR"
  Delete "$DESKTOP\${AppName}.lnk"
  RMDir /r "$SMPROGRAMS\${AppName}"
  DeleteRegKey HKLM "${UninstKey}"
  ; The data folder C:\ProgramData\WaterBilling (database and backups) is kept on purpose.
SectionEnd
