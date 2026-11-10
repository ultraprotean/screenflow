namespace ScreenFlow.Domain;

/// <summary>A catalog entry: a named bundle of search types (e.g. DOT_DRIVER).</summary>
public sealed class Package
{
    private readonly List<PackageSearchType> _searchTypes = [];

    public Guid Id { get; private set; }
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public IReadOnlyList<PackageSearchType> SearchTypes => _searchTypes;

    private Package() { } // for EF Core

    public static Package Create(Guid id, string code, string name, params SearchType[] searchTypes)
    {
        var package = new Package { Id = id, Code = code, Name = name };
        package._searchTypes.AddRange(searchTypes.Select(t => new PackageSearchType(id, t)));
        return package;
    }
}

/// <summary>Join row: one search type within a package.</summary>
public sealed record PackageSearchType(Guid PackageId, SearchType SearchType);
