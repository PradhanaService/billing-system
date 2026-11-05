namespace WaterBilling.Services.Common;

// What a service returns when an action can fail for a normal business reason
// (duplicate name, missing field). The screen shows Error to the user.
// Programming mistakes and missing permissions throw exceptions instead.
public record Result(bool Ok, string? Error)
{
    public static Result Success() => new(true, null);
    public static Result Fail(string error) => new(false, error);
}

public record Result<T>(bool Ok, T? Value, string? Error)
{
    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Fail(string error) => new(false, default, error);
}
