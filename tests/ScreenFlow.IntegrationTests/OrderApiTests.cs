using System.Net;
using System.Net.Http.Json;
using ScreenFlow.Api.Contracts;
using ScreenFlow.Domain;

namespace ScreenFlow.IntegrationTests;

/// <summary>CONTRACT.md §6.5–6.12: every order endpoint and every error case.</summary>
[Collection(DatabaseTestGroup.Name)]
public sealed class OrderApiTests(DatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ---- 6.5 Place order -------------------------------------------------------------------------

    [Fact]
    public async Task Place_order_returns_201_with_searches_and_a_masked_ssn()
    {
        var response = await PostOrderAsync(await CreateClientAsync(), "DOT_DRIVER");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("900123456", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("900-12-3456", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("1990-04-02", raw, StringComparison.Ordinal);

        var order = await Read<OrderResponse>(response);
        Assert.Equal(response.Headers.Location!.AbsolutePath, $"/api/v1/orders/{order.Id}");
        Assert.Equal(OrderStatus.Submitted, order.Status);
        Assert.Equal("***-**-3456", order.Candidate.SsnMasked);
        Assert.Equal(
            [SearchType.CriminalCounty, SearchType.MotorVehicleRecord, SearchType.DrugTest, SearchType.EmploymentVerification],
            order.Searches.Select(s => s.Type));
        Assert.Equal("AZ-Maricopa", order.Searches[0].Jurisdiction);
    }

    [Fact]
    public async Task Placing_the_same_client_reference_again_returns_the_original_order()
    {
        var clientId = await CreateClientAsync();
        var first = await Read<OrderResponse>(await PostOrderAsync(clientId, reference: "REQ-REPLAY"));

        var replay = await PostOrderAsync(clientId, "HEALTHCARE", reference: "REQ-REPLAY");

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var second = await Read<OrderResponse>(replay);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("DOT_DRIVER", second.PackageCode); // the body of a replay is not re-applied
    }

    [Fact]
    public async Task Place_order_for_unknown_client_is_NOT_FOUND()
    {
        await AssertProblem(await PostOrderAsync(Guid.NewGuid()), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Place_order_for_unknown_package_is_NOT_FOUND()
    {
        var response = await PostOrderAsync(await CreateClientAsync(), "PLATINUM");

        await AssertProblem(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Theory]
    [InlineData("900-12-345", "1990-04-02", "candidate.ssn")]
    [InlineData("90012345a", "1990-04-02", "candidate.ssn")]
    [InlineData("900-12-3456", "2027-01-01", "candidate.dateOfBirth")] // future (clock frozen at 2026-11-10)
    [InlineData("900-12-3456", "2013-01-01", "candidate.dateOfBirth")] // under 14
    public async Task Invalid_candidate_is_VALIDATION_FAILED_without_echoing_the_value(string ssn, string dob, string field)
    {
        var response = await PostOrderAsync(await CreateClientAsync(), candidate: CandidateBody(ssn, dob));

        var body = await AssertProblem(response, HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        Assert.True(body.GetProperty("errors").TryGetProperty(field, out _));
        Assert.DoesNotContain(ssn, body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_fields_and_long_reference_are_all_reported()
    {
        var response = await Http.PostAsJsonAsync("/api/v1/orders", new
        {
            clientId = Guid.NewGuid(),
            packageCode = "STANDARD",
            clientReference = new string('R', 65),
            candidate = new { ssn = "900123456", dateOfBirth = "1990-04-02", state = "AZ" },
        });

        var errors = (await AssertProblem(response, HttpStatusCode.BadRequest, "VALIDATION_FAILED")).GetProperty("errors");
        foreach (var field in new[] { "clientReference", "candidate.firstName", "candidate.lastName", "candidate.email", "candidate.county" })
        {
            Assert.True(errors.TryGetProperty(field, out _), $"expected an error for {field}");
        }
    }

    // ---- 6.6 Get order ---------------------------------------------------------------------------

    [Fact]
    public async Task Get_order_returns_it_and_unknown_is_NOT_FOUND()
    {
        var order = await PlaceOrderAsync();

        var fetched = await Http.GetFromJsonAsync<OrderResponse>($"/api/v1/orders/{order.Id}", Json);
        Assert.Equal(order.Id, fetched!.Id);
        Assert.Equal(order.Searches.Select(s => s.Id), fetched.Searches.Select(s => s.Id));

        await AssertProblem(await Http.GetAsync($"/api/v1/orders/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    // ---- 6.7 List orders -------------------------------------------------------------------------

    [Fact]
    public async Task List_pages_through_every_order_once_newest_first()
    {
        // All five share one SubmittedAt (frozen clock), so paging depends on the Id tie-breaker.
        var clientId = await CreateClientAsync();
        var placed = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            placed.Add((await PlaceOrderAsync(clientId)).Id);
        }

        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var url = $"/api/v1/orders?clientId={clientId}&limit=2" + (cursor is null ? "" : $"&cursor={cursor}");
            var page = await Http.GetFromJsonAsync<OrderPageResponse>(url, Json);
            seen.AddRange(page!.Items.Select(o => o.Id));
            Assert.All(page.Items, o => Assert.Equal("Dana Reyes", o.CandidateName));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.Equal(3, pages);
        Assert.Equal(placed.Order(), seen.Order()); // every order, none twice
    }

    [Fact]
    public async Task List_filters_by_status()
    {
        var clientId = await CreateClientAsync();
        var cancelled = await PlaceOrderAsync(clientId);
        await PlaceOrderAsync(clientId);
        await PostOk($"/api/v1/orders/{cancelled.Id}/cancel");

        var page = await Http.GetFromJsonAsync<OrderPageResponse>(
            $"/api/v1/orders?clientId={clientId}&status=Cancelled", Json);

        Assert.Equal([cancelled.Id], page!.Items.Select(o => o.Id));
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task List_finds_an_order_by_client_reference()
    {
        var clientId = await CreateClientAsync();
        var target = await Read<OrderResponse>(await PostOrderAsync(clientId, reference: "REQ-10442"));
        await PostOrderAsync(clientId, reference: "REQ-10443");

        var found = await Http.GetFromJsonAsync<OrderPageResponse>(
            $"/api/v1/orders?clientId={clientId}&clientReference=%20req-10442%20", Json); // trimmed, any case

        Assert.Equal([target.Id], found!.Items.Select(o => o.Id));
        Assert.Equal("REQ-10442", found.Items[0].ClientReference);
    }

    [Fact]
    public async Task List_by_unknown_or_other_clients_reference_is_an_empty_page()
    {
        var clientId = await CreateClientAsync();
        await PostOrderAsync(clientId, reference: "REQ-MINE");
        var otherClient = await CreateClientAsync();

        var unknown = await Http.GetFromJsonAsync<OrderPageResponse>(
            $"/api/v1/orders?clientId={clientId}&clientReference=REQ-NOPE", Json);
        var otherClients = await Http.GetFromJsonAsync<OrderPageResponse>(
            $"/api/v1/orders?clientId={otherClient}&clientReference=REQ-MINE", Json);

        Assert.Empty(unknown!.Items);
        Assert.Empty(otherClients!.Items); // references are scoped to the client
    }

    [Theory]
    [InlineData("")]                                   // missing clientId
    [InlineData("clientId=not-a-guid")]
    [InlineData("clientId={client}&limit=0")]
    [InlineData("clientId={client}&limit=101")]
    [InlineData("clientId={client}&status=Pending")]   // a search status, not an order status
    [InlineData("clientId={client}&cursor=garbage!")]
    [InlineData("clientId={client}&clientReference=")]
    [InlineData("clientId={client}&clientReference=RRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRR")] // 65 chars
    public async Task Bad_list_parameters_are_VALIDATION_FAILED(string query)
    {
        var response = await Http.GetAsync("/api/v1/orders?" + query.Replace("{client}", Guid.NewGuid().ToString(), StringComparison.Ordinal));

        await AssertProblem(response, HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    // ---- 6.8 / 6.9 Start and complete searches -----------------------------------------------------

    [Fact]
    public async Task Completing_every_search_clear_completes_the_order()
    {
        var order = await PlaceOrderAsync(packageCode: "STANDARD");

        var started = await PostOk(SearchUrl(order, 0, "start"));
        Assert.Equal(OrderStatus.InProgress, started.Status);

        var done = await CompleteAllAsync(started, "Clear", "Clear", "Clear");

        Assert.Equal(OrderStatus.Completed, done.Status);
        Assert.Equal(ScreeningResult.Clear, done.Result);
        Assert.Equal(ApiFactory.FrozenNow, done.CompletedAt);
    }

    [Fact]
    public async Task Starting_a_search_twice_is_INVALID_TRANSITION()
    {
        var order = await PlaceOrderAsync();
        await PostOk(SearchUrl(order, 0, "start"));

        var response = await Http.PostAsync(SearchUrl(order, 0, "start"), null);

        await AssertProblem(response, HttpStatusCode.Conflict, "INVALID_TRANSITION");
    }

    [Fact]
    public async Task Completing_a_pending_search_is_INVALID_TRANSITION()
    {
        var order = await PlaceOrderAsync();

        var response = await Http.PostAsJsonAsync(SearchUrl(order, 0, "complete"), new { result = "Clear" });

        await AssertProblem(response, HttpStatusCode.Conflict, "INVALID_TRANSITION");
    }

    [Fact]
    public async Task Complete_without_a_result_is_VALIDATION_FAILED()
    {
        var order = await PlaceOrderAsync();
        await PostOk(SearchUrl(order, 0, "start"));

        var response = await Http.PostAsJsonAsync(SearchUrl(order, 0, "complete"), new { });

        await AssertProblem(response, HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Unknown_order_or_search_is_NOT_FOUND()
    {
        var order = await PlaceOrderAsync();

        await AssertProblem(
            await Http.PostAsync($"/api/v1/orders/{order.Id}/searches/{Guid.NewGuid()}/start", null),
            HttpStatusCode.NotFound, "NOT_FOUND");
        await AssertProblem(
            await Http.PostAsync($"/api/v1/orders/{Guid.NewGuid()}/searches/{order.Searches[0].Id}/start", null),
            HttpStatusCode.NotFound, "NOT_FOUND");
    }

    // ---- 6.10 Review ------------------------------------------------------------------------------

    [Theory]
    [InlineData("Confirm", ScreeningResult.Consider)]
    [InlineData("Dismiss", ScreeningResult.Clear)]
    public async Task A_Consider_finding_goes_to_review_and_verification_sets_the_reported_result(
        string decision, ScreeningResult reported)
    {
        var order = await PlaceOrderAsync(packageCode: "STANDARD");
        order = await CompleteAllAsync(order, "Clear", "Consider", "Clear");
        Assert.Equal(OrderStatus.ReadyForReview, order.Status);

        var reviewed = await PostOk($"/api/v1/orders/{order.Id}/review", new { decision, reviewer = "j.walker" });

        Assert.Equal(OrderStatus.Completed, reviewed.Status);
        Assert.Equal(reported, reviewed.Result);
    }

    [Fact]
    public async Task Review_with_a_report_result_instead_of_a_decision_is_VALIDATION_FAILED()
    {
        var order = await PlaceOrderAsync(packageCode: "STANDARD");
        order = await CompleteAllAsync(order, "Clear", "Consider", "Clear");

        // "Clear" is a ScreeningResult (what the report says), not a ReviewDecision.
        var response = await Http.PostAsJsonAsync($"/api/v1/orders/{order.Id}/review", new { decision = "Clear", reviewer = "j.walker" });

        await AssertProblem(response, HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Review_before_searches_finish_is_INVALID_TRANSITION()
    {
        var order = await PlaceOrderAsync();

        var response = await Http.PostAsJsonAsync($"/api/v1/orders/{order.Id}/review", new { decision = "Dismiss", reviewer = "j.walker" });

        await AssertProblem(response, HttpStatusCode.Conflict, "INVALID_TRANSITION");
    }

    [Fact]
    public async Task Review_without_reviewer_is_VALIDATION_FAILED()
    {
        var order = await PlaceOrderAsync();

        var response = await Http.PostAsJsonAsync($"/api/v1/orders/{order.Id}/review", new { decision = "Confirm" });

        await AssertProblem(response, HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    // ---- 6.11 Cancel ------------------------------------------------------------------------------

    [Fact]
    public async Task Cancel_cancels_the_order_and_a_second_cancel_is_INVALID_TRANSITION()
    {
        var order = await PlaceOrderAsync();

        var cancelled = await PostOk($"/api/v1/orders/{order.Id}/cancel");
        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.All(cancelled.Searches, s => Assert.Equal(SearchStatus.Cancelled, s.Status));

        await AssertProblem(
            await Http.PostAsync($"/api/v1/orders/{order.Id}/cancel", null), HttpStatusCode.Conflict, "INVALID_TRANSITION");
    }

    // ---- 6.12 Audit -------------------------------------------------------------------------------

    [Fact]
    public async Task Audit_lists_every_transition_oldest_first_with_actors()
    {
        var order = await PlaceOrderAsync(packageCode: "STANDARD");
        order = await CompleteAllAsync(order, "Consider", "Clear", "Clear");
        await PostOk($"/api/v1/orders/{order.Id}/review", new { decision = "Dismiss", reviewer = "j.walker" });

        var audit = await Http.GetFromJsonAsync<List<AuditEventResponse>>($"/api/v1/orders/{order.Id}/audit", Json);

        // placed, 3 x (start + complete), InProgress, ReadyForReview, reviewed
        Assert.Equal(10, audit!.Count);
        Assert.Equal(audit.Select(e => e.Id).Order(), audit.Select(e => e.Id));
        Assert.Equal((AuditActions.OrderPlaced, Actors.System), (audit[0].Action, audit[0].Actor));
        Assert.Equal((AuditActions.OrderReviewed, "j.walker", "Completed"), (audit[^1].Action, audit[^1].Actor, audit[^1].ToStatus));
        Assert.Equal(6, audit.Count(e => e.Action == AuditActions.SearchStatusChanged && e.Actor == Actors.Operator));
    }

    [Fact]
    public async Task Audit_for_unknown_order_is_NOT_FOUND()
    {
        await AssertProblem(await Http.GetAsync($"/api/v1/orders/{Guid.NewGuid()}/audit"), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    // ---- Cross-cutting errors ----------------------------------------------------------------------

    [Fact]
    public async Task A_concurrent_write_between_read_and_save_is_CONCURRENCY_CONFLICT()
    {
        var order = await PlaceOrderAsync();
        Faults.ConcurrentWriteTo(order.Id);

        var response = await Http.PostAsync(SearchUrl(order, 0, "start"), null);

        await AssertProblem(response, HttpStatusCode.Conflict, "CONCURRENCY_CONFLICT");
        var reread = await Http.GetFromJsonAsync<OrderResponse>($"/api/v1/orders/{order.Id}", Json);
        Assert.Equal(SearchStatus.Pending, reread!.Searches[0].Status); // the losing write changed nothing
    }

    [Fact]
    public async Task Unexpected_errors_are_INTERNAL_ERROR_and_never_expose_the_exception_message()
    {
        var order = await PlaceOrderAsync();
        Faults.Throw(new InvalidOperationException("boom: candidate SSN 900-12-3456"));

        var response = await Http.PostAsync($"/api/v1/orders/{order.Id}/cancel", null);

        var body = await AssertProblem(response, HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        Assert.DoesNotContain("900-12-3456", body.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("boom", body.GetRawText(), StringComparison.Ordinal);
    }
}
