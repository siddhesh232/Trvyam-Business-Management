using System.Net;
using System.Net.Http.Json;
using BusinessManagement.Application.Payments;
using BusinessManagement.Application.Sales;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Tests.Database;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Tests.Api;

[Collection("Api")]
public sealed class PaymentApiTests(ApiFixture fx)
{
    private readonly TestApi _api = new(fx);
    private static string Today() => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
    private static readonly string[] Perms = ["CanViewSales", "CanAddSales", "CanEditOwnSales", "CanDeleteOwnSales", "CanViewCustomers",
        "CanRecordPayments", "CanViewPayments", "CanEditOwnPayments", "CanDeleteOwnPayments"];

    private sealed record Ctx(HttpClient Admin, long BizId, HttpClient Member, HttpClient Other, SalesOrderDto Sale, string SaleUrl, string PayUrl);

    /// <summary>A sale of exactly 1,000.00 + 18% = 1,180.00, and two members with full payment permissions.</summary>
    private async Task<Ctx> SetupAsync()
    {
        var admin = await _api.AdminAsync();
        var biz = await _api.NewBusinessAsync(admin);
        var product = await _api.NewProductAsync(admin, biz.Id, 500m, 18m);
        var customer = await _api.NewCustomerAsync(admin, biz.Id);
        var (member, _) = await _api.AddMemberAsync(admin, biz.Id, Perms);
        var (other, _) = await _api.AddMemberAsync(admin, biz.Id, Perms);
        var saleUrl = $"/api/v1/businesses/{biz.Id}/sales";
        var sale = await TestApi.Ok<SalesOrderDto>(await member.PostAsJsonAsync(saleUrl, new
        { customerId = customer.Id, orderDate = Today(), items = new[] { new { productId = product.Id, quantity = 2m, itemDiscount = 0m } }, orderDiscount = 0m }), HttpStatusCode.Created);
        sale.FinalAmount.Should().Be(1180m);
        return new Ctx(admin, biz.Id, member, other, sale, saleUrl, $"{saleUrl}/{sale.Id}/payments");
    }

    private static object Pay(decimal amount, string mode = "Upi", string? reference = null, string? date = null) =>
        new { paymentDate = date ?? Today(), amount, paymentMode = mode, transactionReference = reference, notes = (string?)null };

    [SqlFact]
    public async Task A_sale_can_receive_many_payments_and_status_and_outstanding_are_calculated()
    {
        var c = await SetupAsync();
        var h0 = await TestApi.Ok<PaymentHistoryDto>(await c.Member.GetAsync(c.PayUrl));
        (h0.PaymentStatus, h0.TotalReceived, h0.Outstanding).Should().Be(("Unpaid", 0m, 1180m));

        var h1 = await TestApi.Ok<PaymentHistoryDto>(await c.Member.PostAsJsonAsync(c.PayUrl, Pay(400m, "Cash")), HttpStatusCode.Created);
        (h1.PaymentStatus, h1.TotalReceived, h1.Outstanding).Should().Be(("PartiallyPaid", 400m, 780m));
        var h2 = await TestApi.Ok<PaymentHistoryDto>(await c.Other.PostAsJsonAsync(c.PayUrl, Pay(380m, "BankTransfer", "UTR12345")), HttpStatusCode.Created);
        (h2.TotalReceived, h2.Outstanding).Should().Be((780m, 400m));
        var h3 = await TestApi.Ok<PaymentHistoryDto>(await c.Member.PostAsJsonAsync(c.PayUrl, Pay(400m)), HttpStatusCode.Created);
        (h3.PaymentStatus, h3.Outstanding).Should().Be(("Paid", 0m));

        h3.Payments.Should().HaveCount(3);
        h3.Payments.Select(p => p.PaymentMode).Should().Equal("Cash", "BankTransfer", "Upi");
        h3.Payments[1].TransactionReference.Should().Be("UTR12345");

        // The sale list reflects the calculated status.
        var list = await TestApi.Ok<BusinessManagement.Application.Common.PagedResult<SalesListItemDto>>(await c.Member.GetAsync($"{c.SaleUrl}?paymentStatus=Paid"));
        list.Items.Should().ContainSingle(i => i.Id == c.Sale.Id);
    }

    [SqlFact]
    public async Task Overpayment_is_rejected_and_nothing_is_recorded()
    {
        var c = await SetupAsync();
        await c.Member.PostAsJsonAsync(c.PayUrl, Pay(1000m));
        var res = await c.Member.PostAsJsonAsync(c.PayUrl, Pay(180.01m));
        res.StatusCode.Should().Be((HttpStatusCode)422);
        (await res.Content.ReadAsStringAsync()).Should().Contain("overpayment");

        var h = await TestApi.Ok<PaymentHistoryDto>(await c.Member.GetAsync(c.PayUrl));
        (h.Payments.Count, h.TotalReceived).Should().Be((1, 1000m));
        (await TestApi.Ok<PaymentHistoryDto>(await c.Member.PostAsJsonAsync(c.PayUrl, Pay(180m)), HttpStatusCode.Created)).PaymentStatus.Should().Be("Paid");
    }

    [SqlFact]
    public async Task Payment_input_is_validated()
    {
        var c = await SetupAsync();
        (await c.Member.PostAsJsonAsync(c.PayUrl, Pay(0m))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.Member.PostAsJsonAsync(c.PayUrl, Pay(-5m))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.Member.PostAsJsonAsync(c.PayUrl, Pay(10.005m))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.Member.PostAsJsonAsync(c.PayUrl, Pay(10m, "Bitcoin"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.Member.PostAsJsonAsync(c.PayUrl, Pay(10m, "3"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var before = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)).ToString("yyyy-MM-dd");
        (await c.Member.PostAsJsonAsync(c.PayUrl, Pay(10m, date: before))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "cannot be paid before the sale date");
    }

    [SqlFact]
    public async Task Reversal_keeps_the_record_removes_it_from_the_total_and_cannot_repeat()
    {
        var c = await SetupAsync();
        var h = await TestApi.Ok<PaymentHistoryDto>(await c.Member.PostAsJsonAsync(c.PayUrl, Pay(1180m)), HttpStatusCode.Created);
        h.PaymentStatus.Should().Be("Paid");
        var id = h.Payments.Single().Id;

        (await c.Member.PostAsJsonAsync($"{c.PayUrl}/{id}/reverse", new { reason = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var r = await TestApi.Ok<PaymentHistoryDto>(await c.Member.PostAsJsonAsync($"{c.PayUrl}/{id}/reverse", new { reason = "Cheque bounced" }));
        (r.PaymentStatus, r.TotalReceived, r.Outstanding).Should().Be(("Unpaid", 0m, 1180m));
        var row = r.Payments.Single();
        (row.Status, row.ReversalReason, row.CanEdit, row.CanReverse).Should().Be(("Reversed", "Cheque bounced", false, false));
        row.ReversedByName.Should().NotBeNullOrEmpty();

        (await c.Member.PostAsJsonAsync($"{c.PayUrl}/{id}/reverse", new { reason = "again" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await c.Member.PutAsJsonAsync($"{c.PayUrl}/{id}", Pay(10m))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        // The customer can pay again after a reversal.
        (await TestApi.Ok<PaymentHistoryDto>(await c.Member.PostAsJsonAsync(c.PayUrl, Pay(1180m)), HttpStatusCode.Created)).PaymentStatus.Should().Be("Paid");

        await using var db = fx.Db.Create();
        (await db.Payments.CountAsync(p => p.SalesOrderId == c.Sale.Id)).Should().Be(2, "reversed payments are never removed");
        (await db.AuditLogs.AnyAsync(a => a.EntityName == "Payment" && a.EntityId == id && a.Action == AuditAction.Update && a.NewValues!.Contains("Reversed"))).Should().BeTrue();
    }

    [SqlFact]
    public async Task Members_edit_and_reverse_only_payments_they_recorded_and_the_admin_can_manage_all()
    {
        var c = await SetupAsync();
        var h = await TestApi.Ok<PaymentHistoryDto>(await c.Member.PostAsJsonAsync(c.PayUrl, Pay(300m)), HttpStatusCode.Created);
        var id = h.Payments.Single().Id;

        (await c.Other.PutAsJsonAsync($"{c.PayUrl}/{id}", Pay(100m))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.Other.PostAsJsonAsync($"{c.PayUrl}/{id}/reverse", new { reason = "not mine" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var edited = await TestApi.Ok<PaymentHistoryDto>(await c.Member.PutAsJsonAsync($"{c.PayUrl}/{id}", Pay(500m, "Cash", "R-1")));
        (edited.TotalReceived, edited.Payments[0].PaymentMode).Should().Be((500m, "Cash"));
        (await c.Member.PutAsJsonAsync($"{c.PayUrl}/{id}", Pay(1181m))).StatusCode.Should().Be((HttpStatusCode)422, "edit may not push the total over the sale amount");

        (await c.Admin.PutAsJsonAsync($"{c.PayUrl}/{id}", Pay(600m))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.Admin.PostAsJsonAsync($"{c.PayUrl}/{id}/reverse", new { reason = "admin correction" })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SqlFact]
    public async Task Payment_permissions_and_isolation_are_enforced()
    {
        var c = await SetupAsync();
        var (recorderOnly, _) = await _api.AddMemberAsync(c.Admin, c.BizId, "CanRecordPayments");
        var (viewerOnly, _) = await _api.AddMemberAsync(c.Admin, c.BizId, "CanViewPayments");
        var (outsider, _) = await _api.AddMemberAsync(c.Admin, (await _api.NewBusinessAsync(c.Admin)).Id, Perms);

        (await recorderOnly.GetAsync(c.PayUrl)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewerOnly.PostAsJsonAsync(c.PayUrl, Pay(10m))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await recorderOnly.PostAsJsonAsync(c.PayUrl, Pay(10m))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await viewerOnly.GetAsync(c.PayUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await outsider.GetAsync(c.PayUrl)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.Member.GetAsync($"/api/v1/businesses/{c.BizId}/sales/999999/payments")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SqlFact]
    public async Task Payments_constrain_how_the_sale_can_be_changed()
    {
        var c = await SetupAsync();
        var product = c.Sale.Items.Single();
        var h = await TestApi.Ok<PaymentHistoryDto>(await c.Member.PostAsJsonAsync(c.PayUrl, Pay(900m)), HttpStatusCode.Created);

        // Edit that would drop the total below what was received => 409.
        var smaller = new { orderDate = Today(), items = new[] { new { productId = product.ProductId, quantity = 1m, itemDiscount = 0m } }, orderDiscount = 0m };
        (await c.Member.PutAsJsonAsync($"{c.SaleUrl}/{c.Sale.Id}", smaller)).StatusCode.Should().Be(HttpStatusCode.Conflict);   // 590 < 900
        // A sale with payments cannot be deleted until they are reversed.
        (await c.Member.DeleteAsync($"{c.SaleUrl}/{c.Sale.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        await c.Member.PostAsJsonAsync($"{c.PayUrl}/{h.Payments.Single().Id}/reverse", new { reason = "refund" });
        (await c.Member.DeleteAsync($"{c.SaleUrl}/{c.Sale.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.Member.PostAsJsonAsync(c.PayUrl, Pay(10m))).StatusCode.Should().Be(HttpStatusCode.NotFound, "payments cannot be added to a deleted sale");
    }

    [SqlFact]
    public async Task Concurrent_payments_can_never_overpay_a_sale()
    {
        var c = await SetupAsync();   // 1,180.00 due; 5 simultaneous payments of 300.00 => at most 3 can fit
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => c.Member.PostAsJsonAsync(c.PayUrl, Pay(300m))));
        var ok = results.Count(r => r.StatusCode == HttpStatusCode.Created);
        results.Where(r => r.StatusCode != HttpStatusCode.Created).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict || (int)r.StatusCode == 422);
        ok.Should().BeInRange(1, 3);

        var h = await TestApi.Ok<PaymentHistoryDto>(await c.Member.GetAsync(c.PayUrl));
        h.TotalReceived.Should().Be(ok * 300m);
        h.TotalReceived.Should().BeLessThanOrEqualTo(1180m);
        h.Payments.Count.Should().Be(ok);
    }
}
