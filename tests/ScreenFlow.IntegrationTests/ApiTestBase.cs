using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenFlow.Api.Contracts;

namespace ScreenFlow.IntegrationTests;

/// <summary>Shared HTTP helpers for the API tests (BUILD_SEQUENCE unit 3).</summary>
public abstract class ApiTestBase(DatabaseFixture fixture)
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected HttpClient Http { get; } = fixture.Api.CreateClient();
    protected FaultInjector Faults => fixture.Api.Faults;

    protected static object CandidateBody(string ssn = "900-12-3456", string dateOfBirth = "1990-04-02") => new
    {
        firstName = "Dana", lastName = "Reyes", email = "dana@example.com",
        ssn, dateOfBirth, state = "AZ", county = "Maricopa",
    };

    protected async Task<Guid> CreateClientAsync()
    {
        var response = await Http.PostAsJsonAsync(
            "/api/v1/clients", new { name = $"Client {Guid.NewGuid():N}", industry = "Transportation" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Read<ClientResponse>(response)).Id;
    }

    protected Task<HttpResponseMessage> PostOrderAsync(
        Guid clientId, string packageCode = "DOT_DRIVER", string? reference = null, object? candidate = null) =>
        Http.PostAsJsonAsync("/api/v1/orders", new
        {
            clientId,
            packageCode,
            clientReference = reference ?? $"REQ-{Guid.NewGuid():N}"[..20],
            candidate = candidate ?? CandidateBody(),
        });

    protected async Task<OrderResponse> PlaceOrderAsync(Guid? clientId = null, string packageCode = "DOT_DRIVER")
    {
        var response = await PostOrderAsync(clientId ?? await CreateClientAsync(), packageCode);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Read<OrderResponse>(response);
    }

    protected async Task<OrderResponse> PostOk(string url, object? body = null)
    {
        var response = await Http.PostAsJsonAsync(url, body ?? new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Read<OrderResponse>(response);
    }

    protected static string SearchUrl(OrderResponse order, int index, string action) =>
        $"/api/v1/orders/{order.Id}/searches/{order.Searches[index].Id}/{action}";

    /// <summary>Starts (if still pending) and completes every search with the given results.</summary>
    protected async Task<OrderResponse> CompleteAllAsync(OrderResponse order, params string[] results)
    {
        for (var i = 0; i < order.Searches.Count; i++)
        {
            if (order.Searches[i].Status == Domain.SearchStatus.Pending)
            {
                await PostOk(SearchUrl(order, i, "start"));
            }
            order = await PostOk(SearchUrl(order, i, "complete"), new { result = results[i] });
        }
        return order;
    }

    protected static async Task<T> Read<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    /// <summary>Asserts an RFC 9457 problem response with the contract's error code.</summary>
    protected static async Task<JsonElement> AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal((int)status, body.GetProperty("status").GetInt32());
        Assert.True(body.TryGetProperty("traceId", out _));
        return body;
    }
}
