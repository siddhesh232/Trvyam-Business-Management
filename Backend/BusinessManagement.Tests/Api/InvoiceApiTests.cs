using System.Net;
using System.Net.Http.Json;
using BusinessManagement.Application.Businesses;
using BusinessManagement.Application.Common;
using BusinessManagement.Application.Customers;
using BusinessManagement.Application.Invoices;
using BusinessManagement.Application.Products;
using BusinessManagement.Application.Sales;
using BusinessManagement.Domain.Billing;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Tests.Database;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Tests.Api;

[Collection("Api")]
public sealed class InvoiceApiTests(ApiFixture fx)
{
    private readonly TestApi _api = new(fx);
    private static string Today() => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
    private static string ShortFy()
    {
        var fy = InvoiceRules.FinancialYear(InvoiceRules.TodayIst(DateTime.UtcNow));
        return $"{fy[2..4]}-{fy[5..]}";
    }
    private static readonly string[] Perms = ["CanViewSales", "CanAddSales", "CanEditOwnSales", "CanDeleteOwnSales", "CanViewCustomers",
        "CanGenerateInvoices", "CanViewInvoices", "CanRecordPayments", "CanViewPayments"];

    private sealed record Ctx(HttpClient Admin, HttpClient Member, long BizId, ProductDto Product, CustomerDto Customer, string Base);

    private async Task<Ctx> SetupAsync(decimal price = 500m, decimal gst = 18m, string? customerGstin = null, bool ready = true)
    {
        var admin = await _api.AdminAsync();
        var biz = await _api.NewBusinessAsync(admin);
        if (ready) await _api.MakeInvoiceReadyAsync(admin, biz);
        var product = await _api.NewProductAsync(admin, biz.Id, price, gst, "Widget");
        var customer = await _api.NewCustomerAsync(admin, biz.Id, customerGstin);
        var (member, _) = await _api.AddMemberAsync(admin, biz.Id, Perms);
        return new Ctx(admin, member, biz.Id, product, customer, $"/api/v1/businesses/{biz.Id}");
    }

    private async Task<SalesOrderDto> SaleAsync(Ctx c, decimal qty = 2m, CustomerDto? customer = null, ProductDto? product = null) =>
        await TestApi.Ok<SalesOrderDto>(await c.Member.PostAsJsonAsync($"{c.Base}/sales", new
        {
            customerId = (customer ?? c.Customer).Id, orderDate = Today(),
            items = new[] { new { productId = (product ?? c.Product).Id, quantity = qty, itemDiscount = 0m } }, orderDiscount = 0m
        }), HttpStatusCode.Created);

    private async Task<InvoiceDto> IssueAsync(Ctx c, SalesOrderDto sale) =>
        await TestApi.Ok<InvoiceDto>(await c.Member.PostAsync($"{c.Base}/sales/{sale.Id}/invoice", null), HttpStatusCode.Created);

    [SqlFact]
    public async Task Intra_state_sale_gets_cgst_and_sgst_and_a_formatted_sequential_number()
    {
        var c = await SetupAsync();
        var inv = await IssueAsync(c, await SaleAsync(c));
        inv.InvoiceNumber.Should().Be($"INV/{ShortFy()}/00001");
        inv.InvoiceNumber.Length.Should().BeLessThanOrEqualTo(16);
        (inv.IsInterState, inv.TaxableAmount, inv.Cgst, inv.Sgst, inv.Igst, inv.GrandTotal).Should().Be((false, 1000m, 90m, 90m, 0m, 1180m));
        inv.Status.Should().Be("Issued");
        inv.Snapshot.Lines.Single().Should().BeEquivalentTo(new { Description = "Widget", Quantity = 2m, Taxable = 1000m, Cgst = 90m, Sgst = 90m, Igst = 0m, Total = 1180m });
        inv.Snapshot.Seller.Gstin.Should().NotBeNullOrEmpty();
    }

    [SqlFact]
    public async Task Inter_state_sale_gets_igst_only()
    {
        var c = await SetupAsync(customerGstin: TestApi.NewGstin("29"));
        var sale = await SaleAsync(c);
        sale.PlaceOfSupplyState.Should().Be("29");
        var inv = await IssueAsync(c, sale);
        (inv.IsInterState, inv.Cgst, inv.Sgst, inv.Igst, inv.GrandTotal).Should().Be((true, 0m, 0m, 180m, 1180m));
        inv.Snapshot.Buyer.Gstin.Should().Be(c.Customer.Gstin);
    }

    [SqlFact]
    public async Task An_odd_paisa_of_gst_is_split_so_that_cgst_plus_sgst_equals_the_gst()
    {
        var c = await SetupAsync(price: 10.10m, gst: 5m);          // GST 0.505 -> 0.51
        var inv = await IssueAsync(c, await SaleAsync(c, qty: 1m));
        (inv.Cgst, inv.Sgst, inv.GrandTotal).Should().Be((0.25m, 0.26m, 10.61m));
        (inv.TaxableAmount + inv.Cgst + inv.Sgst).Should().Be(inv.GrandTotal);
    }

    [SqlFact]
    public async Task Numbers_are_sequential_per_business_and_independent_across_businesses()
    {
        var c = await SetupAsync();
        var n1 = (await IssueAsync(c, await SaleAsync(c))).InvoiceNumber;
        var n2 = (await IssueAsync(c, await SaleAsync(c))).InvoiceNumber;
        var n3 = (await IssueAsync(c, await SaleAsync(c))).InvoiceNumber;
        new[] { n1, n2, n3 }.Should().Equal($"INV/{ShortFy()}/00001", $"INV/{ShortFy()}/00002", $"INV/{ShortFy()}/00003");

        var other = await SetupAsync();
        (await IssueAsync(other, await SaleAsync(other))).InvoiceNumber.Should().Be($"INV/{ShortFy()}/00001", "each business has its own series");
    }

    [SqlFact]
    public async Task Concurrent_issuing_never_duplicates_or_skips_a_number()
    {
        var c = await SetupAsync();
        var sales = new List<SalesOrderDto>();
        for (var i = 0; i < 4; i++) sales.Add(await SaleAsync(c));

        var responses = await Task.WhenAll(sales.Select(s => c.Member.PostAsync($"{c.Base}/sales/{s.Id}/invoice", null)));
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created, "issuers are serialised by the sequence row lock");
        var numbers = (await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<InvoiceDto>()))).Select(i => i!.InvoiceNumber).OrderBy(n => n).ToArray();
        numbers.Should().Equal(Enumerable.Range(1, 4).Select(i => $"INV/{ShortFy()}/{i:D5}"));
    }

    [SqlFact]
    public async Task Business_must_be_invoice_ready_and_each_sale_gets_one_active_invoice()
    {
        var notReady = await SetupAsync(ready: false);
        var s0 = await SaleAsync(notReady);
        var res = await notReady.Member.PostAsync($"{notReady.Base}/sales/{s0.Id}/invoice", null);
        res.StatusCode.Should().Be((HttpStatusCode)422);
        (await res.Content.ReadAsStringAsync()).Should().Contain("business_incomplete");

        var c = await SetupAsync();
        var sale = await SaleAsync(c);
        await IssueAsync(c, sale);
        (await c.Member.PostAsync($"{c.Base}/sales/{sale.Id}/invoice", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await c.Member.PostAsync($"{c.Base}/sales/999999/invoice", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SqlFact]
    public async Task Issued_invoice_is_a_frozen_snapshot_and_locks_the_sale()
    {
        var c = await SetupAsync();
        var sale = await SaleAsync(c);
        var inv = await IssueAsync(c, sale);

        // Change the business, the customer and the catalogue after issuing.
        await c.Admin.PutAsJsonAsync($"{c.Base}", new { });   // 405 expected route does not exist: harmless
        await TestApi.Ok<BusinessDetailDto>(await c.Admin.PutAsJsonAsync($"/api/v1/businesses/{c.BizId}", new { name = "Renamed Business " + TestApi.Tag(), gstNumber = TestApi.NewGstin("27"), addressLine = "New address", isActive = true }));
        await TestApi.Ok<CustomerDto>(await c.Admin.PutAsJsonAsync($"/api/v1/customers/{c.Customer.Id}", new { name = "Renamed Customer" }));
        var again = await TestApi.Ok<InvoiceDto>(await c.Member.GetAsync($"{c.Base}/invoices/{inv.Id}"));
        again.Snapshot.Seller.Name.Should().Be(inv.Snapshot.Seller.Name);
        again.Snapshot.Seller.Address.Should().Be("12 Market Road, Pune");
        again.Snapshot.Buyer.Name.Should().Be(inv.Snapshot.Buyer.Name).And.NotBe("Renamed Customer");

        // The sale is locked while the invoice is active.
        var edit = new { orderDate = Today(), items = new[] { new { productId = c.Product.Id, quantity = 5m, itemDiscount = 0m } }, orderDiscount = 0m };
        (await c.Member.PutAsJsonAsync($"{c.Base}/sales/{sale.Id}", edit)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await c.Member.DeleteAsync($"{c.Base}/sales/{sale.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TestApi.Ok<SalesOrderDto>(await c.Member.GetAsync($"{c.Base}/sales/{sale.Id}"))).CanEdit.Should().BeFalse();
    }

    [SqlFact]
    public async Task Recording_a_payment_never_creates_an_invoice()
    {
        var c = await SetupAsync();
        var sale = await SaleAsync(c);
        var pay = await c.Member.PostAsJsonAsync($"{c.Base}/sales/{sale.Id}/payments", new { paymentDate = Today(), amount = 1180m, paymentMode = "Cash" });
        pay.StatusCode.Should().Be(HttpStatusCode.Created);
        var list = await TestApi.Ok<PagedResult<InvoiceListItemDto>>(await c.Member.GetAsync($"{c.Base}/invoices"));
        list.Items.Should().BeEmpty();
        await IssueAsync(c, sale);    // invoicing is a separate, explicit action
        (await TestApi.Ok<PagedResult<InvoiceListItemDto>>(await c.Member.GetAsync($"{c.Base}/invoices"))).Total.Should().Be(1);
    }

    [SqlFact]
    public async Task Admin_cancels_an_invoice_the_number_stays_and_the_sale_can_be_reinvoiced_under_a_new_number()
    {
        var c = await SetupAsync();
        var sale = await SaleAsync(c);
        var inv = await IssueAsync(c, sale);

        (await c.Member.PostAsJsonAsync($"{c.Base}/invoices/{inv.Id}/cancel", new { reason = "mistake" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.Admin.PostAsJsonAsync($"{c.Base}/invoices/{inv.Id}/cancel", new { reason = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var cancelled = await TestApi.Ok<InvoiceDto>(await c.Admin.PostAsJsonAsync($"{c.Base}/invoices/{inv.Id}/cancel", new { reason = "Wrong customer" }));
        (cancelled.Status, cancelled.CancelReason, cancelled.InvoiceNumber).Should().Be(("Cancelled", "Wrong customer", inv.InvoiceNumber));
        (await c.Admin.PostAsJsonAsync($"{c.Base}/invoices/{inv.Id}/cancel", new { reason = "twice" })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var second = await IssueAsync(c, sale);
        second.InvoiceNumber.Should().Be($"INV/{ShortFy()}/00002", "numbers are never reused");
        var cancelledOnly = await TestApi.Ok<PagedResult<InvoiceListItemDto>>(await c.Member.GetAsync($"{c.Base}/invoices?status=Cancelled"));
        cancelledOnly.Items.Should().ContainSingle(i => i.InvoiceNumber == inv.InvoiceNumber);
        (await c.Member.GetAsync($"{c.Base}/invoices?status=Bogus")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var db = fx.Db.Create();
        (await db.GstInvoices.CountAsync(i => i.SalesOrderId == sale.Id)).Should().Be(2, "the cancelled invoice is retained");
        (await db.AuditLogs.AnyAsync(a => a.EntityName == "GstInvoice" && a.EntityId == inv.Id && a.Action == AuditAction.Update)).Should().BeTrue();
    }

    [SqlFact]
    public async Task A_cancelled_invoice_frees_the_sale_for_editing()
    {
        var c = await SetupAsync();
        var sale = await SaleAsync(c);
        var inv = await IssueAsync(c, sale);
        await c.Admin.PostAsJsonAsync($"{c.Base}/invoices/{inv.Id}/cancel", new { reason = "fix quantity" });
        var edit = new { orderDate = Today(), items = new[] { new { productId = c.Product.Id, quantity = 3m, itemDiscount = 0m } }, orderDiscount = 0m };
        var updated = await TestApi.Ok<SalesOrderDto>(await c.Member.PutAsJsonAsync($"{c.Base}/sales/{sale.Id}", edit));
        updated.FinalAmount.Should().Be(1770m);
        (await IssueAsync(c, updated)).GrandTotal.Should().Be(1770m);
    }

    [SqlFact]
    public async Task Invoice_permissions_and_isolation_are_enforced()
    {
        var c = await SetupAsync();
        var sale = await SaleAsync(c);
        var (viewer, _) = await _api.AddMemberAsync(c.Admin, c.BizId, "CanViewInvoices");
        var (generator, _) = await _api.AddMemberAsync(c.Admin, c.BizId, "CanGenerateInvoices");
        var (outsider, _) = await _api.AddMemberAsync(c.Admin, (await _api.NewBusinessAsync(c.Admin)).Id, Perms);

        (await viewer.PostAsync($"{c.Base}/sales/{sale.Id}/invoice", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var inv = await TestApi.Ok<InvoiceDto>(await generator.PostAsync($"{c.Base}/sales/{sale.Id}/invoice", null), HttpStatusCode.Created);
        (await generator.GetAsync($"{c.Base}/invoices/{inv.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "generating does not imply viewing");
        (await viewer.GetAsync($"{c.Base}/invoices/{inv.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.GetAsync($"{c.Base}/invoices/{inv.Id}/pdf")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await outsider.GetAsync($"{c.Base}/invoices/{inv.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await fx.CreateClient().GetAsync($"{c.Base}/invoices/{inv.Id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SqlFact]
    public async Task Invoice_downloads_as_a_pdf_including_cancelled_ones()
    {
        var c = await SetupAsync();
        var inv = await IssueAsync(c, await SaleAsync(c));
        var res = await c.Member.GetAsync($"{c.Base}/invoices/{inv.Id}/pdf");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await res.Content.ReadAsByteArrayAsync();
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        bytes.Length.Should().BeGreaterThan(1500);
        res.Content.Headers.ContentDisposition!.FileNameStar.Should().Contain("INV-");

        await c.Admin.PostAsJsonAsync($"{c.Base}/invoices/{inv.Id}/cancel", new { reason = "test" });
        (await c.Member.GetAsync($"{c.Base}/invoices/{inv.Id}/pdf")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SqlFact]
    public async Task Invoice_rows_cannot_be_altered_or_deleted_at_database_level()
    {
        var c = await SetupAsync();
        var inv = await IssueAsync(c, await SaleAsync(c));
        await using var db = fx.Db.Create();
        var row = await db.GstInvoices.SingleAsync(i => i.Id == inv.Id);
        row.GrandTotal = 1m; row.TaxableAmount = 1m; row.Cgst = 0; row.Sgst = 0;
        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
