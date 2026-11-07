namespace WaterBilling.Services.Security;

// Password rules used everywhere a password is set (new login, Admin reset, own change):
// at least 4 characters of any kind (letters, numbers, symbols, in any mix), and not the
// same as the user name. Client decision (Nov 2026): no "must contain a letter/number/symbol" rules.

public static class PasswordRules
{
    public const int MinLength = 4;

    // Returns an error message, or null when the password is acceptable.
    public static string? Check(string password, string userName)
    {
        if (password.Length < MinLength) return $"Password must be at least {MinLength} characters.";
        if (string.Equals(password, userName, StringComparison.OrdinalIgnoreCase))
            return "Password must not be the same as the user name.";
        return null;
    }
}
