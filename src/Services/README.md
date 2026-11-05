# Services layer

Business logic lives here: invoice, ledger posting, day close.
Pages call services; services use `/Data`. Keep all rules here so they can be tested.

`Common/` holds the conversion rules every module uses: money (paise), volume (tenths), IST time.
