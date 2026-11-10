using ScreenFlow.Domain;

namespace ScreenFlow.Infrastructure.Persistence;

/// <summary>
/// The seeded package catalog (CONTRACT.md §4). Ids are fixed because seed data is part of a
/// migration: a new Guid on every build would make EF Core generate a new migration each time.
/// </summary>
public static class PackageCatalog
{
    public static readonly Package Standard = Package.Create(
        new Guid("0193a000-0000-7000-8000-000000000001"), "STANDARD", "Standard Employment",
        SearchType.CriminalCounty, SearchType.CriminalFederal, SearchType.EmploymentVerification);

    public static readonly Package DotDriver = Package.Create(
        new Guid("0193a000-0000-7000-8000-000000000002"), "DOT_DRIVER", "DOT Driver",
        SearchType.CriminalCounty, SearchType.MotorVehicleRecord, SearchType.DrugTest, SearchType.EmploymentVerification);

    public static readonly Package Healthcare = Package.Create(
        new Guid("0193a000-0000-7000-8000-000000000003"), "HEALTHCARE", "Healthcare",
        SearchType.CriminalCounty, SearchType.CriminalFederal, SearchType.DrugTest, SearchType.EducationVerification);

    public static IReadOnlyList<Package> All { get; } = [Standard, DotDriver, Healthcare];
}
