using System.Text.RegularExpressions;

namespace ScreenFlow.Api.Contracts;

/// <summary>
/// Hand-written validation for CONTRACT.md §6. Explicit and easy to test; for a larger API, .NET 10's
/// built-in minimal API validation (AddValidation + data annotations) or FluentValidation would scale
/// better. Messages never echo the submitted value, because the value might be an SSN.
/// </summary>
public static partial class RequestValidator
{
    [GeneratedRegex(@"^(\d{3}-\d{2}-\d{4}|\d{9})$")]
    private static partial Regex SsnPattern();

    [GeneratedRegex(@"^[A-Za-z]{2}$")]
    private static partial Regex StatePattern();

    public static Dictionary<string, string[]> Validate(CreateClientRequest request)
    {
        var errors = new Errors();
        errors.RequireLength("name", request.Name, 200);
        if (request.Industry is null)
        {
            errors.Add("industry", "Industry is required.");
        }
        return errors.Result;
    }

    public static Dictionary<string, string[]> Validate(CreateOrderRequest request, DateOnly today)
    {
        var errors = new Errors();
        if (request.ClientId is null || request.ClientId == Guid.Empty)
        {
            errors.Add("clientId", "Client id is required.");
        }
        errors.RequireLength("packageCode", request.PackageCode, 32);
        errors.RequireLength("clientReference", request.ClientReference, 64);

        var c = request.Candidate;
        if (c is null)
        {
            errors.Add("candidate", "Candidate is required.");
            return errors.Result;
        }

        errors.RequireLength("candidate.firstName", c.FirstName, 100);
        errors.RequireLength("candidate.lastName", c.LastName, 100);
        errors.RequireLength("candidate.email", c.Email, 254);
        if (!string.IsNullOrWhiteSpace(c.Email) && !c.Email.Contains('@', StringComparison.Ordinal))
        {
            errors.Add("candidate.email", "Email must be a valid address.");
        }
        if (c.Ssn is null || !SsnPattern().IsMatch(c.Ssn))
        {
            errors.Add("candidate.ssn", "SSN must be formatted NNN-NN-NNNN or 9 digits.");
        }
        if (c.DateOfBirth is not { } dob)
        {
            errors.Add("candidate.dateOfBirth", "Date of birth is required.");
        }
        else if (dob > today)
        {
            errors.Add("candidate.dateOfBirth", "Date of birth cannot be in the future.");
        }
        else if (dob > today.AddYears(-14))
        {
            errors.Add("candidate.dateOfBirth", "Candidate must be at least 14 years old.");
        }
        if (c.State is null || !StatePattern().IsMatch(c.State))
        {
            errors.Add("candidate.state", "State must be a two-letter code.");
        }
        errors.RequireLength("candidate.county", c.County, 100);
        return errors.Result;
    }

    public static Dictionary<string, string[]> Validate(CompleteSearchRequest request)
    {
        var errors = new Errors();
        if (request.Result is null)
        {
            errors.Add("result", "Result is required (Clear or Consider).");
        }
        return errors.Result;
    }

    public static Dictionary<string, string[]> Validate(ReviewRequest request)
    {
        var errors = new Errors();
        if (request.Decision is null)
        {
            errors.Add("decision", "Decision is required (Confirm or Dismiss).");
        }
        errors.RequireLength("reviewer", request.Reviewer, 100);
        return errors.Result;
    }

    private sealed class Errors
    {
        private readonly Dictionary<string, List<string>> _errors = [];

        public Dictionary<string, string[]> Result => _errors.ToDictionary(e => e.Key, e => e.Value.ToArray());

        public void Add(string field, string message)
        {
            if (!_errors.TryGetValue(field, out var list))
            {
                _errors[field] = list = [];
            }
            list.Add(message);
        }

        public void RequireLength(string field, string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Add(field, "Required.");
            }
            else if (value.Trim().Length > max)
            {
                Add(field, $"Must be at most {max} characters.");
            }
        }
    }
}
