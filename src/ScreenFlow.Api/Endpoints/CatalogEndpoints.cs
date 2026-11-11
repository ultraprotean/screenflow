using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ScreenFlow.Api.Contracts;
using ScreenFlow.Api.Errors;
using ScreenFlow.Domain;
using ScreenFlow.Infrastructure.Persistence;

namespace ScreenFlow.Api.Endpoints;

/// <summary>Clients and packages: CONTRACT.md §6.2–6.4.</summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/clients", CreateClient);
        app.MapGet("/clients/{clientId:guid}", GetClient).WithName("GetClient");
        app.MapGet("/packages", ListPackages);
        return app;
    }

    private static async Task<IResult> CreateClient(
        CreateClientRequest request, ScreenFlowDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var errors = RequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var name = request.Name!.Trim();
        if (await db.Clients.AnyAsync(c => c.Name == name, ct))
        {
            return DuplicateName();
        }

        var client = Client.Create(name, request.Industry!.Value, clock.GetUtcNow());
        db.Clients.Add(client);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return DuplicateName(); // lost a race with a concurrent create of the same name
        }

        return Results.CreatedAtRoute("GetClient", new { clientId = client.Id }, ClientResponse.From(client));
    }

    private static async Task<IResult> GetClient(Guid clientId, ScreenFlowDbContext db, CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(c => c.Id == clientId, ct);
        return client is null
            ? ApiProblems.NotFound($"Client {clientId} was not found.")
            : Results.Ok(ClientResponse.From(client));
    }

    private static async Task<IResult> ListPackages(ScreenFlowDbContext db, CancellationToken ct)
    {
        var packages = await db.Packages.AsNoTracking().OrderBy(p => p.Code).ToListAsync(ct);
        return Results.Ok(packages.Select(PackageResponse.From));
    }

    private static IResult DuplicateName() => ApiProblems.Validation("name", "A client with this name already exists.");
}
