namespace ScreenFlow.Domain;

/// <summary>An employer that orders screenings.</summary>
public sealed class Client
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public Industry Industry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Client() { } // for EF Core

    public static Client Create(string name, Industry industry, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Name = name.Trim(),
        Industry = industry,
        CreatedAt = now,
    };
}
