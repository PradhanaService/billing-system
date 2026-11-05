using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Ledger;

// P10 / FR-VM-12: a party saved with an opening balance posts one opening entry, once
// (called only from party creation).
//   Dr balance: Dr party, Cr Opening Balance Equity.  Cr balance: the reverse.
public static class OpeningBalances
{
    public static async Task PostAsync(AppDbContext db, LedgerPoster poster, PartyType type, long partyId,
        string partyName, long amountPaise, BalanceSide side, UserInfo user)
    {
        if (amountPaise <= 0) return;

        long? supplier = type == PartyType.Supplier ? partyId : null;
        long? customer = type == PartyType.Customer ? partyId : null;
        var party = SystemAccounts.ForParty(type);
        var text = $"Opening balance b/f - {partyName}";
        LineDraft[] lines = side == BalanceSide.Dr
            ? [LineDraft.Dr(party, amountPaise, text, supplier, customer), LineDraft.Cr(SystemAccounts.OpeningBalanceEquity, amountPaise, text)]
            : [LineDraft.Dr(SystemAccounts.OpeningBalanceEquity, amountPaise, text), LineDraft.Cr(party, amountPaise, text, supplier, customer)];

        var voucher = new Voucher
        {
            Type = VoucherType.Journal,
            PartyType = type,
            SupplierId = supplier,
            CustomerId = customer,
            AmountPaise = amountPaise,
            Narration = text,
        };
        await poster.PostAsync(db, voucher, lines, user);
    }
}
