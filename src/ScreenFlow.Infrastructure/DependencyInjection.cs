using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using ScreenFlow.Infrastructure.Encryption;
using ScreenFlow.Infrastructure.Persistence;

namespace ScreenFlow.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the DbContext and the field encryptor. The API supplies the values from configuration.</summary>
    public static IServiceCollection AddScreenFlowInfrastructure(
        this IServiceCollection services, string connectionString, string? base64EncryptionKey)
    {
        // Fail at startup, not on the first request that touches a candidate.
        services.AddSingleton(FieldEncryptor.FromBase64(base64EncryptionKey));
        services.AddDbContext<ScreenFlowDbContext>(options => options.UseSqlServer(connectionString));
        return services;
    }
}

/// <summary>
/// Used only by `dotnet ef` to build the model when adding migrations. The throwaway key is never
/// used to encrypt anything: generating a migration reads the model, not the data.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ScreenFlowDbContext>
{
    public ScreenFlowDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ScreenFlowDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ScreenFlow;Trusted_Connection=True")
            .Options;
        return new ScreenFlowDbContext(options, new FieldEncryptor(new byte[32]));
    }
}
