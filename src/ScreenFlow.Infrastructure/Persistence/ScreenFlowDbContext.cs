using Microsoft.EntityFrameworkCore;
using ScreenFlow.Domain;
using ScreenFlow.Infrastructure.Encryption;
using ScreenFlow.Infrastructure.Persistence.Configurations;

namespace ScreenFlow.Infrastructure.Persistence;

/// <remarks>
/// EF Core builds the model once per context type and caches it, so the value converters (and
/// the encryption key they capture) are fixed for the life of the process. One key per process
/// is what we want; key rotation would mean re-encrypting rows, not swapping the key at runtime.
/// </remarks>
public sealed class ScreenFlowDbContext(DbContextOptions<ScreenFlowDbContext> options, FieldEncryptor encryptor)
    : DbContext(options)
{
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Search> Searches => Set<Search>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums as readable strings in every table (raw SQL and the warehouse export stay legible).
        configurationBuilder.Properties<Industry>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<SearchType>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<OrderStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<SearchStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<ScreeningResult>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ClientConfiguration());
        modelBuilder.ApplyConfiguration(new PackageConfiguration());
        modelBuilder.ApplyConfiguration(new PackageSearchTypeConfiguration());
        modelBuilder.ApplyConfiguration(new CandidateConfiguration(encryptor));
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new SearchConfiguration());
        modelBuilder.ApplyConfiguration(new AuditEventConfiguration());
    }
}
