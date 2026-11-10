namespace ScreenFlow.Domain;

/// <summary>
/// The person being screened. <see cref="Ssn"/> and <see cref="DateOfBirth"/> are plain values in
/// memory; the infrastructure layer encrypts them on the way to the database (CONTRACT.md §3).
/// </summary>
public sealed class Candidate
{
    public Guid Id { get; private set; }
    public string FirstName { get; private set; } = "";
    public string LastName { get; private set; } = "";
    public string Email { get; private set; } = "";

    /// <summary>Nine digits, no dashes. Never log or return this.</summary>
    public string Ssn { get; private set; } = "";

    /// <summary>Stored in plain text so the API can show ***-**-1234 without decrypting.</summary>
    public string SsnLast4 { get; private set; } = "";

    /// <summary>Never log or return this.</summary>
    public DateOnly DateOfBirth { get; private set; }

    public string State { get; private set; } = "";
    public string County { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }

    public string SsnMasked => $"***-**-{SsnLast4}";

    private Candidate() { } // for EF Core

    /// <param name="ssn">Format is validated by the API; dashes are stripped here.</param>
    public static Candidate Create(
        string firstName, string lastName, string email, string ssn, DateOnly dateOfBirth,
        string state, string county, DateTimeOffset now)
    {
        var digits = new string(ssn.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != 9)
        {
            // Deliberately does not echo the value: it is an SSN.
            throw new ArgumentException("SSN must contain exactly nine digits.", nameof(ssn));
        }

        return new Candidate
        {
            Id = Guid.CreateVersion7(now),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Email = email.Trim(),
            Ssn = digits,
            SsnLast4 = digits[^4..],
            DateOfBirth = dateOfBirth,
            State = state.Trim().ToUpperInvariant(),
            County = county.Trim(),
            CreatedAt = now,
        };
    }
}
