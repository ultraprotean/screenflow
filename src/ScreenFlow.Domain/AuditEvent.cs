namespace ScreenFlow.Domain;

/// <summary>One append-only history row. Never updated or deleted.</summary>
public sealed class AuditEvent
{
    public long Id { get; private set; } // identity, assigned by the database
    public Guid OrderId { get; private set; }
    public Guid? SearchId { get; private set; }
    public string Action { get; private set; } = "";
    public string? FromStatus { get; private set; }
    public string ToStatus { get; private set; } = "";
    public string Actor { get; private set; } = "";
    public DateTimeOffset OccurredAt { get; private set; }

    private AuditEvent() { } // for EF Core

    internal AuditEvent(
        Guid orderId, Guid? searchId, string action, string? fromStatus, string toStatus,
        string actor, DateTimeOffset occurredAt)
    {
        OrderId = orderId;
        SearchId = searchId;
        Action = action;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Actor = actor;
        OccurredAt = occurredAt;
    }
}

public static class AuditActions
{
    public const string OrderPlaced = "OrderPlaced";
    public const string OrderStatusChanged = "OrderStatusChanged";
    public const string OrderReviewed = "OrderReviewed";
    public const string OrderCancelled = "OrderCancelled";
    public const string SearchStatusChanged = "SearchStatusChanged";
}

public static class Actors
{
    public const string System = "system";
    public const string Operator = "operator";
}
