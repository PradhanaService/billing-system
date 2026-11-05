namespace WaterBilling.Services.Ledger;

// A normal business refusal raised deep inside a save (closed day, unbalanced voucher).
// Services catch it and return Result.Fail(message) to the screen; the transaction rolls back.
public sealed class BusinessRuleException(string message) : Exception(message);
