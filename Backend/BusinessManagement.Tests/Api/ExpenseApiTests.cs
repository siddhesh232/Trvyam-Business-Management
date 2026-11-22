using System.Net;
using System.Net.Http.Json;
using BusinessManagement.Application.Common;
using BusinessManagement.Application.Expenses;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Tests.Database;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Tests.Api;

[Collection("Api")]
public sealed class ExpenseApiTests(ApiFixture fx)
{
    private readonly TestApi _api = new(fx);
    private static string Today(int daysAgo = 0) => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-daysAgo)).ToString("yyyy-MM-dd");
    private static object Body(string name, decimal qty, decimal price, long? purchasedBy = null, string? date = null, decimal gst = 0, bool eligible = false) =>
        new { expenseDate = date ?? Today(), name, quantity = qty, unitPrice = price, purchasedByUserId = purchasedBy, inputGstAmount = gst, isInputGstEligible = eligible };

    [SqlFact]
    public async Task Member_adds_an_expense_as_purchaser_and_the_total_is_computed()
    {
        var admin = await _api.AdminAsync();
        var biz = await _api.NewBusinessAsync(admin);
        var (m, user) = await _api.AddMemberAsync(admin, biz.Id, "CanAddExpenses", "CanViewExpenseTotal");

        var e = await TestApi.Ok<ExpenseDto>(await m.PostAsJsonAsync($"/api/v1/businesses/{biz.Id}/expenses", Body("Cement", 3, 12.50m)), HttpStatusCode.Created);
        e.TotalAmount.Should().Be(37.50m);
        (e.PurchasedByUserId, e.CreatedByUserId).Should().Be((user.Id, user.Id));
        e.CanEdit.Should().BeFalse("EditOwnExpenses is not granted");
    }

    [SqlFact]
    public async Task Admin_must_name_the_purchaser_and_the_purchaser_must_belong_to_the_business()
    {
        var admin = await _api.AdminAsync();
        var biz = await _api.NewBusinessAsync(admin);
        var (_, buyer) = await _api.AddMemberAsync(admin, biz.Id);
        var outsider = await _api.NewUserAsync(admin);
        var url = $"/api/v1/businesses/{biz.Id}/expenses";

        (await admin.PostAsJsonAsync(url, Body("No buyer", 1, 10m))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync(url, Body("Outsider buyer", 1, 10m, outsider.Id))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var ok = await TestApi.Ok<ExpenseDto>(await admin.PostAsJsonAsync(url, Body("Valid", 2, 10m, buyer.Id)), HttpStatusCode.Created);
        ok.PurchasedByName.Should().Be(buyer.Name);
        ok.PurchasedByUserId.Should().Be(buyer.Id);
        ok.CreatedByUserId.Should().Be(fx.Db.AdminId, "the Admin entered it; purchaser and creator are different people");
        ok.CreatedByName.Should().NotBe(ok.PurchasedByName);
    }

    [SqlFact]
    public async Task Expense_input_is_validated()
    {
        var admin = await _api.AdminAsync();
        var biz = await _api.NewBusinessAsync(admin);
        var (m, _) = await _api.AddMemberAsync(admin, biz.Id, "CanAddExpenses");
        var url = $"/api/v1/businesses/{biz.Id}/expenses";
        (await m.PostAsJsonAsync(url, Body("Future", 1, 10m, date: Today(-10)))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await m.PostAsJsonAsync(url, Body("Negative", 1, -5m))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await m.PostAsJsonAsync(url, Body("Zero qty", 0, 5m))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await m.PostAsJsonAsync(url, Body("Too much GST", 1, 100m, gst: 150m, eligible: true))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var (noPerm, _) = await _api.AddMemberAsync(admin, biz.Id);
        (await noPerm.PostAsJsonAsync(url, Body("No permission", 1, 1m))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SqlFact]
    public async Task Members_edit_and_delete_only_their_own_records_and_the_admin_can_manage_all()
    {
        var admin = await _api.AdminAsync();
        var biz = await _api.NewBusinessAsync(admin);
        var perms = new[] { "CanAddExpenses", "CanEditOwnExpenses", "CanDeleteOwnExpenses", "CanViewMemberWiseExpenses" };
        var (a, _) = await _api.AddMemberAsync(admin, biz.Id, perms);
        var (b, _) = await _api.AddMemberAsync(admin, biz.Id, perms);
        var url = $"/api/v1/businesses/{biz.Id}/expenses";

        var mine = await TestApi.Ok<ExpenseDto>(await a.PostAsJsonAsync(url, Body("A's item", 1, 100m)), HttpStatusCode.Created);
        mine.CanEdit.Should().BeTrue();

        // B can SEE A's record (member-wise visibility) but visibility never grants edit/delete rights.
        (await b.PutAsJsonAsync($"{url}/{mine.Id}", Body("Hijacked", 1, 1m))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await b.DeleteAsync($"{url}/{mine.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var edited = await TestApi.Ok<ExpenseDto>(await a.PutAsJsonAsync($"{url}/{mine.Id}", Body("A's item v2", 2, 100m)));
        edited.TotalAmount.Should().Be(200m, "the total is recomputed after an edit");
        (await admin.PutAsJsonAsync($"{url}/{mine.Id}", Body("Admin fix", 1, 90m, edited.PurchasedByUserId))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await a.DeleteAsync($"{url}/{mine.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SqlFact]
    public async Task Member_wise_breakdown_is_permission_gated_labelled_and_reconciles_with_the_total()
    {
        var admin = await _api.AdminAsync();
        var biz = await _api.NewBusinessAsync(admin);
        var (x, xu) = await _api.AddMemberAsync(admin, biz.Id, "CanAddExpenses", "CanViewExpenseTotal", "CanViewMemberWiseExpenses");
        var (y, yu) = await _api.AddMemberAsync(admin, biz.Id, "CanAddExpenses", "CanViewExpenseTotal");     // total only
        var (z, zu) = await _api.AddMemberAsync(admin, biz.Id, "CanAddExpenses");                              // neither
        var url = $"/api/v1/businesses/{biz.Id}/expenses";

        await x.PostAsJsonAsync(url, Body("X buys", 1, 100m));                                    // X bought, X entered
        await y.PostAsJsonAsync(url, Body("Y buys", 2, 50m, date: Today(40)));                    // 100, old date
        await z.PostAsJsonAsync(url, Body("Z buys", 1, 25m));                                     // 25
        await admin.PostAsJsonAsync(url, Body("Admin enters for Y", 1, 10m, yu.Id));              // Y bought, Admin entered

        // --- Y: total allowed, breakdown refused, record list limited to Y's own (bought or entered)
        var yTotal = await TestApi.Ok<ExpenseSummaryDto>(await y.GetAsync($"{url}/summary"));
        yTotal.TotalAmount.Should().Be(235m);
        (await y.GetAsync($"{url}/by-member")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var yList = await TestApi.Ok<PagedResult<ExpenseDto>>(await y.GetAsync(url));
        yList.Items.Select(i => i.Name).Should().BeEquivalentTo("Y buys", "Admin enters for Y");
        (await y.GetAsync($"{url}?purchasedBy={xu.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // --- Z: no total, no breakdown, only own records
        (await z.GetAsync($"{url}/summary")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await z.GetAsync($"{url}/by-member")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await TestApi.Ok<PagedResult<ExpenseDto>>(await z.GetAsync(url))).Items.Should().ContainSingle().Which.Name.Should().Be("Z buys");

        // --- X: full visibility; both groupings reconcile with the summary for identical filters
        var byBuyer = await TestApi.Ok<ExpenseByMemberDto>(await x.GetAsync($"{url}/by-member?basis=purchasedBy"));
        byBuyer.Basis.Should().Be("PurchasedBy");
        byBuyer.BasisLabel.Should().Contain("PURCHASED");
        byBuyer.Rows.Select(r => (r.Name, r.TotalAmount)).Should().BeEquivalentTo(new[] { (xu.Name, 100m), (yu.Name, 110m), (zu.Name, 25m) });

        var byEntrant = await TestApi.Ok<ExpenseByMemberDto>(await x.GetAsync($"{url}/by-member?basis=createdBy"));
        byEntrant.Basis.Should().Be("CreatedBy");
        byEntrant.Rows.Should().Contain(r => r.UserId == fx.Db.AdminId && r.TotalAmount == 10m);

        var summary = await TestApi.Ok<ExpenseSummaryDto>(await x.GetAsync($"{url}/summary"));
        summary.TotalAmount.Should().Be(235m);
        byBuyer.GrandTotal.Should().Be(summary.TotalAmount);
        byEntrant.GrandTotal.Should().Be(summary.TotalAmount);

        // Same date filter on both => still reconciles (excludes Y's 40-day-old purchase).
        var range = $"from={Today(7)}&to={Today()}";
        var s2 = await TestApi.Ok<ExpenseSummaryDto>(await x.GetAsync($"{url}/summary?{range}"));
        var b2 = await TestApi.Ok<ExpenseByMemberDto>(await x.GetAsync($"{url}/by-member?{range}"));
        s2.TotalAmount.Should().Be(135m);
        b2.Rows.Sum(r => r.TotalAmount).Should().Be(s2.TotalAmount);
        (await x.GetAsync($"{url}/by-member?basis=bogus")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Admin sees everything regardless of flags.
        (await TestApi.Ok<ExpenseByMemberDto>(await admin.GetAsync($"{url}/by-member"))).GrandTotal.Should().Be(235m);
    }

    [SqlFact]
    public async Task Deleted_expenses_leave_the_totals_but_stay_in_the_database_and_audit_log()
    {
        var admin = await _api.AdminAsync();
        var biz = await _api.NewBusinessAsync(admin);
        var (m, user) = await _api.AddMemberAsync(admin, biz.Id, "CanAddExpenses", "CanDeleteOwnExpenses", "CanViewExpenseTotal");
        var url = $"/api/v1/businesses/{biz.Id}/expenses";
        var keep = await TestApi.Ok<ExpenseDto>(await m.PostAsJsonAsync(url, Body("Keep", 1, 40m)), HttpStatusCode.Created);
        var gone = await TestApi.Ok<ExpenseDto>(await m.PostAsJsonAsync(url, Body("Gone", 1, 60m, gst: 9m, eligible: true)), HttpStatusCode.Created);

        (await TestApi.Ok<ExpenseSummaryDto>(await m.GetAsync($"{url}/summary"))).EligibleInputGst.Should().Be(9m);
        (await m.DeleteAsync($"{url}/{gone.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var s = await TestApi.Ok<ExpenseSummaryDto>(await m.GetAsync($"{url}/summary"));
        (s.TotalAmount, s.Count, s.EligibleInputGst).Should().Be((40m, 1, 0m));

        await using var db = fx.Db.Create();
        var row = await db.Expenses.IgnoreQueryFilters().SingleAsync(e => e.Id == gone.Id);
        (row.IsDeleted, row.DeletedBy).Should().Be((true, user.Id));
        (await db.AuditLogs.AnyAsync(a => a.EntityName == "Expense" && a.EntityId == gone.Id && a.Action == AuditAction.Delete)).Should().BeTrue();
        keep.Id.Should().NotBe(gone.Id);
    }

    [SqlFact]
    public async Task Expense_list_filters_by_date_item_and_member_for_privileged_viewers()
    {
        var admin = await _api.AdminAsync();
        var biz = await _api.NewBusinessAsync(admin);
        var (a, au) = await _api.AddMemberAsync(admin, biz.Id, "CanAddExpenses");
        var (b, bu) = await _api.AddMemberAsync(admin, biz.Id, "CanAddExpenses");
        var url = $"/api/v1/businesses/{biz.Id}/expenses";
        await a.PostAsJsonAsync(url, Body("Steel rods", 1, 10m, date: Today(30)));
        await a.PostAsJsonAsync(url, Body("Paint", 1, 20m));
        await b.PostAsJsonAsync(url, Body("Paint brush", 1, 5m));

        (await TestApi.Ok<PagedResult<ExpenseDto>>(await admin.GetAsync($"{url}?q=paint"))).Total.Should().Be(2);
        (await TestApi.Ok<PagedResult<ExpenseDto>>(await admin.GetAsync($"{url}?purchasedBy={au.Id}"))).Total.Should().Be(2);
        (await TestApi.Ok<PagedResult<ExpenseDto>>(await admin.GetAsync($"{url}?from={Today(7)}"))).Total.Should().Be(2);
        (await TestApi.Ok<PagedResult<ExpenseDto>>(await admin.GetAsync($"{url}?to={Today(7)}"))).Items.Should().ContainSingle().Which.Name.Should().Be("Steel rods");
        (await TestApi.Ok<PagedResult<ExpenseDto>>(await admin.GetAsync($"{url}?purchasedBy={bu.Id}&pageSize=1"))).Items.Should().HaveCount(1);
        (await admin.GetAsync($"{url}?from={Today()}&to={Today(5)}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
