using System.ComponentModel.DataAnnotations;

namespace WaterBilling.Services.Common;

// Runs the [Required], [StringLength] ... attributes of a form on the server.
// The screen runs the same attributes for instant feedback, but the service never trusts the screen.
public static class FormValidator
{
    // Returns the first error message, or null when the form is valid.
    public static string? FirstError(object form)
    {
        var results = new List<ValidationResult>();
        return Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true)
            ? null
            : results[0].ErrorMessage;
    }
}
