namespace ScreenFlow.Domain;

/// <summary>
/// The aggregate root: one screening request and its searches. Every status change in the system
/// goes through a method here, which enforces CONTRACT.md §5 and appends one audit event per
/// transition. Callers never set a status directly.
/// </summary>
public sealed class Order
{
    private readonly List<Search> _searches = [];
    private readonly List<AuditEvent> _auditEvents = [];

    public Guid Id { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid CandidateId { get; private set; }
    public Guid PackageId { get; private set; }
    public string ClientReference { get; private set; } = "";
    public OrderStatus Status { get; private set; }
    public ScreeningResult? Result { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Optimistic concurrency token, incremented by every operation. Because it always changes, any
    /// change to the aggregate (even one that only touches a search) updates the order row, so two
    /// requests racing on different searches of the same order are caught (write skew). A timestamp
    /// can't do this job: two requests in the same clock tick would write the same value, and EF Core
    /// skips an UPDATE when nothing changed.
    /// </summary>
    public int Version { get; private set; }

    public Candidate Candidate { get; private set; } = null!;
    public Package Package { get; private set; } = null!;
    public IReadOnlyList<Search> Searches => _searches;

    /// <summary>
    /// Events appended by this instance. Not loaded when reading an order (history is queried
    /// separately); EF Core inserts whatever is added here on save.
    /// </summary>
    public IReadOnlyList<AuditEvent> AuditEvents => _auditEvents;

    private Order() { } // for EF Core

    public static Order Place(
        Client client, Package package, Candidate candidate, string clientReference, DateTimeOffset now)
    {
        var order = new Order
        {
            Id = Guid.CreateVersion7(now),
            ClientId = client.Id,
            CandidateId = candidate.Id,
            PackageId = package.Id,
            Candidate = candidate,
            Package = package,
            ClientReference = clientReference.Trim(),
            Status = OrderStatus.Submitted,
            SubmittedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

        foreach (var type in package.SearchTypes.Select(t => t.SearchType))
        {
            order._searches.Add(new Search(order.Id, type, JurisdictionFor(type, candidate), now));
        }

        order.Audit(null, AuditActions.OrderPlaced, null, OrderStatus.Submitted.ToString(), Actors.System, now);
        return order;
    }

    public void StartSearch(Guid searchId, string actor, DateTimeOffset now)
    {
        var search = FindSearch(searchId);
        var from = search.Status;
        search.Start(now);
        Touch(now);
        AuditSearch(search, from, actor, now);

        if (Status == OrderStatus.Submitted)
        {
            ChangeStatus(OrderStatus.InProgress, Actors.System, now);
        }
    }

    public void CompleteSearch(Guid searchId, ScreeningResult result, string actor, DateTimeOffset now)
    {
        var search = FindSearch(searchId);
        var from = search.Status;
        search.Complete(result, now);
        Touch(now);
        AuditSearch(search, from, actor, now);

        var remaining = _searches.Where(s => s.Status != SearchStatus.Cancelled).ToList();
        if (remaining.Any(s => s.Status != SearchStatus.Completed))
        {
            return;
        }

        if (remaining.Any(s => s.Result == ScreeningResult.Consider))
        {
            ChangeStatus(OrderStatus.ReadyForReview, Actors.System, now);
        }
        else
        {
            Result = ScreeningResult.Clear;
            CompletedAt = now;
            ChangeStatus(OrderStatus.Completed, Actors.System, now);
        }
    }

    public void Review(ScreeningResult decision, string reviewer, DateTimeOffset now)
    {
        if (Status != OrderStatus.ReadyForReview)
        {
            throw new InvalidTransitionException($"Order {Id} cannot be reviewed from {Status}.");
        }

        Result = decision;
        CompletedAt = now;
        Touch(now);
        ChangeStatus(OrderStatus.Completed, reviewer, now, AuditActions.OrderReviewed);
    }

    public void Cancel(string actor, DateTimeOffset now)
    {
        if (Status is not (OrderStatus.Submitted or OrderStatus.InProgress))
        {
            throw new InvalidTransitionException($"Order {Id} cannot be cancelled from {Status}.");
        }

        foreach (var search in _searches.Where(s => s.Status is SearchStatus.Pending or SearchStatus.InProgress))
        {
            var from = search.Status;
            search.Cancel();
            AuditSearch(search, from, actor, now);
        }

        Touch(now);
        ChangeStatus(OrderStatus.Cancelled, actor, now, AuditActions.OrderCancelled);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }

    private static string? JurisdictionFor(SearchType type, Candidate candidate) => type switch
    {
        SearchType.CriminalCounty => $"{candidate.State}-{candidate.County}",
        SearchType.CriminalFederal => candidate.State,
        _ => null,
    };

    private Search FindSearch(Guid searchId) =>
        _searches.SingleOrDefault(s => s.Id == searchId) ?? throw new SearchNotFoundException(Id, searchId);

    private void ChangeStatus(
        OrderStatus to, string actor, DateTimeOffset now, string action = AuditActions.OrderStatusChanged)
    {
        var from = Status;
        Status = to;
        Audit(null, action, from.ToString(), to.ToString(), actor, now);
    }

    private void AuditSearch(Search search, SearchStatus from, string actor, DateTimeOffset now) =>
        Audit(search.Id, AuditActions.SearchStatusChanged, from.ToString(), search.Status.ToString(), actor, now);

    private void Audit(Guid? searchId, string action, string? from, string to, string actor, DateTimeOffset now) =>
        _auditEvents.Add(new AuditEvent(Id, searchId, action, from, to, actor, now));
}
