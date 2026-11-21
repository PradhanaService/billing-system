# Code signing (why Windows warns, and how the setup is made trusted)

Windows warns about any setup file it cannot trace to a known publisher:
- **"Unknown publisher"** in the administrator prompt when the file has no digital signature.
- **"Windows protected your PC"** (SmartScreen) when a *downloaded* file has no reputation yet.

The build signs every program file (`WaterBillingSetup.exe`, the uninstaller, `WaterBilling.exe`,
`WaterBillingApp.exe`) with the publisher's certificate. Script: `installer/sign.sh`, called by
`installer/build-installer.sh` when `SIGN_PFX` and `SIGN_PFX_PASSWORD` are set.

## Two kinds of certificate

| | Self-signed (used now) | Bought certificate (for the final handover) |
|---|---|---|
| Cost | Free | About ₹6,000 to ₹30,000 a year (or Microsoft Trusted Signing, about USD 10 a month) |
| Who trusts it | Only PCs where `Trust Water Billing publisher.cmd` was run once | Every Windows PC, nothing to install |
| Admin prompt | "Verified publisher: Shree Karunambika Traders" after the trust step | Same, on every PC |
| SmartScreen on a downloaded file | Can still appear: click More info → Run anyway, or copy the setup by USB / unblock it | Disappears once the certificate has reputation (at once for Trusted Signing / EV) |
| Needs | Nothing | Business documents (GST/registration) for the issuer's identity check |

## Installing with the self-signed certificate (shop PC, once)
1. Keep these three files in one folder: `WaterBillingSetup.exe`, `WaterBilling-Publisher.cer`,
   `Trust Water Billing publisher.cmd`.
2. Right-click `Trust Water Billing publisher.cmd` → **Run as administrator**. It adds the publisher
   certificate to Windows (Trusted Root and Trusted Publishers). The certificate can only sign programs.
3. If the setup was downloaded from the internet: right-click `WaterBillingSetup.exe` → Properties →
   tick **Unblock** → OK. (A file copied from a USB drive does not need this.)
4. Double-click `WaterBillingSetup.exe`. The prompt shows **Verified publisher: Shree Karunambika Traders**.

## Keeping the certificate
- The private key file (`.pfx`) and its password must **never** be put in the repository or sent by chat.
  Keep them on the developer's PC and a backup USB drive.
- Updates must be signed with the **same** certificate, or the shop PC sees "Unknown publisher" again.
- Signatures made without a timestamp stay valid until the certificate expires (the self-signed one: 5 years).
  When building with internet access, set `SIGN_TIMESTAMP_URL=http://timestamp.digicert.com` so signatures
  stay valid after that.

## Switching to a bought certificate
Buy an OV code-signing certificate (or set up Microsoft Trusted Signing) in the business name, export it as
`.pfx`, set `SIGN_PFX` / `SIGN_PFX_PASSWORD` / `SIGN_TIMESTAMP_URL`, and run `installer/build-installer.sh`.
Nothing else changes, and the trust step above is no longer needed.
