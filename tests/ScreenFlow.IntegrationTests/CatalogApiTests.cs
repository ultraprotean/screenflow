using System.Net;
using System.Net.Http.Json;
using System.Text;
using ScreenFlow.Api.Contracts;
using ScreenFlow.Domain;

namespace ScreenFlow.IntegrationTests;

/// <summary>CONTRACT.md §6.1–6.4: health, clients, packages.</summary>
[Collection(DatabaseTestGroup.Name)]
public sealed class CatalogApiTests(DatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Health_reports_healthy_with_the_database_reachable()
    {
        var response = await Http.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", (await response.Content.ReadFromJsonAsync<HealthBody>())!.Status);
    }

    [Fact]
    public async Task Create_client_returns_201_with_location_and_get_returns_it()
    {
        var name = $"Acme {Guid.NewGuid():N}";

        var created = await Http.PostAsJsonAsync("/api/v1/clients", new { name, industry = "Healthcare" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var client = await Read<ClientResponse>(created);
        Assert.Equal((name, Industry.Healthcare), (client.Name, client.Industry));

        var fetched = await Read<ClientResponse>(await Http.GetAsync(created.Headers.Location));
        Assert.Equal(client, fetched);
    }

    [Fact]
    public async Task Get_unknown_client_is_NOT_FOUND()
    {
        var response = await Http.GetAsync($"/api/v1/clients/{Guid.NewGuid()}");

        await AssertProblem(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Duplicate_client_name_is_VALIDATION_FAILED()
    {
        var name = $"Dup {Guid.NewGuid():N}";
        await Http.PostAsJsonAsync("/api/v1/clients", new { name, industry = "Energy" });

        var response = await Http.PostAsJsonAsync("/api/v1/clients", new { name, industry = "Energy" });

        var body = await AssertProblem(response, HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        Assert.True(body.GetProperty("errors").TryGetProperty("name", out _));
    }

    [Theory]
    [InlineData("""{"name":"","industry":"Energy"}""")]   // empty name
    [InlineData("""{"name":"X"}""")]                      // missing industry
    [InlineData("""{"name":"X","industry":"Mining"}""")]  // unknown enum value
    [InlineData("""{"name":"X",""")]                      // malformed JSON
    public async Task Invalid_client_body_is_VALIDATION_FAILED(string json)
    {
        var response = await Http.PostAsync(
            "/api/v1/clients", new StringContent(json, Encoding.UTF8, "application/json"));

        await AssertProblem(response, HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Packages_lists_the_seeded_catalog_with_search_types()
    {
        var packages = await Http.GetFromJsonAsync<List<PackageResponse>>("/api/v1/packages", Json);

        Assert.Equal(["DOT_DRIVER", "HEALTHCARE", "STANDARD"], packages!.Select(p => p.Code));
        Assert.Contains(SearchType.MotorVehicleRecord, packages![0].SearchTypes);
    }

    private sealed record HealthBody(string Status);
}
