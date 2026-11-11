using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ScreenFlow.Domain;
using ScreenFlow.Infrastructure.Persistence;

namespace ScreenFlow.IntegrationTests;

/// <summary>BUILD_SEQUENCE unit 2: persistence against a real, freshly migrated SQL Server database.</summary>
[Collection(DatabaseTestGroup.Name)]
public sealed class PersistenceTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Migration_seeds_the_package_catalog()
    {
        await using var db = fixture.CreateContext();

        var packages = await db.Packages.OrderBy(p => p.Code).ToListAsync();

        Assert.Equal(["DOT_DRIVER", "HEALTHCARE", "STANDARD"], packages.Select(p => p.Code));
        Assert.Equal(
            [SearchType.CriminalCounty, SearchType.MotorVehicleRecord, SearchType.DrugTest, SearchType.EmploymentVerification],
            packages[0].SearchTypes.Select(t => t.SearchType).Order());
    }

    [Fact]
    public async Task Order_round_trips_with_searches_audit_and_decrypted_candidate()
    {
        var orderId = await SaveNewOrderAsync();

        await using var db = fixture.CreateContext();
        var order = await LoadAsync(db, orderId);

        Assert.Equal(OrderStatus.Submitted, order.Status);
        Assert.Equal(4, order.Searches.Count);
        Assert.Equal("900123456", order.Candidate.Ssn);
        Assert.Equal(new DateOnly(1990, 4, 2), order.Candidate.DateOfBirth);
        Assert.Equal("AZ-Maricopa", order.Searches.Single(s => s.Type == SearchType.CriminalCounty).Jurisdiction);

        var audit = await db.AuditEvents.Where(e => e.OrderId == orderId).ToListAsync();
        var placed = Assert.Single(audit);
        Assert.True(placed.Id > 0); // identity assigned by SQL Server
    }

    [Fact]
    public async Task Ssn_and_date_of_birth_are_unreadable_in_raw_sql()
    {
        var first = await SaveNewOrderAsync();
        var second = await SaveNewOrderAsync(); // same SSN and DOB

        var rowA = await ReadCandidateColumnsAsync(first);
        var rowB = await ReadCandidateColumnsAsync(second);

        foreach (var bytes in new[] { rowA.Ssn, rowA.Dob })
        {
            var asText = Encoding.UTF8.GetString(bytes) + Encoding.Unicode.GetString(bytes);
            Assert.DoesNotContain("900123456", asText, StringComparison.Ordinal);
            Assert.DoesNotContain("1990-04-02", asText, StringComparison.Ordinal);
        }
        Assert.Equal("3456", rowA.Last4);
        // Random nonce per value: identical SSNs produce different ciphertext.
        Assert.NotEqual(rowA.Ssn, rowB.Ssn);
    }

    [Fact]
    public async Task Database_rejects_a_duplicate_client_reference()
    {
        await using var db = fixture.CreateContext();
        var client = Client.Create($"Dup Client {Guid.NewGuid():N}", Industry.Energy, Now);
        var package = await db.Packages.SingleAsync(p => p.Code == "STANDARD");
        db.Clients.Add(client);
        db.Orders.Add(Order.Place(client, package, NewCandidate(), "REQ-DUP", Now));
        db.Orders.Add(Order.Place(client, package, NewCandidate(), "REQ-DUP", Now));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        var sql = Assert.IsType<SqlException>(ex.InnerException);
        Assert.Equal(2601, sql.Number); // duplicate key in a unique index
    }

    [Fact]
    public async Task Database_rejects_a_duplicate_package_code()
    {
        // Orders select a package by code, so two packages with one code would make that lookup ambiguous.
        await using var db = fixture.CreateContext();
        db.Packages.Add(Package.Create(Guid.NewGuid(), "STANDARD", "Impostor", SearchType.DrugTest));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        var sql = Assert.IsType<SqlException>(ex.InnerException);
        Assert.Equal(2601, sql.Number);
        Assert.Contains("IX_Packages_Code", sql.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_completion_of_the_last_two_searches_is_detected()
    {
        // Arrange: an order with two searches, both in progress.
        var orderId = await SaveOrderWithTwoSearchesInProgressAsync();

        // Two requests read the same order at the same time...
        await using var requestA = fixture.CreateContext();
        await using var requestB = fixture.CreateContext();
        var orderA = await LoadAsync(requestA, orderId);
        var orderB = await LoadAsync(requestB, orderId);

        // ...and each completes a different search. Each sees the other search still in progress.
        orderA.CompleteSearch(orderA.Searches[0].Id, ScreeningResult.Clear, Actors.Operator, Now);
        orderB.CompleteSearch(orderB.Searches[1].Id, ScreeningResult.Clear, Actors.Operator, Now);
        Assert.Equal(OrderStatus.InProgress, orderA.Status);
        Assert.Equal(OrderStatus.InProgress, orderB.Status);

        await requestA.SaveChangesAsync();

        // Without the order-level token this save would succeed and the order would never complete.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => requestB.SaveChangesAsync());
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private static Candidate NewCandidate() => Candidate.Create(
        "Dana", "Reyes", "dana@example.com", "900-12-3456", new DateOnly(1990, 4, 2), "AZ", "Maricopa", Now);

    private async Task<Guid> SaveNewOrderAsync() => await SaveAsync(order => { });

    /// <summary>A STANDARD order (three searches) with one completed and two in progress.</summary>
    private async Task<Guid> SaveOrderWithTwoSearchesInProgressAsync() => await SaveAsync(order =>
    {
        foreach (var search in order.Searches)
        {
            order.StartSearch(search.Id, Actors.Operator, Now);
        }
        order.CompleteSearch(order.Searches[2].Id, ScreeningResult.Clear, Actors.Operator, Now);
    }, "STANDARD");

    private async Task<Guid> SaveAsync(Action<Order> arrange, string packageCode = "DOT_DRIVER")
    {
        await using var db = fixture.CreateContext();
        var client = Client.Create($"Client {Guid.NewGuid():N}", Industry.Transportation, Now);
        db.Clients.Add(client);

        // Packages are seeded rows: load them through this context so EF tracks them as existing
        // (attaching a new Package instance would make EF try to INSERT it).
        var package = await db.Packages.SingleAsync(p => p.Code == packageCode);
        var order = Order.Place(client, package, NewCandidate(), $"REQ-{Guid.NewGuid():N}", Now);
        arrange(order);

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static Task<Order> LoadAsync(ScreenFlowDbContext db, Guid orderId) => db.Orders
        .Include(o => o.Searches.OrderBy(s => s.Type))
        .Include(o => o.Candidate)
        .Include(o => o.Package)
        .SingleAsync(o => o.Id == orderId);

    private async Task<(byte[] Ssn, byte[] Dob, string Last4)> ReadCandidateColumnsAsync(Guid orderId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            SELECT c.SsnEncrypted, c.DateOfBirthEncrypted, c.SsnLast4
            FROM Candidates c JOIN Orders o ON o.CandidateId = c.Id
            WHERE o.Id = @orderId
            """, connection);
        command.Parameters.AddWithValue("@orderId", orderId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return ((byte[])reader[0], (byte[])reader[1], reader.GetString(2));
    }
}
