# Install guide (shop PC)

## Build the installer (developer, on any Windows PC)
1. Install the .NET 8 SDK and Inno Setup 6.
2. From the repository folder run:
   `powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1`
   It runs all tests first and stops if any fail.
3. The result is `installer\output\WaterBillingSetup.exe` (one file, about 45 MB).

## Install on the shop PC
1. Turn on **BitLocker** for the C: drive (Settings → Privacy & security → Device encryption). This
   encrypts the database at rest (NFR-SEC-01).
2. Run `WaterBillingSetup.exe` as an administrator. It needs no internet and no .NET installation.
3. Open **Water Billing** from the desktop. It opens `http://localhost:5080` in the browser.
4. Sign in as `admin` / `Admin@123` and choose a new password straight away.
5. In **Ledger Master → Starting numbers**, set the agreed first invoice number before the first invoice.
6. Create the staff users in **Users & Roles**.

## Where things are
| What | Where |
|---|---|
| Program | `C:\Program Files\Water Billing` |
| Database | `C:\ProgramData\WaterBilling\waterbilling.db` |
| Backups (one per day automatically, plus "Backup now") | `C:\ProgramData\WaterBilling\Backups` |
| Service | Windows Services → **Water Billing** (starts with the PC, restarts if it stops) |
| Logs | Windows Event Viewer → Applications |

## Security notes
- The app only listens on `localhost`: other computers on the network cannot reach it.
- Traffic never leaves the PC, so plain HTTP on localhost is used; no certificate to manage.
- Only the service account, Administrators and SYSTEM can open the data folder.
- Copy the Backups folder to a USB drive or another computer every week.

## Update to a new version
Run the new `WaterBillingSetup.exe`. It stops the service, replaces the program, and starts it again.
The database is upgraded automatically on start. Data and backups are kept. Take a "Backup now" first.

## Uninstall
Control Panel → Programs → Water Billing → Uninstall. The data folder is **kept**; delete
`C:\ProgramData\WaterBilling` by hand only if the data is no longer needed.
