using ScreenFlow.Domain;
using static ScreenFlow.UnitTests.OrderBuilder;

namespace ScreenFlow.UnitTests;

/// <summary>CONTRACT.md §5: every allowed and every forbidden transition.</summary>
public class OrderStateMachineTests
{
    // ---- Placing an order --------------------------------------------------------------------

    [Fact]
    public void Place_creates_one_pending_search_per_package_type_with_jurisdictions()
    {
        var order = Place(SearchType.CriminalCounty, SearchType.CriminalFederal, SearchType.DrugTest);

        Assert.Equal(OrderStatus.Submitted, order.Status);
        Assert.All(order.Searches, s => Assert.Equal(SearchStatus.Pending, s.Status));
        Assert.Equal(["AZ-Maricopa", "AZ", null], order.Searches.Select(s => s.Jurisdiction));
        var placed = Assert.Single(order.AuditEvents);
        Assert.Equal((AuditActions.OrderPlaced, null, "Submitted"), (placed.Action, placed.FromStatus, placed.ToStatus));
    }

    [Fact]
    public void Candidate_stores_digits_only_and_masks_all_but_last_four()
    {
        var order = Place();

        Assert.Equal("900123456", order.Candidate.Ssn);
        Assert.Equal("***-**-3456", order.Candidate.SsnMasked);
    }

    [Theory]
    [InlineData("900-12-345")]
    [InlineData("900-12-34567")]
    public void Candidate_rejects_ssn_without_nine_digits_and_does_not_echo_it(string ssn)
    {
        var ex = Assert.Throws<ArgumentException>(() => Candidate.Create(
            "A", "B", "a@example.com", ssn, new DateOnly(1990, 1, 1), "AZ", "Pima", Now));

        Assert.DoesNotContain(ssn, ex.Message);
    }

    // ---- Allowed transitions -------------------------------------------------------------------

    [Fact]
    public void Starting_the_first_search_moves_the_order_to_InProgress()
    {
        var order = Place();

        order.StartSearch(order.Searches[0].Id, Actors.Operator, Now);

        Assert.Equal(SearchStatus.InProgress, order.Searches[0].Status);
        Assert.Equal(Now, order.Searches[0].StartedAt);
        Assert.Equal(OrderStatus.InProgress, order.Status);
        Assert.Equal(
            [AuditActions.OrderPlaced, AuditActions.SearchStatusChanged, AuditActions.OrderStatusChanged],
            order.AuditEvents.Select(e => e.Action));
    }

    [Fact]
    public void Starting_a_second_search_does_not_audit_the_order_again()
    {
        var order = Place();
        order.StartSearch(order.Searches[0].Id, Actors.Operator, Now);

        order.StartSearch(order.Searches[1].Id, Actors.Operator, Now);

        Assert.Equal(1, order.AuditEvents.Count(e => e.Action == AuditActions.OrderStatusChanged));
    }

    [Fact]
    public void All_searches_clear_completes_the_order_as_Clear()
    {
        var order = Place();

        CompleteAll(order, ScreeningResult.Clear, ScreeningResult.Clear);

        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(ScreeningResult.Clear, order.Result);
        Assert.Equal(Now.AddHours(2), order.CompletedAt);
        var last = order.AuditEvents[^1];
        Assert.Equal(("InProgress", "Completed", Actors.System), (last.FromStatus, last.ToStatus, last.Actor));
    }

    [Fact]
    public void Order_stays_InProgress_until_the_last_search_completes()
    {
        var order = Place();
        order.StartSearch(order.Searches[0].Id, Actors.Operator, Now);

        order.CompleteSearch(order.Searches[0].Id, ScreeningResult.Clear, Actors.Operator, Now);

        Assert.Equal(OrderStatus.InProgress, order.Status);
        Assert.Null(order.Result);
    }

    [Fact]
    public void Any_Consider_sends_the_order_to_review()
    {
        var order = Place();

        CompleteAll(order, ScreeningResult.Clear, ScreeningResult.Consider);

        Assert.Equal(OrderStatus.ReadyForReview, order.Status);
        Assert.Null(order.Result);
        Assert.Null(order.CompletedAt);
    }

    [Theory]
    [InlineData(ReviewDecision.Confirm, ScreeningResult.Consider)]
    [InlineData(ReviewDecision.Dismiss, ScreeningResult.Clear)]
    public void Review_verifies_the_findings_and_completes_the_order(ReviewDecision decision, ScreeningResult reported)
    {
        var order = Place();
        CompleteAll(order, ScreeningResult.Clear, ScreeningResult.Consider);

        order.Review(decision, "j.walker", Now.AddDays(1));

        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(reported, order.Result);
        Assert.Equal(ScreeningResult.Consider, order.Searches[1].Result); // the search keeps what was found
        Assert.Equal(Now.AddDays(1), order.CompletedAt);
        var last = order.AuditEvents[^1];
        Assert.Equal((AuditActions.OrderReviewed, "j.walker"), (last.Action, last.Actor));
    }

    [Fact]
    public void Cancel_from_Submitted_cancels_every_search()
    {
        var order = Place();

        order.Cancel(Actors.Operator, Now);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.All(order.Searches, s => Assert.Equal(SearchStatus.Cancelled, s.Status));
        Assert.Equal(AuditActions.OrderCancelled, order.AuditEvents[^1].Action);
    }

    [Fact]
    public void Cancel_from_InProgress_keeps_completed_searches()
    {
        var order = Place(SearchType.CriminalCounty, SearchType.DrugTest, SearchType.MotorVehicleRecord);
        order.StartSearch(order.Searches[0].Id, Actors.Operator, Now);
        order.CompleteSearch(order.Searches[0].Id, ScreeningResult.Clear, Actors.Operator, Now);
        order.StartSearch(order.Searches[1].Id, Actors.Operator, Now);

        order.Cancel(Actors.Operator, Now);

        Assert.Equal(
            [SearchStatus.Completed, SearchStatus.Cancelled, SearchStatus.Cancelled],
            order.Searches.Select(s => s.Status));
    }

    [Fact]
    public void Every_operation_increments_Version_even_when_only_a_search_changes()
    {
        var order = Place(SearchType.CriminalCounty, SearchType.DrugTest);
        order.StartSearch(order.Searches[0].Id, Actors.Operator, Now);
        var before = order.Version;

        // Order status stays InProgress here; only the second search changes.
        order.StartSearch(order.Searches[1].Id, Actors.Operator, Now);

        Assert.Equal(OrderStatus.InProgress, order.Status);
        Assert.Equal(before + 1, order.Version);
    }

    // ---- Forbidden transitions -----------------------------------------------------------------

    [Fact]
    public void Completing_a_pending_search_is_forbidden()
    {
        var order = Place();

        AssertForbidden(order, o => o.CompleteSearch(o.Searches[0].Id, ScreeningResult.Clear, Actors.Operator, Now));
    }

    [Fact]
    public void Starting_an_in_progress_search_is_forbidden()
    {
        var order = Place();
        order.StartSearch(order.Searches[0].Id, Actors.Operator, Now);

        AssertForbidden(order, o => o.StartSearch(o.Searches[0].Id, Actors.Operator, Now));
    }

    [Fact]
    public void Restarting_or_recompleting_a_completed_search_is_forbidden()
    {
        var order = Place();
        order.StartSearch(order.Searches[0].Id, Actors.Operator, Now);
        order.CompleteSearch(order.Searches[0].Id, ScreeningResult.Clear, Actors.Operator, Now);

        AssertForbidden(order, o => o.StartSearch(o.Searches[0].Id, Actors.Operator, Now));
        AssertForbidden(order, o => o.CompleteSearch(o.Searches[0].Id, ScreeningResult.Consider, Actors.Operator, Now));
    }

    [Fact]
    public void Starting_a_search_on_a_cancelled_order_is_forbidden()
    {
        var order = Place();
        order.Cancel(Actors.Operator, Now);

        AssertForbidden(order, o => o.StartSearch(o.Searches[0].Id, Actors.Operator, Now));
    }

    [Theory]
    [InlineData(OrderStatus.Submitted)]
    [InlineData(OrderStatus.InProgress)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void Review_is_forbidden_unless_ReadyForReview(OrderStatus status)
    {
        var order = OrderIn(status);

        AssertForbidden(order, o => o.Review(ReviewDecision.Dismiss, "j.walker", Now));
    }

    [Theory]
    [InlineData(OrderStatus.ReadyForReview)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void Cancel_is_forbidden_once_searches_are_done_or_already_cancelled(OrderStatus status)
    {
        var order = OrderIn(status);

        AssertForbidden(order, o => o.Cancel(Actors.Operator, Now));
    }

    [Fact]
    public void Unknown_search_id_throws_SearchNotFound()
    {
        var order = Place();

        Assert.Throws<SearchNotFoundException>(() => order.StartSearch(Guid.NewGuid(), Actors.Operator, Now));
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    /// <summary>A forbidden call throws and leaves statuses and the audit trail untouched.</summary>
    private static void AssertForbidden(Order order, Action<Order> act)
    {
        var statusBefore = order.Status;
        var searchesBefore = order.Searches.Select(s => s.Status).ToList();
        var auditBefore = order.AuditEvents.Count;
        var versionBefore = order.Version;

        Assert.Throws<InvalidTransitionException>(() => act(order));

        Assert.Equal(versionBefore, order.Version);
        Assert.Equal(statusBefore, order.Status);
        Assert.Equal(searchesBefore, order.Searches.Select(s => s.Status));
        Assert.Equal(auditBefore, order.AuditEvents.Count);
    }

    private static Order OrderIn(OrderStatus status)
    {
        var order = Place();
        switch (status)
        {
            case OrderStatus.Submitted:
                break;
            case OrderStatus.InProgress:
                order.StartSearch(order.Searches[0].Id, Actors.Operator, Now);
                break;
            case OrderStatus.ReadyForReview:
                CompleteAll(order, ScreeningResult.Consider, ScreeningResult.Clear);
                break;
            case OrderStatus.Completed:
                CompleteAll(order, ScreeningResult.Clear, ScreeningResult.Clear);
                break;
            case OrderStatus.Cancelled:
                order.Cancel(Actors.Operator, Now);
                break;
        }
        Assert.Equal(status, order.Status);
        return order;
    }
}
