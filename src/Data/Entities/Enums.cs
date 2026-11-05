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

public enum UserRole
{
    Admin,
    DispatchOperator,
    BillingClerk,
    Accountant,
}

// Groups for the activity log, so the Admin can filter it.
public enum ActivityCategory
{
    Auth,
    Master,
    Billing,
    Finance,
    Day,
    Admin,
}
