#!/usr/bin/env bash
# Signs one Windows .exe in place with the publisher's code-signing certificate (Authenticode, SHA-256).
# Used by build-installer.sh and by the NSIS script (installer, uninstaller). Skips quietly when no
# certificate is configured, so unsigned test builds still work.
#   SIGN_PFX           path to the .pfx certificate file (keep it OUT of the repository)
#   SIGN_PFX_PASSWORD  its password
#   SIGN_TIMESTAMP_URL optional, e.g. http://timestamp.digicert.com (keeps the signature valid after the certificate expires)
set -euo pipefail
file="$1"
if [ -z "${SIGN_PFX:-}" ]; then echo "sign.sh: no SIGN_PFX set, $file left unsigned"; exit 0; fi
ts=()
if [ -n "${SIGN_TIMESTAMP_URL:-}" ]; then ts=(-ts "$SIGN_TIMESTAMP_URL"); fi
osslsigncode sign -pkcs12 "$SIGN_PFX" -pass "$SIGN_PFX_PASSWORD" -h sha256 \
  -n "Water Billing" -i "https://github.com/PradhanaService/billing-system" "${ts[@]}" \
  -in "$file" -out "$file.signed"
mv -f "$file.signed" "$file"
echo "sign.sh: signed $file"
