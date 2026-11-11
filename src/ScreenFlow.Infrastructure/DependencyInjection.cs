using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ScreenFlow.Infrastructure.Encryption;
using ScreenFlow.Infrastructure.Persistence;

namespace ScreenFlow.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "ScreenFlow";
    public const string EncryptionKeySetting = "Encryption:Key";

    /// <summary>
    /// Registers the DbContext and the field encryptor. Configuration is read when the services are
    /// first resolved, not here, so a test host can override it. The API resolves the encryptor
    /// right after building the app to fail fast on a missing key.
    /// </summary>
    public static IServiceCollection AddScreenFlowInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
            FieldEncryptor.FromBase64(sp.GetRequiredService<IConfiguration>()[EncryptionKeySetting]));

        services.AddDbContext<ScreenFlowDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} is not configured.");
            options.UseSqlServer(connectionString);
        });
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
