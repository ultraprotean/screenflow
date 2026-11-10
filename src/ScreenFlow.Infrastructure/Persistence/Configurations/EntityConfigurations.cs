using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ScreenFlow.Domain;
using ScreenFlow.Infrastructure.Encryption;

namespace ScreenFlow.Infrastructure.Persistence.Configurations;

// Fluent configuration keeps persistence concerns out of the domain classes (no [Table] or
// [MaxLength] attributes there): the EF Core equivalent of keeping EDMX mapping out of the POCOs.

internal sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> b)
    {
        b.Property(c => c.Name).HasMaxLength(200);
        b.HasIndex(c => c.Name).IsUnique();
    }
}

internal sealed class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> b)
    {
        b.Property(p => p.Code).HasMaxLength(32);
        b.Property(p => p.Name).HasMaxLength(100);
        b.HasIndex(p => p.Code).IsUnique();
        b.HasMany(p => p.SearchTypes).WithOne().HasForeignKey(t => t.PackageId);
        b.Navigation(p => p.SearchTypes).HasField("_searchTypes").AutoInclude();

        b.HasData(PackageCatalog.All.Select(p => new { p.Id, p.Code, p.Name }));
    }
}

internal sealed class PackageSearchTypeConfiguration : IEntityTypeConfiguration<PackageSearchType>
{
    public void Configure(EntityTypeBuilder<PackageSearchType> b)
    {
        b.ToTable("PackageSearchTypes"); // no DbSet, so EF would otherwise use the singular class name
        b.HasKey(t => new { t.PackageId, t.SearchType });
        b.HasData(PackageCatalog.All.SelectMany(p => p.SearchTypes));
    }
}

internal sealed class CandidateConfiguration(FieldEncryptor encryptor) : IEntityTypeConfiguration<Candidate>
{
    public void Configure(EntityTypeBuilder<Candidate> b)
    {
        // Encrypted on the way in, decrypted on the way out; the column never holds plaintext.
        var ssnConverter = new ValueConverter<string, byte[]>(
            v => encryptor.Encrypt(v),
            v => encryptor.Decrypt(v));
        var dobConverter = new ValueConverter<DateOnly, byte[]>(
            v => encryptor.Encrypt(v.ToString("O", CultureInfo.InvariantCulture)),
            v => DateOnly.ParseExact(encryptor.Decrypt(v), "O", CultureInfo.InvariantCulture));

        b.Property(c => c.Ssn).HasColumnName("SsnEncrypted").HasConversion(ssnConverter).HasMaxLength(64);
        b.Property(c => c.DateOfBirth).HasColumnName("DateOfBirthEncrypted").HasConversion(dobConverter).HasMaxLength(64);
        b.Property(c => c.SsnLast4).HasMaxLength(4).IsFixedLength();
        b.Property(c => c.FirstName).HasMaxLength(100);
        b.Property(c => c.LastName).HasMaxLength(100);
        b.Property(c => c.Email).HasMaxLength(254);
        b.Property(c => c.State).HasMaxLength(2).IsFixedLength();
        b.Property(c => c.County).HasMaxLength(100);
        b.Ignore(c => c.SsnMasked);
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.Property(o => o.ClientReference).HasMaxLength(64);

        // The concurrency token lives on the aggregate root only; see Order.Version.
        // EF Core adds "WHERE Version = @original" to the UPDATE; zero rows affected = conflict.
        b.Property(o => o.Version).IsConcurrencyToken();

        b.HasOne<Client>().WithMany().HasForeignKey(o => o.ClientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(o => o.Candidate).WithMany().HasForeignKey(o => o.CandidateId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(o => o.Package).WithMany().HasForeignKey(o => o.PackageId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(o => o.Searches).WithOne().HasForeignKey(s => s.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(o => o.Searches).HasField("_searches");

        b.HasMany(o => o.AuditEvents).WithOne().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.NoAction);
        b.Navigation(o => o.AuditEvents).HasField("_auditEvents");

        // Idempotent order creation: the database, not just the code, refuses a duplicate.
        b.HasIndex(o => new { o.ClientId, o.ClientReference }).IsUnique();

        // List endpoint: filter by client (and optionally status), newest first, keyset on (SubmittedAt, Id).
        b.HasIndex(o => new { o.ClientId, o.SubmittedAt, o.Id }).IsDescending(false, true, true);
        b.HasIndex(o => new { o.ClientId, o.Status, o.SubmittedAt, o.Id }).IsDescending(false, false, true, true);
    }
}

internal sealed class SearchConfiguration : IEntityTypeConfiguration<Search>
{
    public void Configure(EntityTypeBuilder<Search> b)
    {
        b.Property(s => s.Jurisdiction).HasMaxLength(64);
        b.HasIndex(s => new { s.Status, s.Type }); // operations work queue
    }
}

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> b)
    {
        b.Property(e => e.Action).HasMaxLength(32);
        b.Property(e => e.FromStatus).HasMaxLength(32);
        b.Property(e => e.ToStatus).HasMaxLength(32);
        b.Property(e => e.Actor).HasMaxLength(100);
        b.HasIndex(e => new { e.OrderId, e.Id });
    }
}
