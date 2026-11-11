using ScreenFlow.Domain;

namespace ScreenFlow.Api.Contracts;

// Request and response shapes from CONTRACT.md §6. These are deliberately separate from the domain
// entities: the API never serializes an entity, so a new entity property can't leak (an SSN, say).

public sealed record CreateClientRequest(string? Name, Industry? Industry);

public sealed record ClientResponse(Guid Id, string Name, Industry Industry, DateTimeOffset CreatedAt)
{
    public static ClientResponse From(Client c) => new(c.Id, c.Name, c.Industry, c.CreatedAt);
}

public sealed record PackageResponse(Guid Id, string Code, string Name, IReadOnlyList<SearchType> SearchTypes)
{
    public static PackageResponse From(Package p) =>
        new(p.Id, p.Code, p.Name, p.SearchTypes.Select(t => t.SearchType).Order().ToList());
}

public sealed record CandidateRequest(
    string? FirstName, string? LastName, string? Email, string? Ssn, DateOnly? DateOfBirth, string? State, string? County);

public sealed record CreateOrderRequest(Guid? ClientId, string? PackageCode, string? ClientReference, CandidateRequest? Candidate);

public sealed record CompleteSearchRequest(ScreeningResult? Result);

public sealed record ReviewRequest(ReviewDecision? Decision, string? Reviewer);

/// <summary>Never carries the SSN or date of birth: only <see cref="SsnMasked"/>.</summary>
public sealed record CandidateResponse(
    string FirstName, string LastName, string Email, string SsnMasked, string State, string County);

public sealed record SearchResponse(
    Guid Id, SearchType Type, string? Jurisdiction, SearchStatus Status, ScreeningResult? Result,
    DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt);

public sealed record OrderResponse(
    Guid Id, Guid ClientId, string ClientReference, string PackageCode, OrderStatus Status, ScreeningResult? Result,
    DateTimeOffset SubmittedAt, DateTimeOffset? CompletedAt, CandidateResponse Candidate, IReadOnlyList<SearchResponse> Searches)
{
    public static OrderResponse From(Order o) => new(
        o.Id, o.ClientId, o.ClientReference, o.Package.Code, o.Status, o.Result, o.SubmittedAt, o.CompletedAt,
        new CandidateResponse(
            o.Candidate.FirstName, o.Candidate.LastName, o.Candidate.Email, o.Candidate.SsnMasked,
            o.Candidate.State, o.Candidate.County),
        o.Searches
            .OrderBy(s => s.Type)
            .Select(s => new SearchResponse(s.Id, s.Type, s.Jurisdiction, s.Status, s.Result, s.StartedAt, s.CompletedAt))
            .ToList());
}

public sealed record OrderSummaryResponse(
    Guid Id, Guid ClientId, string ClientReference, string PackageCode, OrderStatus Status, ScreeningResult? Result,
    DateTimeOffset SubmittedAt, DateTimeOffset? CompletedAt, string CandidateName);

public sealed record OrderPageResponse(IReadOnlyList<OrderSummaryResponse> Items, string? NextCursor);

public sealed record AuditEventResponse(
    long Id, Guid? SearchId, string Action, string? FromStatus, string ToStatus, string Actor, DateTimeOffset OccurredAt);
