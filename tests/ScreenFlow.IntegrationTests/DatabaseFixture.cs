using Microsoft.EntityFrameworkCore;
using ScreenFlow.Infrastructure.Encryption;
using ScreenFlow.Infrastructure.Persistence;

namespace ScreenFlow.IntegrationTests;

/// <summary>
/// One real, freshly migrated SQL Server database per test run, dropped afterwards. Never mocked:
/// constraints, rowversion and collation behave exactly as in production.
/// Server: SCREENFLOW_TEST_SQLSERVER if set (CI uses a SQL Server container), else LocalDB.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    // Public, test-only key: it encrypts synthetic data in throwaway databases. Real keys come from
    // user-secrets locally or a secrets manager in a deployment, never from source.
    public const string TestEncryptionKey = "c2NyZWVuZmxvdy10ZXN0LWtleS0zMi1ieXRlcy1vayE=";

    private const string DefaultServer =
        "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=True";

    public string ConnectionString { get; }
    public FieldEncryptor Encryptor { get; } = FieldEncryptor.FromBase64(TestEncryptionKey);

    public DatabaseFixture()
    {
        var server = Environment.GetEnvironmentVariable("SCREENFLOW_TEST_SQLSERVER") ?? DefaultServer;
        ConnectionString = $"{server};Database=ScreenFlowTests_{Guid.NewGuid():N}";
    }

    public ScreenFlowDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ScreenFlowDbContext>().UseSqlServer(ConnectionString).Options,
        Encryptor);

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    private ApiFactory? _api;

    /// <summary>The in-memory API host, created on first use and shared by the API tests.</summary>
    public ApiFactory Api => _api ??= new ApiFactory(this);

    public async Task DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseTestGroup : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Database";
}
