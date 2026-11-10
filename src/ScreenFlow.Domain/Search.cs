namespace ScreenFlow.Domain;

/// <summary>
/// One unit of screening work. Transitions are internal: callers go through <see cref="Order"/>,
/// which keeps the order's own status and the audit trail consistent.
/// </summary>
public sealed class Search
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public SearchType Type { get; private set; }
    public string? Jurisdiction { get; private set; }
    public SearchStatus Status { get; private set; }
    public ScreeningResult? Result { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private Search() { } // for EF Core

    internal Search(Guid orderId, SearchType type, string? jurisdiction, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        OrderId = orderId;
        Type = type;
        Jurisdiction = jurisdiction;
        Status = SearchStatus.Pending;
        CreatedAt = now;
    }

    internal void Start(DateTimeOffset now)
    {
        Require(SearchStatus.Pending, "start");
        Status = SearchStatus.InProgress;
        StartedAt = now;
    }

    internal void Complete(ScreeningResult result, DateTimeOffset now)
    {
        Require(SearchStatus.InProgress, "complete");
        Status = SearchStatus.Completed;
        Result = result;
        CompletedAt = now;
    }

    internal void Cancel()
    {
        if (Status is not (SearchStatus.Pending or SearchStatus.InProgress))
        {
            throw Invalid("cancel");
        }
        Status = SearchStatus.Cancelled;
    }

    private void Require(SearchStatus expected, string verb)
    {
        if (Status != expected)
        {
            throw Invalid(verb);
        }
    }

    private InvalidTransitionException Invalid(string verb) =>
        new($"Search {Id} cannot {verb} from {Status}.");
}
