using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ScreenFlow.Api.Contracts;
using ScreenFlow.Api.Errors;
using ScreenFlow.Domain;
using ScreenFlow.Infrastructure.Persistence;

namespace ScreenFlow.Api.Endpoints;

/// <summary>Orders, searches, review, cancel and audit: CONTRACT.md §6.5–6.12.</summary>
public static class OrderEndpoints
{
    private const int DefaultLimit = 25;
    private const int MaxLimit = 100;

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders");
        orders.MapPost("/", PlaceOrder);
        orders.MapGet("/", ListOrders);
        orders.MapGet("/{orderId:guid}", GetOrder).WithName("GetOrder");
        orders.MapGet("/{orderId:guid}/audit", GetAudit);

        // The operations below load the order, call one domain method, and save. The domain enforces
        // the state machine; the exception mapper turns a refusal into INVALID_TRANSITION.
        orders.MapPost("/{orderId:guid}/searches/{searchId:guid}/start",
            (Guid orderId, Guid searchId, ScreenFlowDbContext db, TimeProvider clock, CancellationToken ct) =>
                Mutate(orderId, db, o => o.StartSearch(searchId, Actors.Operator, clock.GetUtcNow()), ct));

        orders.MapPost("/{orderId:guid}/searches/{searchId:guid}/complete",
            (Guid orderId, Guid searchId, CompleteSearchRequest request, ScreenFlowDbContext db, TimeProvider clock,
                CancellationToken ct) =>
            {
                var errors = RequestValidator.Validate(request);
                return errors.Count > 0
                    ? Task.FromResult(ApiProblems.Validation(errors))
                    : Mutate(orderId, db, o => o.CompleteSearch(searchId, request.Result!.Value, Actors.Operator, clock.GetUtcNow()), ct);
            });

        orders.MapPost("/{orderId:guid}/review",
            (Guid orderId, ReviewRequest request, ScreenFlowDbContext db, TimeProvider clock, CancellationToken ct) =>
            {
                var errors = RequestValidator.Validate(request);
                return errors.Count > 0
                    ? Task.FromResult(ApiProblems.Validation(errors))
                    : Mutate(orderId, db, o => o.Review(request.Decision!.Value, request.Reviewer!.Trim(), clock.GetUtcNow()), ct);
            });

        orders.MapPost("/{orderId:guid}/cancel",
            (Guid orderId, ScreenFlowDbContext db, TimeProvider clock, CancellationToken ct) =>
                Mutate(orderId, db, o => o.Cancel(Actors.Operator, clock.GetUtcNow()), ct));

        return app;
    }

    private static async Task<IResult> PlaceOrder(
        CreateOrderRequest request, ScreenFlowDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var errors = RequestValidator.Validate(request, DateOnly.FromDateTime(now.UtcDateTime));
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var clientId = request.ClientId!.Value;
        var reference = request.ClientReference!.Trim();

        // Idempotent replay: the same client reference returns the order already placed.
        if (await FindByReferenceAsync(db, clientId, reference, ct) is { } existing)
        {
            return Results.Ok(OrderResponse.From(existing));
        }

        var client = await db.Clients.SingleOrDefaultAsync(c => c.Id == clientId, ct);
        if (client is null)
        {
            return ApiProblems.NotFound($"Client {clientId} was not found.");
        }
        var packageCode = request.PackageCode!.Trim().ToUpperInvariant();
        var package = await db.Packages.SingleOrDefaultAsync(p => p.Code == packageCode, ct);
        if (package is null)
        {
            return ApiProblems.NotFound($"Package {packageCode} was not found.");
        }

        var c = request.Candidate!;
        var candidate = Candidate.Create(
            c.FirstName!, c.LastName!, c.Email!, c.Ssn!, c.DateOfBirth!.Value, c.State!, c.County!, now);
        var order = Order.Place(client, package, candidate, reference, now);
        db.Orders.Add(order);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Two identical requests raced past the check above; the unique index stopped the second.
            db.ChangeTracker.Clear();
            var winner = await FindByReferenceAsync(db, clientId, reference, ct);
            return Results.Ok(OrderResponse.From(winner!));
        }

        return Results.CreatedAtRoute("GetOrder", new { orderId = order.Id }, OrderResponse.From(order));
    }

    private static async Task<IResult> GetOrder(Guid orderId, ScreenFlowDbContext db, CancellationToken ct)
    {
        var order = await LoadOrder(db).AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, ct);
        return order is null ? OrderNotFound(orderId) : Results.Ok(OrderResponse.From(order));
    }

    private static async Task<IResult> ListOrders(
        string? clientId, string? status, string? clientReference, string? limit, string? cursor,
        ScreenFlowDbContext db, CancellationToken ct)
    {
        // Query parameters are bound as strings and parsed here, so a bad value produces the
        // contract's VALIDATION_FAILED shape rather than the framework's default 400.
        if (!Guid.TryParse(clientId, out var client))
        {
            return ApiProblems.Validation("clientId", "clientId is required and must be a GUID.");
        }
        OrderStatus? statusFilter = null;
        if (status is not null)
        {
            if (!Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return ApiProblems.Validation("status", "Unknown status.");
            }
            statusFilter = parsed;
        }
        // Look up an order by the client's own id (e.g. "where is REQ-10442?"). Exact match after
        // trimming; case-insensitive under SQL Server's default collation, like the unique index.
        var reference = clientReference?.Trim();
        if (clientReference is not null && (reference!.Length == 0 || reference.Length > 64))
        {
            return ApiProblems.Validation("clientReference", "clientReference must be 1 to 64 characters.");
        }
        var pageSize = DefaultLimit;
        if (limit is not null && (!int.TryParse(limit, out pageSize) || pageSize is < 1 or > MaxLimit))
        {
            return ApiProblems.Validation("limit", $"limit must be between 1 and {MaxLimit}.");
        }
        OrderCursor? after = null;
        if (cursor is not null)
        {
            if (!OrderCursor.TryDecode(cursor, out var decoded))
            {
                return ApiProblems.Validation("cursor", "Invalid cursor.");
            }
            after = decoded;
        }

        var query = db.Orders.AsNoTracking().Where(o => o.ClientId == client);
        if (statusFilter is { } s)
        {
            query = query.Where(o => o.Status == s);
        }
        if (reference is not null)
        {
            query = query.Where(o => o.ClientReference == reference); // served by the unique index
        }
        if (after is { } a)
        {
            // Keyset: everything strictly "after" the last row in (SubmittedAt DESC, Id DESC) order.
            query = query.Where(o => o.SubmittedAt < a.SubmittedAt
                || (o.SubmittedAt == a.SubmittedAt && o.Id.CompareTo(a.Id) < 0));
        }

        // Fetch one extra row to know whether there is a next page without a COUNT query.
        var rows = await query
            .OrderByDescending(o => o.SubmittedAt).ThenByDescending(o => o.Id)
            .Take(pageSize + 1)
            .Select(o => new OrderSummaryResponse(
                o.Id, o.ClientId, o.ClientReference, o.Package.Code, o.Status, o.Result, o.SubmittedAt, o.CompletedAt,
                o.Candidate.FirstName + " " + o.Candidate.LastName))
            .ToListAsync(ct);

        var page = rows.Take(pageSize).ToList();
        var next = rows.Count > pageSize ? new OrderCursor(page[^1].SubmittedAt, page[^1].Id).Encode() : null;
        return Results.Ok(new OrderPageResponse(page, next));
    }

    private static async Task<IResult> GetAudit(Guid orderId, ScreenFlowDbContext db, CancellationToken ct)
    {
        if (!await db.Orders.AnyAsync(o => o.Id == orderId, ct))
        {
            return OrderNotFound(orderId);
        }
        var events = await db.AuditEvents.AsNoTracking()
            .Where(e => e.OrderId == orderId)
            .OrderBy(e => e.Id)
            .Select(e => new AuditEventResponse(e.Id, e.SearchId, e.Action, e.FromStatus, e.ToStatus, e.Actor, e.OccurredAt))
            .ToListAsync(ct);
        return Results.Ok(events);
    }

    private static async Task<IResult> Mutate(
        Guid orderId, ScreenFlowDbContext db, Action<Order> operation, CancellationToken ct)
    {
        var order = await LoadOrder(db).SingleOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
        {
            return OrderNotFound(orderId);
        }

        operation(order);
        await db.SaveChangesAsync(ct); // DbUpdateConcurrencyException -> CONCURRENCY_CONFLICT
        return Results.Ok(OrderResponse.From(order));
    }

    // IgnoreAutoIncludes: Package.SearchTypes is auto-included everywhere else, but here only the
    // package code is needed. Two collection includes in one query multiply rows (searches x types).
    private static IQueryable<Order> LoadOrder(ScreenFlowDbContext db) => db.Orders
        .IgnoreAutoIncludes()
        .Include(o => o.Searches)
        .Include(o => o.Candidate)
        .Include(o => o.Package);

    private static Task<Order?> FindByReferenceAsync(
        ScreenFlowDbContext db, Guid clientId, string reference, CancellationToken ct) =>
        LoadOrder(db).AsNoTracking().SingleOrDefaultAsync(o => o.ClientId == clientId && o.ClientReference == reference, ct);

    private static IResult OrderNotFound(Guid orderId) => ApiProblems.NotFound($"Order {orderId} was not found.");
}
