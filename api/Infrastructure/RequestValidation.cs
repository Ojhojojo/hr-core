using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace HrCore.Infrastructure;

public static class RequestValidation
{
    public static bool TryValidate(object model, [NotNullWhen(false)] out Dictionary<string, string[]>? errors)
    {
        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(model, context, results, validateAllProperties: true))
        {
            errors = null;
            return true;
        }

        errors = results
            .SelectMany(result =>
            {
                var names = result.MemberNames.Any() ? result.MemberNames : [""];
                return names.Select(name => (name, message: result.ErrorMessage ?? "Invalid"));
            })
            .GroupBy(x => string.IsNullOrEmpty(x.name) ? "request" : x.name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.message).ToArray(), StringComparer.OrdinalIgnoreCase);
        return false;
    }
}
