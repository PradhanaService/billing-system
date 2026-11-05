namespace WaterBilling.Data.Entities;

// Enums are stored in the database as text ("Active"), not numbers, so the data is readable.

public enum RecordStatus
{
    Active,
    Inactive,
}

// Which side an opening balance sits on. Dr = they owe us. Cr = we owe them.
public enum BalanceSide
{
    Dr,
    Cr,
}
