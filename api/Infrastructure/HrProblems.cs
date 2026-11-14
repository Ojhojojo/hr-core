using Microsoft.AspNetCore.Http.HttpResults;

namespace HrCore.Infrastructure;

/// <summary>
/// Builds hr-core's error responses as RFC 9457 problem details (application/problem+json).
/// The <c>error</c> extension repeats <c>detail</c> because older clients still read the old <c>error</c> field.
/// </summary>
public static class HrProblems
{
    /// <summary>A problem response with the given status and user-readable detail. Extra fields keep their names.</summary>
    public static ProblemHttpResult Problem(int status, string? detail, IDictionary<string, object?>? extensions = null)
    {
        var all = new Dictionary<string, object?> { ["error"] = detail };
        if (extensions is not null)
        {
            foreach (var (key, value) in extensions)
            {
                all[key] = value;
            }
        }

        return TypedResults.Problem(
            detail: detail,
            statusCode: status,
            title: TitleFor(status),
            extensions: all);
    }

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "Bad request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status422UnprocessableEntity => "Unprocessable",
        _ => "Request failed",
    };
}
