using ScreenFlow.Domain;

namespace ScreenFlow.UnitTests;

/// <summary>Test data helpers. SSNs use the 900 area, which is never issued.</summary>
internal static class OrderBuilder
{
    public static readonly DateTimeOffset Now = new(2026, 11, 10, 18, 0, 0, TimeSpan.Zero);

    public static Order Place(params SearchType[] types)
    {
        if (types.Length == 0)
        {
            types = [SearchType.CriminalCounty, SearchType.DrugTest];
        }

        var client = Client.Create("Acme Freight", Industry.Transportation, Now);
        var package = Package.Create(Guid.NewGuid(), "TEST", "Test package", types);
        var candidate = Candidate.Create(
            "Dana", "Reyes", "dana@example.com", "900-12-3456", new DateOnly(1990, 4, 2), "az", " Maricopa ", Now);
        return Order.Place(client, package, candidate, "REQ-1", Now);
    }

    /// <summary>Starts and completes every search with the given results, in order.</summary>
    public static void CompleteAll(Order order, params ScreeningResult[] results)
    {
        for (var i = 0; i < order.Searches.Count; i++)
        {
            var id = order.Searches[i].Id;
            if (order.Searches[i].Status == SearchStatus.Pending)
            {
                order.StartSearch(id, Actors.Operator, Now);
            }
            order.CompleteSearch(id, results[i], Actors.Operator, Now.AddHours(i + 1));
        }
    }
}
