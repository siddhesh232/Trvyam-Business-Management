using System.Net;
using System.Net.Http.Json;
using BusinessManagement.Application.Common;
using BusinessManagement.Application.Customers;
using BusinessManagement.Application.Products;
using BusinessManagement.Application.Sales;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Tests.Database;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Tests.Api;

[Collection("Api")]
public sealed class SalesApiTests(ApiFixture fx)
{
    private readonly TestApi _api = new(fx);
    private static string Today() => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    private static readonly string[] SalesPerms = ["CanViewSales", "CanAddSales", "CanEditOwnSales", "CanDeleteOwnSales", "CanViewCustomers"];

    private sealed record Ctx(HttpClient Admin, long BizId, HttpClient Member, ProductDto P1, ProductDto P2, CustomerDto Customer, string Url);

    /// <summary>Business with: P1 = 500 @18%, P2 = 200 @5%, a customer, and a member allowed to sell.</summary>
    private async Task<Ctx> SetupAsync()
    {
        var admin = await _api.AdminAsync();
        var biz = await _api.NewBusinessAsync(admin);
        var p1 = await _api.NewProductAsync(admin, biz.Id, 500m, 18m, "Widget");
        var p2 = await _api.NewProductAsync(admin, biz.Id, 200m, 5m, "Gadget");
        var customer = await _api.NewCustomerAsync(admin, biz.Id);
        var (member, _) = await _api.AddMemberAsync(admin, biz.Id, SalesPerms);
        return new Ctx(admin, biz.Id, member, p1, p2, customer, $"/api/v1/businesses/{biz.Id}/sales");
    }

    private static object Sale(long customerId, object[] items, decimal orderDiscount = 0) => new { customerId, orderDate = Today(), items, orderDiscount };
    private static object Item(long productId, decimal qty, decimal itemDiscount = 0, decimal? unitPrice = null) => new { productId, quantity = qty, itemDiscount, unitPrice };

    [SqlFact]
    public async Task Server_calculates_discounts_taxable_value_gst_and_final_amount_to_the_paisa()
    {
        var c = await SetupAsync();
        // Hand-calculated: P1 2x500=1000 less item discount 100 => 900; P2 1x200 => 200. Order discount 30 is
        // spread pro rata (900:200) => 24.55 / 5.45 (largest remainder). GST on the discounted value.
        var o = await TestApi.Ok<SalesOrderDto>(await c.Member.PostAsJsonAsync(c.Url,
            Sale(c.Customer.Id, [Item(c.P1.Id, 2, 100m), Item(c.P2.Id, 1)], 30m)), HttpStatusCode.Created);

        (o.Subtotal, o.DiscountAmount, o.TaxableAmount, o.GstAmount, o.FinalAmount).Should().Be((1200m, 130m, 1070.00m, 167.31m, 1237.31m));
        var i1 = o.Items[0]; var i2 = o.Items[1];
        (i1.ProductName, i1.Quantity, i1.UnitPrice, i1.DiscountAmount, i1.TaxableAmount, i1.GstAmount, i1.TotalAmount)
            .Should().Be(("Widget", 2m, 500m, 124.55m, 875.45m, 157.58m, 1033.03m));
        (i2.DiscountAmount, i2.TaxableAmount, i2.GstAmount, i2.TotalAmount).Should().Be((5.45m, 194.55m, 9.73m, 204.28m));

        // Header reconciles with lines.
        o.Items.Sum(i => i.TaxableAmount).Should().Be(o.TaxableAmount);
        o.Items.Sum(i => i.GstAmount).Should().Be(o.GstAmount);
        o.Items.Sum(i => i.DiscountAmount).Should().Be(o.DiscountAmount);
        (o.TotalReceived, o.Outstanding, o.PaymentStatus).Should().Be((0m, 1237.31m, "Unpaid"));
        o.PlaceOfSupplyState.Should().BeNull();
    }

    [SqlFact]
    public async Task Items_keep_a_snapshot_so_later_catalogue_changes_do_not_alter_history()
    {
        var c = await SetupAsync();
        var o = await TestApi.Ok<SalesOrderDto>(await c.Member.PostAsJsonAsync(c.Url, Sale(c.Customer.Id, [Item(c.P1.Id, 1)])), HttpStatusCode.Created);

        await TestApi.Ok<ProductDto>(await c.Admin.PutAsJsonAsync($"/api/v1/products/{c.P1.Id}", new
        {
            name = "Renamed Widget", sku = c.P1.Sku, sellingPrice = 999m, purchasePrice = 1m, gstRate = 28m, unit = "box", isActive = true
        }));

        var again = await TestApi.Ok<SalesOrderDto>(await c.Member.GetAsync($"{c.Url}/{o.Id}"));
        (again.Items[0].ProductName, again.Items[0].UnitPrice, again.Items[0].GstRate, again.Items[0].Unit).Should().Be(("Widget", 500m, 18m, "piece"));
        again.FinalAmount.Should().Be(590m);
    }

    [SqlFact]
    public async Task Only_catalogue_products_of_this_business_and_linked_customers_can_be_sold()
    {
        var c = await SetupAsync();
        var otherBiz = await _api.NewBusinessAsync(c.Admin);
        var foreign = await _api.NewProductAsync(c.Admin, otherBiz.Id, 10m, 0m);
        var unlinked = await _api.NewCustomerAsync(c.Admin, otherBiz.Id);

        (await c.Member.PostAsJsonAsync(c.Url, Sale(c.Customer.Id, [Item(foreign.Id, 1)]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.Member.PostAsJsonAsync(c.Url, Sale(unlinked.Id, [Item(c.P1.Id, 1)]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.Member.PostAsJsonAsync(c.Url, Sale(c.Customer.Id, []))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.Member.PostAsJsonAsync(c.Url, Sale(c.Customer.Id, [Item(c.P1.Id, 0)]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.Member.PostAsJsonAsync(c.Url, Sale(c.Customer.Id, [Item(c.P1.Id, 1, itemDiscount: 600m)]))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "discount above the line value");
        (await c.Member.PostAsJsonAsync(c.Url, Sale(c.Customer.Id, [Item(c.P1.Id, 1)], orderDiscount: 501m))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "order discount above the order value");

        await c.Admin.PutAsJsonAsync($"/api/v1/products/{c.P2.Id}", new { name = "Gadget", sku = c.P2.Sku, sellingPrice = 200m, purchasePrice = 1m, gstRate = 5m, unit = "piece", isActive = false });
        (await c.Member.PostAsJsonAsync(c.Url, Sale(c.Customer.Id, [Item(c.P2.Id, 1)]))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "inactive products cannot be sold");
    }

    [SqlFact]
    public async Task Only_the_admin_may_override_the_selling_price()
    {
        var c = await SetupAsync();
        (await c.Member.PostAsJsonAsync(c.Url, Sale(c.Customer.Id, [Item(c.P1.Id, 1, unitPrice: 400m)]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var same = await TestApi.Ok<SalesOrderDto>(await c.Member.PostAsJsonAsync(c.Url, Sale(c.Customer.Id, [Item(c.P1.Id, 1, unitPrice: 500m)])), HttpStatusCode.Created);
        same.FinalAmount.Should().Be(590m);
        var adminSale = await TestApi.Ok<SalesOrderDto>(await c.Admin.PostAsJsonAsync(c.Url, Sale(c.Customer.Id, [Item(c.P1.Id, 1, unitPrice: 400m)])), HttpStatusCode.Created);
        adminSale.FinalAmount.Should().Be(472m);   // 400 + 18%
    }

    [SqlFact]
    public async Task Sales_permissions_and_business_isolation_are_enforced()
    {
        var c = await SetupAsync();
        var (viewer, _) = await _api.AddMemberAsync(c.Admin, c.BizId, "CanViewSales");
        var (nothing, _) = await _api.AddMemberAsync(c.Admin, c.BizId);
        var sale = Sale(c.Customer.Id, [Item(c.P1.Id, 1)]);

        (await viewer.PostAsJsonAsync(c.Url, sale)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await nothing.GetAsync(c.Url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var (otherMember, _) = await _api.AddMemberAsync(c.Admin, (await _api.NewBusinessAsync(c.Admin)).Id, SalesPerms);
        (await otherMember.GetAsync(c.Url)).StatusCode.Should().Be(HttpStatusCode.NotFound, "not assigned to this business");
        (await fx.CreateClient().GetAsync(c.Url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SqlFact]
    public async Task Members_edit_and_delete_only_their_own_sales_and_the_totals_are_recalculated()
    {
        var c = await SetupAsync();
        var (other, _) = await _api.AddMemberAsync(c.Admin, c.BizId, SalesPerms);
        var mine = await TestApi.Ok<SalesOrderDto>(await c.Member.PostAsJsonAsync(c.Url, Sale(c.Customer.Id, [Item(c.P1.Id, 1)])), HttpStatusCode.Created);
        mine.CanEdit.Should().BeTrue();

        var edit = new { orderDate = Today(), items = new[] { Item(c.P1.Id, 2), Item(c.P2.Id, 5) }, orderDiscount = 0m };
        (await other.PutAsJsonAsync($"{c.Url}/{mine.Id}", edit)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.DeleteAsync($"{c.Url}/{mine.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await TestApi.Ok<SalesOrderDto>(await other.GetAsync($"{c.Url}/{mine.Id}"))).CanEdit.Should().BeFalse();

        var updated = await TestApi.Ok<SalesOrderDto>(await c.Member.PutAsJsonAsync($"{c.Url}/{mine.Id}", edit));
        updated.Items.Should().HaveCount(2);
        (updated.TaxableAmount, updated.GstAmount, updated.FinalAmount).Should().Be((2000m, 230m, 2230m));   // 1000@18% + 1000@5%

        (await c.Member.DeleteAsync($"{c.Url}/{mine.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.Member.GetAsync($"{c.Url}/{mine.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var db = fx.Db.Create();
        var row = await db.SalesOrders.IgnoreQueryFilters().SingleAsync(s => s.Id == mine.Id);
        row.IsDeleted.Should().BeTrue();
        (await db.AuditLogs.AnyAsync(a => a.EntityName == "SalesOrder" && a.EntityId == mine.Id && a.Action == AuditAction.Delete)).Should().BeTrue();

        // Admin can manage anyone's sale.
        var theirs = await TestApi.Ok<SalesOrderDto>(await other.PostAsJsonAsync(c.Url, Sale(c.Customer.Id, [Item(c.P2.Id, 1)])), HttpStatusCode.Created);
        (await c.Admin.DeleteAsync($"{c.Url}/{theirs.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [SqlFact]
    public async Task Sales_list_filters_by_date_customer_product_payment_status_and_search()
    {
        var c = await SetupAsync();
        var second = await _api.NewCustomerAsync(c.Admin, c.BizId);
        var s1 = await TestApi.Ok<SalesOrderDto>(await c.Member.PostAsJsonAsync(c.Url, Sale(c.Customer.Id, [Item(c.P1.Id, 1)])), HttpStatusCode.Created);
        var s2 = await TestApi.Ok<SalesOrderDto>(await c.Member.PostAsJsonAsync(c.Url, Sale(second.Id, [Item(c.P2.Id, 3)])), HttpStatusCode.Created);

        async Task<long[]> Ids(string query) =>
            (await TestApi.Ok<PagedResult<SalesListItemDto>>(await c.Member.GetAsync($"{c.Url}{query}"))).Items.Select(i => i.Id).ToArray();

        (await Ids("")).Should().BeEquivalentTo([s1.Id, s2.Id]);
        (await Ids($"?customerId={second.Id}")).Should().BeEquivalentTo([s2.Id]);
        (await Ids($"?productId={c.P1.Id}")).Should().BeEquivalentTo([s1.Id]);
        (await Ids($"?q={second.Mobile}")).Should().BeEquivalentTo([s2.Id]);
        (await Ids("?paymentStatus=Unpaid")).Should().BeEquivalentTo([s1.Id, s2.Id]);
        (await Ids("?paymentStatus=Paid")).Should().BeEmpty();
        (await Ids($"?from={Today()}&to={Today()}")).Should().HaveCount(2);
        (await Ids($"?to=2001-01-01")).Should().BeEmpty();
        (await c.Member.GetAsync($"{c.Url}?paymentStatus=Bogus")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var page = await TestApi.Ok<PagedResult<SalesListItemDto>>(await c.Member.GetAsync($"{c.Url}?pageSize=1&sort=-amount"));
        page.Total.Should().Be(2);
        page.Items.Single().Id.Should().Be(s2.Id, "sorted by amount descending: 630 (s2) comes before 590 (s1)");
    }
}
