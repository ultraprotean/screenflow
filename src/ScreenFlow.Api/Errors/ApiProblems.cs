namespace ScreenFlow.Api.Errors;

/// <summary>
/// The error codes in CONTRACT.md §2. Every error response is RFC 9457 Problem Details with a stable
/// <c>code</c> extension, so clients branch on a string rather than parsing messages.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string NotFound = "NOT_FOUND";
    public const string InvalidTransition = "INVALID_TRANSITION";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string InternalError = "INTERNAL_ERROR";
}

public static class ApiProblems
{
    private const string TypeBase = "https://screenflow.dev/errors/";

    public static IResult NotFound(string detail) =>
        Problem(StatusCodes.Status404NotFound, "Not found", detail, ErrorCodes.NotFound, "not-found");

    public static IResult Validation(IDictionary<string, string[]> errors) =>
        Results.ValidationProblem(
            errors,
            title: "Validation failed",
            type: TypeBase + "validation-failed",
            extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.ValidationFailed });

    public static IResult Validation(string field, string message) =>
        Validation(new Dictionary<string, string[]> { [field] = [message] });

    public static IResult Problem(int status, string title, string detail, string code, string slug) =>
        Results.Problem(
            statusCode: status,
            title: title,
            detail: detail,
            type: TypeBase + slug,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
