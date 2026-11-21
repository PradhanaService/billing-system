# Install guide (shop PC)

## Build the installer (developer)
The result is always `installer/output/WaterBillingSetup.exe`: the ONE file to give to the client.
Every way runs all tests first and stops if any fail.

- **On Linux, macOS or WSL (NSIS):** install the .NET 8 SDK and NSIS 3 (`sudo apt install nsis`), then run
  `bash installer/build-installer.sh`. Script: `installer/WaterBilling.nsi`.
- **On Windows (Inno Setup):** install the .NET 8 SDK and Inno Setup 6, then run
  `powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1`. Script: `installer/WaterBilling.iss`.
- **On GitHub:** every push runs `.github/workflows/windows-installer.yml` on a Windows PC. It builds the setup,
  installs it silently, checks the service answers and the app window opens, uninstalls it, and keeps
  `WaterBillingSetup.exe` under Actions → the run → Artifacts.

Both scripts install the same way (folders, service, icons, uninstall). Keep them in step when one changes.

## Install on the shop PC
1. Turn on **BitLocker** for the C: drive (Settings → Privacy & security → Device encryption). This
   encrypts the database at rest (NFR-SEC-01).
2. Double-click `WaterBillingSetup.exe` and click **Next**, **Install**, **Finish** (Windows asks for
   administrator permission once). No .NET installation is needed. Internet is needed only if the PC
   lacks the Microsoft Edge WebView2 Runtime (Windows 11 and up-to-date Windows 10 already have it).
3. Leave **Open Water Billing now** ticked on the last page, or later double-click the **Water Billing**
   icon on the desktop. It opens the Water Billing window: no browser, no address bar. Clicking the icon
   again brings the open window to the front instead of opening a second one.
4. Sign in as `admin` / `Admin@123` and choose a new password straight away.
5. In **Ledger Master → Starting numbers**, set the agreed first invoice number before the first invoice.
6. In **Password Management**, set the Sales login's password. In **Sales Persons**, add the staff names.

## Where things are
| What | Where |
|---|---|
| Program | `C:\Program Files\Water Billing` (`WaterBillingApp.exe` is the window, `WaterBilling.exe` the service) |
| Database | `C:\ProgramData\WaterBilling\waterbilling.db` |
| Backups (one per day automatically, plus "Backup now") | `C:\ProgramData\WaterBilling\Backups` |
| Service | Windows Services → **Water Billing** (starts with the PC, restarts if it stops) |
| Logs | Windows Event Viewer → Applications |

## Security notes
- The window shows the Water Billing service running on the same PC. The service only listens on this
  computer: other computers on the network cannot reach it. The window opens nothing but Water Billing
  screens; PDFs open in their own Water Billing window.
- Traffic never leaves the PC, so plain HTTP on localhost is used; no certificate to manage.
- Only the service account, Administrators and SYSTEM can open the data folder.
- Copy the Backups folder to a USB drive or another computer every week.

## Update to a new version
Run the new `WaterBillingSetup.exe`. It closes the window, stops the service, replaces the program, and starts it again.
The database is upgraded automatically on start. Data and backups are kept. Take a "Backup now" first.

## Uninstall
Control Panel → Programs → Water Billing → Uninstall. The data folder is **kept**; delete
`C:\ProgramData\WaterBilling` by hand only if the data is no longer needed.
