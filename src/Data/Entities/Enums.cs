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

public enum VehicleStatus
{
    Active,
    Inactive,
    Maintenance,
}

// How a vehicle is charged (FR-INV-03): per trip, or per kilolitre of water.
public enum BillingBasis
{
    Trip,
    Volume,
}

public enum AccountType { Asset, Liability, Equity, Income, Expense }

public enum VoucherType { Sales, Receipt, Payment, Journal, Reversal }

public enum VoucherStatus { Posted, Cancelled }

// Mode of a receipt or payment (FR-TXN-03).
public enum PaymentMode { Cash, Bank, Upi }

public enum PartyType { Supplier, Customer }

public enum BusinessDayStatus { Open, Closed }

// Invoice payment mode (FR-INV-05).
public enum InvoicePaymentMode { Cash, Credit }

public enum InvoiceStatus { Posted, PartPaid, Paid, Cancelled }

public enum RateSource { Master, Overridden }
