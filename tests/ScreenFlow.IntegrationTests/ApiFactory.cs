using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ScreenFlow.Infrastructure.Persistence;

namespace ScreenFlow.IntegrationTests;

/// <summary>
/// Hosts the real API in memory against the fixture's database. Two test seams are added:
/// a frozen clock and a fault injector; everything else is the production pipeline.
/// </summary>
public sealed class ApiFactory(DatabaseFixture database) : WebApplicationFactory<Program>
{
    /// <summary>Every request sees the same instant, so all orders share one SubmittedAt.</summary>
    public static readonly DateTimeOffset FrozenNow = new(2026, 11, 10, 18, 0, 0, TimeSpan.Zero);

    public FaultInjector Faults { get; } = new(database.ConnectionString);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:ScreenFlow", database.ConnectionString);
        builder.UseSetting("Encryption:Key", DatabaseFixture.TestEncryptionKey);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(new FrozenTimeProvider(FrozenNow));
            services.ConfigureDbContext<ScreenFlowDbContext>(o => o.AddInterceptors(Faults));
        });
    }

    private sealed class FrozenTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

/// <summary>
/// Simulates things that are hard to trigger over HTTP. Armed by a test, it fires once, just before
/// the API's next SaveChanges.
/// </summary>
public sealed class FaultInjector(string connectionString) : SaveChangesInterceptor
{
    private Func<Task>? _pending;

    /// <summary>Another writer changes the order between the API's read and its save.</summary>
    public void ConcurrentWriteTo(Guid orderId) => _pending = async () =>
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using DbCommand command = new SqlCommand(
            "UPDATE Orders SET Version = Version + 1 WHERE Id = @id", connection);
        command.Parameters.Add(new SqlParameter("@id", orderId));
        await command.ExecuteNonQueryAsync();
    };

    /// <summary>The save throws an unexpected exception whose message holds sensitive-looking data.</summary>
    public void Throw(Exception exception) => _pending = () => Task.FromException(exception);

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var pending = Interlocked.Exchange(ref _pending, null);
        if (pending is not null)
        {
            await pending();
        }
        return result;
    }
}
