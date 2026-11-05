using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace WaterBilling.Services.Masters;

// Filter used by every master list: Active, Inactive or All (FR-VM-10).
public enum StatusFilter { Active, Inactive, All }

// Small rules shared by suppliers and customers.
public static partial class PartyRules
{
    // "  Sangeetha   Water " -> "Sangeetha Water"
    public static string Tidy(string? text) => MultiSpace().Replace((text ?? "").Trim(), " ");

    public static string? TidyOrNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : Tidy(text);

    // Phone: 10 to 15 digits, optional + in front. Spaces and dashes typed by the user are removed.
    public const string PhonePattern = @"^\+?[0-9]{10,15}$";
    public static string CleanPhone(string? phone) => SpacesAndDashes().Replace(phone ?? "", "");

    [GeneratedRegex(@"\s+")] private static partial Regex MultiSpace();
    [GeneratedRegex(@"[\s-]")] private static partial Regex SpacesAndDashes();
}

// Phone check used on forms. Accepts "98765 43210" by cleaning it first.
public sealed class PhoneAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s)) return ValidationResult.Success;   // [Required] handles empty
        return Regex.IsMatch(PartyRules.CleanPhone(s), PartyRules.PhonePattern)
            ? ValidationResult.Success
            : new ValidationResult(ErrorMessage ?? "Phone must be 10 to 15 digits.", [context.MemberName!]);
    }
}

// Money typed on a form: 0 or more, at most 2 decimals.
public sealed class RupeesAttribute : ValidationAttribute
{
    public bool AllowZero { get; set; } = true;

    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        if (value is not decimal d) return ValidationResult.Success;
        if (d < 0 || (!AllowZero && d == 0))
            return new ValidationResult(AllowZero ? "Amount cannot be negative." : "Amount must be greater than zero.", [context.MemberName!]);
        if (d * 100 != decimal.Truncate(d * 100))
            return new ValidationResult("Amount can have at most 2 decimal places.", [context.MemberName!]);
        if (d > 99_99_99_999.99m)
            return new ValidationResult("Amount is too large.", [context.MemberName!]);
        return ValidationResult.Success;
    }
}
