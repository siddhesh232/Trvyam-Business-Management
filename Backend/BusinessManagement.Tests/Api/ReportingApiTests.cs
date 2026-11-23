using System.Net;
using System.Net.Http.Json;
using BusinessManagement.Application.Adjustments;
using BusinessManagement.Application.Audit;
using BusinessManagement.Application.Common;
using BusinessManagement.Application.Expenses;
using BusinessManagement.Application.Financials;
using BusinessManagement.Application.Users;
using BusinessManagement.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Tests.Api;

[Collection("Api")]
public sealed class ReportingApiTests(ApiFixture fx)
{
    private readonly TestApi _api = new(fx);
    private static string D(int daysAgo = 0) => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-daysAgo)).ToString("yyyy-MM-dd");

    private sealed record Scenario(HttpClient Admin, long BizId, string Base, UserListItemDto Buyer);

    /// <summary>
    /// One business with hand-calculable books (all dated today):
    ///  sale A 2 x 500 @18% = 1000 + 180 = 1180   sale B 5 x 200 @5% = 1000 + 50 = 1050
    ///  payments: A 1180 (paid in full), B 300.   return: 1 of A's 2 units = 500 + 90 = 590 (A now owes 590 but has paid 1180 -> refund 590)
    ///  expenses: 590 (incl. 90 eligible input GST) and 100.   adjustments: other income +50, loss -20.
    /// </summary>
    private async Task<Scenario> BuildAsync()
    {
        var admin = await _api.AdminAsync();
        var biz = await _api.NewBusinessAsync(admin);
        var p1 = await _api.NewProductAsync(admin, biz.Id, 500m, 18m);
        var p2 = await _api.NewProductAsync(admin, biz.Id, 200m, 5m);
        var customer = await _api.NewCustomerAsync(admin, biz.Id);
        var (_, buyer) = await _api.AddMemberAsync(admin, biz.Id);
        var b = $"/api/v1/businesses/{biz.Id}";

        async Task<BusinessManagement.Application.Sales.SalesOrderDto> Sale(long productId, decimal qty) =>
            await TestApi.Ok<BusinessManagement.Application.Sales.SalesOrderDto>(await admin.PostAsJsonAsync($"{b}/sales",
                new { customerId = customer.Id, orderDate = D(), items = new[] { new { productId, quantity = qty, itemDiscount = 0m } }, orderDiscount = 0m }), HttpStatusCode.Created);

        var a = await Sale(p1.Id, 2m);
        var s2 = await Sale(p2.Id, 5m);
        await admin.PostAsJsonAsync($"{b}/sales/{a.Id}/payments", new { paymentDate = D(), amount = 1180m, paymentMode = "Cash" });
        await admin.PostAsJsonAsync($"{b}/sales/{s2.Id}/payments", new { paymentDate = D(), amount = 300m, paymentMode = "Upi" });
        (await admin.PostAsJsonAsync($"{b}/sales/{a.Id}/returns", new { returnDate = D(), items = new[] { new { salesOrderItemId = a.Items[0].Id, quantity = 1m } } })).StatusCode.Should().Be(HttpStatusCode.Created);

        (await admin.PostAsJsonAsync($"{b}/expenses", new { expenseDate = D(), name = "Stock", quantity = 2m, unitPrice = 295m, purchasedByUserId = buyer.Id, inputGstAmount = 90m, isInputGstEligible = true })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await admin.PostAsJsonAsync($"{b}/expenses", new { expenseDate = D(), name = "Rent", quantity = 1m, unitPrice = 100m, purchasedByUserId = buyer.Id, inputGstAmount = 0m, isInputGstEligible = false })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await admin.PostAsJsonAsync($"{b}/adjustments", new { adjustmentDate = D(), type = "OtherIncome", direction = "IncreasesProfit", amount = 50m, description = "Scrap sale" })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await admin.PostAsJsonAsync($"{b}/adjustments", new { adjustmentDate = D(), type = "Loss", direction = "DecreasesProfit", amount = 20m, description = "Damaged stock" })).StatusCode.Should().Be(HttpStatusCode.Created);
        return new Scenario(admin, biz.Id, b, buyer);
    }

    [SqlFact]
    public async Task Admin_dashboard_matches_the_hand_calculated_books()
    {
        var s = await BuildAsync();
        var d = await TestApi.Ok<DashboardDto>(await s.Admin.GetAsync($"{s.Base}/dashboard"));

        d.TotalCustomers.Should().Be(1);
        d.Sales.Should().Be(new SalesFigures(GrossSales: 2000m, SalesReturns: 500m, NetSales: 1500m, TotalSalesAmount: 2230m, SalesCount: 2, QuantitySold: 6m, OutputGst: 140m));
        d.Payments.Should().Be(new PaymentFigures(AmountReceived: 1480m, OutstandingAmount: 750m, RefundsDue: 590m));
        d.Expenses.Should().Be(new ExpenseFigures(TotalExpenses: 690m, Count: 2, EligibleInputGst: 90m, ExpensesNetOfInputGst: 600m));
        d.Profit!.NetProfit.Should().Be(930m);                // 1500 - 600 + 30
        (d.Profit.OtherAdjustments, d.Profit.OutputGst, d.Profit.EligibleInputGst, d.Profit.NetGstPayable, d.Profit.CashFlow).Should().Be((30m, 140m, 90m, 50m, 790m));
        d.Profit.Formula.Should().Contain("not revenue");

        d.RecentSales.Should().HaveCount(2);
        d.RecentExpenses.Should().HaveCount(2);
        d.ExpenseByMember!.GrandTotal.Should().Be(d.Expenses!.TotalExpenses, "total and member-wise breakdown reconcile");

        // Date filter outside the activity window => zeros, not errors.
        var empty = await TestApi.Ok<DashboardDto>(await s.Admin.GetAsync($"{s.Base}/dashboard?from=2001-01-01&to=2001-01-31"));
        (empty.Sales!.NetSales, empty.Expenses!.TotalExpenses, empty.Profit!.NetProfit).Should().Be((0m, 0m, 0m));
        (await s.Admin.GetAsync($"{s.Base}/dashboard?from={D()}&to={D(5)}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Admin.GetAsync($"{s.Base}/dashboard?basis=bogus")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SqlFact]
    public async Task Members_see_only_the_dashboard_sections_their_permissions_allow()
    {
        var s = await BuildAsync();
        async Task<DashboardDto> As(params string[] perms)
        {
            var (client, _) = await _api.AddMemberAsync(s.Admin, s.BizId, perms);
            return await TestApi.Ok<DashboardDto>(await client.GetAsync($"{s.Base}/dashboard"));
        }

        var none = await As();
        (none.Sales, none.Payments, none.Expenses, none.Profit, none.ExpenseByMember, none.RecentSales, none.RecentExpenses).Should().Be(((SalesFigures?)null, (PaymentFigures?)null, (ExpenseFigures?)null, (ProfitFigures?)null, (BusinessManagement.Application.Expenses.ExpenseByMemberDto?)null, (IReadOnlyList<RecentSaleDto>?)null, (IReadOnlyList<ExpenseDto>?)null));

        var sales = await As("CanViewSales");
        sales.Sales!.NetSales.Should().Be(1500m);
        (sales.Profit, sales.Expenses, sales.Payments).Should().Be(((ProfitFigures?)null, (ExpenseFigures?)null, (PaymentFigures?)null));
        sales.RecentSales.Should().HaveCount(2);

        (await As("CanViewPayments")).Payments!.AmountReceived.Should().Be(1480m);
        var total = await As("CanViewExpenseTotal");
        total.Expenses!.TotalExpenses.Should().Be(690m);
        total.ExpenseByMember.Should().BeNull("member-wise visibility is a separate permission");
        total.RecentExpenses.Should().BeEmpty("without member-wise visibility only own records are listed, and this member has none");

        var reports = await As("CanViewReports");
        reports.Profit!.NetProfit.Should().Be(930m);

        var memberWise = await As("CanViewMemberWiseExpenses");
        memberWise.ExpenseByMember!.Rows.Should().ContainSingle(r => r.UserId == s.Buyer.Id && r.TotalAmount == 690m);
        memberWise.Expenses!.TotalExpenses.Should().Be(memberWise.ExpenseByMember.GrandTotal);
    }

    [SqlFact]
    public async Task Reports_add_up_to_the_dashboard_for_daily_weekly_monthly_and_custom_periods()
    {
        var s = await BuildAsync();
        var dash = await TestApi.Ok<DashboardDto>(await s.Admin.GetAsync($"{s.Base}/dashboard?from={D(2)}&to={D()}"));

        var daily = await TestApi.Ok<ReportDto>(await s.Admin.GetAsync($"{s.Base}/reports?period=daily&from={D(2)}&to={D()}"));
        daily.Buckets.Should().HaveCount(3);
        daily.Buckets[^1].NetSales.Should().Be(1500m);
        daily.Buckets[0].NetSales.Should().Be(0m);
        daily.Buckets.Sum(b => b.NetProfit).Should().Be(daily.Totals.NetProfit);
        daily.Totals.NetProfit.Should().Be(dash.Profit!.NetProfit);

        foreach (var period in new[] { "weekly", "monthly", "custom" })
        {
            var r = await TestApi.Ok<ReportDto>(await s.Admin.GetAsync($"{s.Base}/reports?period={period}&from={D(40)}&to={D()}"));
            r.Buckets.Sum(b => b.NetSales).Should().Be(1500m, period);
            r.Buckets.Sum(b => b.TotalExpenses).Should().Be(690m, period);
            r.Buckets.Sum(b => b.OtherAdjustments).Should().Be(30m, period);
            r.Totals.NetProfit.Should().Be(930m, period);
        }
        (await TestApi.Ok<ReportDto>(await s.Admin.GetAsync($"{s.Base}/reports?period=custom&from={D(40)}&to={D()}"))).Buckets.Should().ContainSingle();

        var monthlyDefault = await TestApi.Ok<ReportDto>(await s.Admin.GetAsync($"{s.Base}/reports"));
        monthlyDefault.Period.Should().Be("Monthly");
        monthlyDefault.Buckets.Count.Should().BeInRange(12, 13);
        monthlyDefault.Totals.NetSales.Should().Be(1500m);
    }

    [SqlFact]
    public async Task Report_inputs_are_validated_and_gated_by_the_reports_permission()
    {
        var s = await BuildAsync();
        (await s.Admin.GetAsync($"{s.Base}/reports?period=custom")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Admin.GetAsync($"{s.Base}/reports?period=yearly")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Admin.GetAsync($"{s.Base}/reports?period=daily&from={D(400)}&to={D()}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Admin.GetAsync($"{s.Base}/reports?from={D()}&to={D(3)}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (viewer, _) = await _api.AddMemberAsync(s.Admin, s.BizId, "CanViewSales", "CanViewExpenseTotal");
        (await viewer.GetAsync($"{s.Base}/reports")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var (reporter, _) = await _api.AddMemberAsync(s.Admin, s.BizId, "CanViewReports");
        (await reporter.GetAsync($"{s.Base}/reports")).StatusCode.Should().Be(HttpStatusCode.OK);
        var (outsider, _) = await _api.AddMemberAsync(s.Admin, (await _api.NewBusinessAsync(s.Admin)).Id, "CanViewReports");
        (await outsider.GetAsync($"{s.Base}/reports")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await fx.CreateClient().GetAsync($"{s.Base}/reports")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SqlFact]
    public async Task Consolidated_dashboard_is_admin_only_and_adds_up_across_businesses()
    {
        var s = await BuildAsync();
        var other = await BuildAsync();
        var c = await TestApi.Ok<ConsolidatedDashboardDto>(await s.Admin.GetAsync($"/api/v1/dashboard/consolidated?from={D()}&to={D()}"));

        var mine = c.Businesses.Single(b => b.BusinessId == s.BizId);
        (mine.NetSales, mine.TotalExpenses, mine.NetProfit, mine.AmountReceived, mine.OutstandingAmount).Should().Be((1500m, 690m, 930m, 1480m, 750m));
        c.Businesses.Should().Contain(b => b.BusinessId == other.BizId);
        c.Sales.NetSales.Should().Be(c.Businesses.Sum(b => b.NetSales));
        c.Expenses.TotalExpenses.Should().Be(c.Businesses.Sum(b => b.TotalExpenses));
        c.Profit.NetProfit.Should().Be(c.Businesses.Sum(b => b.NetProfit));
        c.BusinessCount.Should().Be(c.Businesses.Count);

        var (member, _) = await _api.AddMemberAsync(s.Admin, s.BizId, "CanViewReports", "CanViewSales");
        (await member.GetAsync("/api/v1/dashboard/consolidated")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------------------ adjustments
    [SqlFact]
    public async Task Adjustments_are_validated_admin_only_and_soft_deleted_ones_leave_the_profit()
    {
        var admin = await _api.AdminAsync();
        var biz = await _api.NewBusinessAsync(admin);
        var url = $"/api/v1/businesses/{biz.Id}/adjustments";
        object Adj(string type, string dir, decimal amount, string? date = null) => new { adjustmentDate = date ?? D(), type, direction = dir, amount, description = "x" };

        (await admin.PostAsJsonAsync(url, Adj("OtherIncome", "DecreasesProfit", 10m))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync(url, Adj("Loss", "IncreasesProfit", 10m))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync(url, Adj("Adjustment", "IncreasesProfit", 0m))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync(url, Adj("Gift", "IncreasesProfit", 5m))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync(url, new { adjustmentDate = D(), type = "Loss", direction = "DecreasesProfit", amount = 5m, description = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var gain = await TestApi.Ok<AdjustmentDto>(await admin.PostAsJsonAsync(url, Adj("Adjustment", "IncreasesProfit", 80m)), HttpStatusCode.Created);
        var loss = await TestApi.Ok<AdjustmentDto>(await admin.PostAsJsonAsync(url, Adj("Loss", "DecreasesProfit", 30m)), HttpStatusCode.Created);
        (gain.SignedAmount, loss.SignedAmount).Should().Be((80m, -30m));
        (await TestApi.Ok<DashboardDto>(await admin.GetAsync($"/api/v1/businesses/{biz.Id}/dashboard"))).Profit!.NetProfit.Should().Be(50m);

        var (member, _) = await _api.AddMemberAsync(admin, biz.Id, "CanViewReports", "CanViewSales");
        (await member.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PostAsJsonAsync(url, Adj("Loss", "DecreasesProfit", 1m))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.DeleteAsync($"{url}/{gain.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await admin.DeleteAsync($"{url}/{gain.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await TestApi.Ok<DashboardDto>(await admin.GetAsync($"/api/v1/businesses/{biz.Id}/dashboard"))).Profit!.NetProfit.Should().Be(-30m);
        (await TestApi.Ok<PagedResult<AdjustmentDto>>(await admin.GetAsync(url))).Items.Should().ContainSingle(a => a.Id == loss.Id);
        (await admin.DeleteAsync($"{url}/{gain.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = fx.Db.Create();
        (await db.FinancialAdjustments.IgnoreQueryFilters().SingleAsync(a => a.Id == gain.Id)).IsDeleted.Should().BeTrue("kept for audit");
    }

    // ------------------------------------------------------------------------------ audit log API
    [SqlFact]
    public async Task Audit_log_is_admin_only_filterable_and_shows_who_did_what()
    {
        var admin = await _api.AdminAsync();
        var biz = await _api.NewBusinessAsync(admin);

        var created = await TestApi.Ok<PagedResult<AuditLogDto>>(await admin.GetAsync($"/api/v1/audit-logs?entityName=Business&entityId={biz.Id}&action=Create"));
        var entry = created.Items.Should().ContainSingle().Subject;
        (entry.EntityName, entry.Action, entry.UserId, entry.BusinessId).Should().Be(("Business", "Create", fx.Db.AdminId, biz.Id));
        entry.UserName.Should().NotBeNullOrEmpty();
        entry.NewValues.Should().Contain(biz.Name);

        var byBusiness = await TestApi.Ok<PagedResult<AuditLogDto>>(await admin.GetAsync($"/api/v1/audit-logs?businessId={biz.Id}&pageSize=100"));
        byBusiness.Items.Should().OnlyContain(a => a.BusinessId == biz.Id);
        byBusiness.Items.Select(a => a.EntityName).Should().Contain(["Business", "BusinessMember"]);
        byBusiness.Items.Select(a => a.OccurredAt).Should().BeInDescendingOrder();

        (await TestApi.Ok<PagedResult<AuditLogDto>>(await admin.GetAsync($"/api/v1/audit-logs?businessId={biz.Id}&from={D(-2)}"))).Items.Should().BeEmpty();
        (await admin.GetAsync("/api/v1/audit-logs?action=Explode")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.GetAsync("/api/v1/audit-logs?pageSize=1000")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (member, _) = await _api.AddMemberAsync(admin, biz.Id, "CanViewReports");
        (await member.GetAsync("/api/v1/audit-logs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await fx.CreateClient().GetAsync("/api/v1/audit-logs")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
