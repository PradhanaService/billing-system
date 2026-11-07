namespace WaterBilling.Data.Entities;

// A ledger (Ledger Master): Cash in Hand, Water Sales, Fuel Expense ...
// System ledgers are created by the app and cannot be changed (FR-TXN-13).
public class Account : IAuditedEntity, ISoftDeletable
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public AccountType Type { get; set; }

    // Fixed code for ledgers the code relies on (see SystemAccounts). Null for user-made ledgers.
    public string? SystemCode { get; set; }
    public bool IsSystem { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;

    // Soft delete (see ISoftDeletable): set when deleted, cleared when restored.
    public DateTime? DeletedAtUtc { get; set; }
    public long? DeletedByUserId { get; set; }
    public long? DeletedBySalesPersonId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public int Version { get; set; }
}

// The ledgers the posting rules need, by fixed Id (seeded by the AddLedger migration).
public static class SystemAccounts
{
    public const long Cash = 1;
    public const long Bank = 2;
    public const long Upi = 3;
    public const long SupplierReceivables = 4;
    public const long CustomerReceivables = 5;
    public const long WaterSales = 6;
    public const long CashOverShort = 7;
    public const long OpeningBalanceEquity = 8;

    // Where money comes in or goes out for each mode.
    public static long ForMode(PaymentMode mode) => mode switch
    {
        PaymentMode.Cash => Cash,
        PaymentMode.Bank => Bank,
        _ => Upi,
    };

    // A party's balance lives in the control ledger for its type, tagged with the party.
    public static long ForParty(PartyType type) => type == PartyType.Supplier ? SupplierReceivables : CustomerReceivables;
}
