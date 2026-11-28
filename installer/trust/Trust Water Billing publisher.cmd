@echo off
rem Run ONCE on the shop PC, before WaterBillingSetup.exe: right-click this file > Run as administrator.
rem It tells Windows that programs signed by "Shree Karunambika Traders" (WaterBilling-Publisher.cer,
rem kept next to this file) come from a known publisher. Afterwards the setup shows
rem "Verified publisher: Shree Karunambika Traders" instead of "Unknown publisher".
rem The certificate can only sign programs; it cannot be used for websites or anything else.
setlocal
cd /d "%~dp0"
net session >nul 2>&1 || (echo Please right-click this file and choose "Run as administrator". & pause & exit /b 1)
if not exist "WaterBilling-Publisher.cer" (echo WaterBilling-Publisher.cer is missing. Keep it in the same folder as this file. & pause & exit /b 1)
certutil -addstore -f Root "WaterBilling-Publisher.cer" >nul || goto failed
certutil -addstore -f TrustedPublisher "WaterBilling-Publisher.cer" >nul || goto failed
echo Done. Windows now trusts the Water Billing publisher. You can run WaterBillingSetup.exe.
pause
exit /b 0
:failed
echo Windows could not add the certificate. Make sure you used "Run as administrator".
pause
exit /b 1
