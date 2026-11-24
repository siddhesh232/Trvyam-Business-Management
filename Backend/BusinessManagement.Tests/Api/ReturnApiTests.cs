using System.Net;
using System.Net.Http.Json;
using BusinessManagement.Application.Common;
using BusinessManagement.Application.Customers;
using BusinessManagement.Application.Invoices;
using BusinessManagement.Application.Payments;
using BusinessManagement.Application.Products;
using BusinessManagement.Application.Returns;
using BusinessManagement.Application.Sales;
using BusinessManagement.Domain.Billing;
using BusinessManagement.Tests.Database;
using FluentAssertions;

namespace BusinessManagement.Tests.Api;

[Collection("Api")]
public sealed class ReturnApiTests(ApiFixture fx)
{
    private readonly TestApi _api = new(fx);
    private static string Today() => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
    private static string ShortFy() { var fy = InvoiceRules.FinancialYear(InvoiceRules.TodayIst(DateTime.UtcNow)); return $"{fy[2..4]}-{fy[5..]}"; }
    private static readonly string[] Perms = ["CanViewSales", "CanAddSales", "CanEditOwnSales", "CanDeleteOwnSales", "CanViewCustomers", "CanGenerateInvoices",
        "CanViewInvoices", "CanRecordPayments", "CanViewPayments"];

    private sealed record Ctx(HttpClient Admin, HttpClient Member, long BizId, ProductDto Product, CustomerDto Customer, string Base);

    private async Task<Ctx> SetupAsync(decimal price = 500m, decimal gst = 18m)
    {
        var admin = await _api.AdminAsync();
        var biz = await _api.NewBusinessAsync(admin);
        await _api.MakeInvoiceReadyAsync(admin, biz);
        var product = await _api.NewProductAsync(admin, biz.Id, price, gst, "Widget");
        var customer = await _api.NewCustomerAsync(admin, biz.Id);
        var (member, _) = await _api.AddMemberAsync(admin, biz.Id, Perms);
        return new Ctx(admin, member, biz.Id, product, customer, $"/api/v1/businesses/{biz.Id}");
    }

    private async Task<SalesOrderDto> SaleAsync(Ctx c, decimal qty, decimal itemDiscount = 0m) =>
        await TestApi.Ok<SalesOrderDto>(await c.Member.PostAsJsonAsync($"{c.Base}/sales", new
        { customerId = c.Customer.Id, orderDate = Today(), items = new[] { new { productId = c.Product.Id, quantity = qty, itemDiscount } }, orderDiscount = 0m }), HttpStatusCode.Created);

    private static object Return(long itemId, decimal qty, string? date = null) =>
        new { returnDate = date ?? Today(), reason = "Damaged", items = new[] { new { salesOrderItemId = itemId, quantity = qty } } };

    [SqlFact]
    public async Task Return_on_an_invoiced_sale_issues_a_credit_note_in_its_own_series()
    {
        var c = await SetupAsync();
        var sale = await SaleAsync(c, 3m);                                     // 1500 + 270 = 1770
        var inv = await TestApi.Ok<InvoiceDto>(await c.Member.PostAsync($"{c.Base}/sales/{sale.Id}/invoice", null), HttpStatusCode.Created);

        var ret = await TestApi.Ok<SalesReturnDto>(await c.Admin.PostAsJsonAsync($"{c.Base}/sales/{sale.Id}/returns", Return(sale.Items[0].Id, 1m)), HttpStatusCode.Created);
        (ret.TaxableAmount, ret.GstAmount, ret.TotalAmount, ret.Status).Should().Be((500m, 90m, 590m, "Accepted"));
        ret.CreditNoteNumber.Should().Be($"CN/{ShortFy()}/00001", "credit notes have their own series, separate from INV");
        ret.Items.Single().ProductName.Should().Be("Widget");

        var note = await TestApi.Ok<InvoiceDto>(await c.Member.GetAsync($"{c.Base}/invoices/{ret.CreditNoteId}"));
        (note.DocType, note.OriginalInvoiceId, note.GrandTotal, note.Cgst, note.Sgst).Should().Be(("CreditNote", inv.Id, 590m, 45m, 45m));
        note.Snapshot.DocTitle.Should().Be("CREDIT NOTE");
        note.Snapshot.OriginalInvoiceNumber.Should().Be(inv.InvoiceNumber);
        (await c.Member.GetAsync($"{c.Base}/invoices/{ret.CreditNoteId}/pdf")).StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await TestApi.Ok<SalesReturnDto>(await c.Admin.PostAsJsonAsync($"{c.Base}/sales/{sale.Id}/returns", Return(sale.Items[0].Id, 1m)), HttpStatusCode.Created);
        second.CreditNoteNumber.Should().Be($"CN/{ShortFy()}/00002");
        (await TestApi.Ok<InvoiceDto>(await c.Member.GetAsync($"{c.Base}/invoices/{inv.Id}"))).InvoiceNumber.Should().Be($"INV/{ShortFy()}/00001");

        var list = await TestApi.Ok<PagedResult<InvoiceListItemDto>>(await c.Member.GetAsync($"{c.Base}/invoices?sort=number"));
        list.Items.Select(i => i.DocType).Should().BeEquivalentTo("TaxInvoice", "CreditNote", "CreditNote");
    }

    [SqlFact]
    public async Task Returns_without_an_invoice_create_no_credit_note()
    {
        var c = await SetupAsync();
        var sale = await SaleAsync(c, 2m);
        var ret = await TestApi.Ok<SalesReturnDto>(await c.Admin.PostAsJsonAsync($"{c.Base}/sales/{sale.Id}/returns", Return(sale.Items[0].Id, 1m)), HttpStatusCode.Created);
        (ret.CreditNoteId, ret.CreditNoteNumber).Should().Be(((long?)null, (string?)null));
        (await TestApi.Ok<IReadOnlyList<SalesReturnDto>>(await c.Member.GetAsync($"{c.Base}/sales/{sale.Id}/returns"))).Should().ContainSingle();
    }

    [SqlFact]
    public async Task Returns_are_pro_rated_and_the_last_return_takes_the_exact_remainder()
    {
        var c = await SetupAsync(price: 10m, gst: 18m);
        var sale = await SaleAsync(c, 3m, itemDiscount: 0.01m);                 // taxable 29.99, GST 5.40
        (sale.TaxableAmount, sale.GstAmount).Should().Be((29.99m, 5.40m));
        var id = sale.Items[0].Id;

        var r1 = await TestApi.Ok<SalesReturnDto>(await c.Admin.PostAsJsonAsync($"{c.Base}/sales/{sale.Id}/returns", Return(id, 1m)), HttpStatusCode.Created);
        var r2 = await TestApi.Ok<SalesReturnDto>(await c.Admin.PostAsJsonAsync($"{c.Base}/sales/{sale.Id}/returns", Return(id, 1m)), HttpStatusCode.Created);
        var r3 = await TestApi.Ok<SalesReturnDto>(await c.Admin.PostAsJsonAsync($"{c.Base}/sales/{sale.Id}/returns", Return(id, 1m)), HttpStatusCode.Created);
        new[] { r1, r2, r3 }.Sum(r => r.TaxableAmount).Should().Be(29.99m, "returns add up to exactly the sold value, never a paisa more");
        new[] { r1, r2, r3 }.Sum(r => r.GstAmount).Should().Be(5.40m);
        (await c.Admin.PostAsJsonAsync($"{c.Base}/sales/{sale.Id}/returns", Return(id, 1m))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "nothing left to return");
    }

    [SqlFact]
    public async Task Return_input_is_validated_and_admin_only()
    {
        var c = await SetupAsync();
        var sale = await SaleAsync(c, 2m);
        var other = await SaleAsync(c, 1m);
        var url = $"{c.Base}/sales/{sale.Id}/returns";

        (await c.Member.PostAsJsonAsync(url, Return(sale.Items[0].Id, 1m))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.Admin.PostAsJsonAsync(url, Return(sale.Items[0].Id, 3m))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.Admin.PostAsJsonAsync(url, Return(sale.Items[0].Id, 0m))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.Admin.PostAsJsonAsync(url, Return(other.Items[0].Id, 1m))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "line belongs to another sale");
        (await c.Admin.PostAsJsonAsync(url, new { returnDate = Today(), items = new object[] { new { salesOrderItemId = sale.Items[0].Id, quantity = 1m }, new { salesOrderItemId = sale.Items[0].Id, quantity = 1m } } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "duplicate line");
        (await c.Admin.PostAsJsonAsync(url, new { returnDate = Today(), items = Array.Empty<object>() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var before = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)).ToString("yyyy-MM-dd");
        (await c.Admin.PostAsJsonAsync(url, Return(sale.Items[0].Id, 1m, before))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "before the sale date");
        (await c.Admin.PostAsJsonAsync($"{c.Base}/sales/999999/returns", Return(1, 1m))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SqlFact]
    public async Task Sales_with_returns_are_locked_and_invoices_with_credit_notes_cannot_be_cancelled()
    {
        var c = await SetupAsync();
        var sale = await SaleAsync(c, 2m);
        await c.Admin.PostAsJsonAsync($"{c.Base}/sales/{sale.Id}/returns", Return(sale.Items[0].Id, 1m));
        var edit = new { orderDate = Today(), items = new[] { new { productId = c.Product.Id, quantity = 5m, itemDiscount = 0m } }, orderDiscount = 0m };
        (await c.Member.PutAsJsonAsync($"{c.Base}/sales/{sale.Id}", edit)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await c.Member.DeleteAsync($"{c.Base}/sales/{sale.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var invoiced = await SaleAsync(c, 2m);
        var inv = await TestApi.Ok<InvoiceDto>(await c.Member.PostAsync($"{c.Base}/sales/{invoiced.Id}/invoice", null), HttpStatusCode.Created);
        await c.Admin.PostAsJsonAsync($"{c.Base}/sales/{invoiced.Id}/returns", Return(invoiced.Items[0].Id, 1m));
        (await c.Admin.PostAsJsonAsync($"{c.Base}/invoices/{inv.Id}/cancel", new { reason = "try" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [SqlFact]
    public async Task Returns_reduce_what_is_due_and_can_create_a_refund_owed()
    {
        var c = await SetupAsync();
        var sale = await SaleAsync(c, 2m);                                      // 1000 + 180 = 1180
        var pay = $"{c.Base}/sales/{sale.Id}/payments";
        var paid = await TestApi.Ok<PaymentHistoryDto>(await c.Member.PostAsJsonAsync(pay, new { paymentDate = Today(), amount = 1180m, paymentMode = "Cash" }), HttpStatusCode.Created);
        paid.PaymentStatus.Should().Be("Paid");

        await c.Admin.PostAsJsonAsync($"{c.Base}/sales/{sale.Id}/returns", Return(sale.Items[0].Id, 1m));   // 590 back
        var h = await TestApi.Ok<PaymentHistoryDto>(await c.Member.GetAsync(pay));
        (h.ReturnedAmount, h.NetAmount, h.TotalReceived, h.Outstanding, h.RefundDue, h.PaymentStatus).Should().Be((590m, 590m, 1180m, 0m, 590m, "Paid"));
        (await c.Member.PostAsJsonAsync(pay, new { paymentDate = Today(), amount = 1m, paymentMode = "Cash" })).StatusCode.Should().Be((HttpStatusCode)422);

        var detail = await TestApi.Ok<SalesOrderDto>(await c.Member.GetAsync($"{c.Base}/sales/{sale.Id}"));
        (detail.ReturnedAmount, detail.NetAmount, detail.Outstanding, detail.RefundDue, detail.PaymentStatus).Should().Be((590m, 590m, 0m, 590m, "Paid"));
        var listed = await TestApi.Ok<PagedResult<SalesListItemDto>>(await c.Member.GetAsync($"{c.Base}/sales?paymentStatus=Paid"));
        listed.Items.Should().ContainSingle(i => i.Id == sale.Id && i.Outstanding == 0m);

        // An unpaid sale: the return cuts what the customer still owes.
        var unpaid = await SaleAsync(c, 2m);
        await c.Admin.PostAsJsonAsync($"{c.Base}/sales/{unpaid.Id}/returns", Return(unpaid.Items[0].Id, 1m));
        var d2 = await TestApi.Ok<SalesOrderDto>(await c.Member.GetAsync($"{c.Base}/sales/{unpaid.Id}"));
        (d2.Outstanding, d2.PaymentStatus, d2.RefundDue).Should().Be((590m, "Unpaid", 0m));
        await c.Member.PostAsJsonAsync($"{c.Base}/sales/{unpaid.Id}/payments", new { paymentDate = Today(), amount = 590m, paymentMode = "Upi" });
        (await TestApi.Ok<SalesOrderDto>(await c.Member.GetAsync($"{c.Base}/sales/{unpaid.Id}"))).PaymentStatus.Should().Be("Paid");
    }
}
