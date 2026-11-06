namespace WaterBilling.Services.Security;

// Password rules used everywhere a password is set: at least 8 characters, at least one letter
// and one number, and not the same as the user name.

public static class PasswordRules
{
    public const int MinLength = 8;

    // Returns an error message, or null when the password is acceptable.
    public static string? Check(string password, string userName)
    {
        if (password.Length < MinLength) return $"Password must be at least {MinLength} characters.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "Password must contain at least one letter and one number.";
        if (string.Equals(password, userName, StringComparison.OrdinalIgnoreCase))
            return "Password must not be the same as the user name.";
        return null;
    }
}
