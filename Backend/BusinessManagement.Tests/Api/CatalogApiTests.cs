using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BusinessManagement.Application.Auth;
using BusinessManagement.Application.Businesses;
using BusinessManagement.Application.Common;
using BusinessManagement.Application.Customers;
using BusinessManagement.Application.Products;
using BusinessManagement.Application.Users;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Tests.Database;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Tests.Api;

[Collection("Api")]
public sealed class CatalogApiTests(ApiFixture fx)
{
    private const string Pw = "Valid-Pass-12345";
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private async Task<HttpClient> SignedInAsync(string username, string password)
    {
        var c = fx.CreateClient();
        var res = await c.PostAsJsonAsync("/api/v1/auth/login", new { username, password });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await res.Content.ReadFromJsonAsync<AuthResponse>())!.AccessToken);
        return c;
    }

    private Task<HttpClient> AdminAsync() => SignedInAsync("admin", ApiFixture.AdminPassword);

    private static async Task<T> Ok<T>(HttpResponseMessage res, HttpStatusCode expected = HttpStatusCode.OK)
    {
        res.StatusCode.Should().Be(expected, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<T>())!;
    }

    private static Dictionary<string, bool> Perms(params string[] on)
    {
        var d = typeof(BusinessPermissionsDto).GetProperties().ToDictionary(p => p.Name, _ => false);
        foreach (var o in on) d[o].Should().BeFalse(); // sanity: name exists
        foreach (var o in on) d[o] = true;
        return d;
    }

    /// <summary>Creates a member + business, grants the given permissions, returns a signed-in client for that member.</summary>
    private async Task<(HttpClient Member, UserListItemDto User, BusinessDetailDto Biz, HttpClient Admin)> MemberInBusinessAsync(params string[] permissions)
    {
        var admin = await AdminAsync();
        var tag = Tag();
        var user = await Ok<UserListItemDto>(await admin.PostAsJsonAsync("/api/v1/users", new
        { name = "M " + tag, username = "m" + tag, mobile = DbFixture.NewMobile(), password = Pw }), HttpStatusCode.Created);
        var biz = await Ok<BusinessDetailDto>(await admin.PostAsJsonAsync("/api/v1/businesses", new { name = "B " + tag, memberIds = new[] { user.Id } }), HttpStatusCode.Created);
        await Ok<BusinessPermissionsDto>(await admin.PutAsJsonAsync($"/api/v1/businesses/{biz.Id}/members/{user.Id}/permissions", Perms(permissions.Append("CanViewBusiness").Distinct().ToArray())));
        return (await SignedInAsync(user.Username, Pw), user, biz, admin);
    }

    private static object ProductBody(string sku, decimal price = 100m, decimal gst = 18m, bool active = true, long[]? businessIds = null, string? name = null) => new
    {
        name = name ?? "Product " + sku, sku, category = "General", sellingPrice = price, purchasePrice = 60m, gstRate = gst, hsnCode = "8471", unit = "piece", isActive = active, businessIds
    };

    // ------------------------------------------------------------------------------ products
    [SqlFact]
    public async Task Admin_manages_the_catalogue_and_validates_input()
    {
        var admin = await AdminAsync();
        var sku = "SKU-" + Tag();
        var created = await Ok<ProductDto>(await admin.PostAsJsonAsync("/api/v1/products", ProductBody(sku)), HttpStatusCode.Created);
        created.PurchasePrice.Should().Be(60m);

        (await admin.PostAsJsonAsync("/api/v1/products", ProductBody(sku))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsJsonAsync("/api/v1/products", ProductBody("S-" + Tag(), gst: 150m))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/v1/products", ProductBody("S-" + Tag(), price: -1m))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/v1/products", ProductBody("S-" + Tag(), price: 10.999m))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var updated = await Ok<ProductDto>(await admin.PutAsJsonAsync($"/api/v1/products/{created.Id}", ProductBody(sku, price: 125.50m, gst: 5m)));
        (updated.SellingPrice, updated.GstRate).Should().Be((125.50m, 5m));

        var list = await Ok<PagedResult<ProductDto>>(await admin.GetAsync($"/api/v1/products?q={sku}"));
        list.Items.Should().ContainSingle();

        (await admin.DeleteAsync($"/api/v1/products/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/v1/products/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var db = fx.Db.Create();
        (await db.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == created.Id)).IsDeleted.Should().BeTrue();
    }

    [SqlFact]
    public async Task Members_only_see_active_products_mapped_to_their_business_without_cost_price()
    {
        var (member, _, biz, admin) = await MemberInBusinessAsync("CanViewProducts");
        var other = await Ok<BusinessDetailDto>(await admin.PostAsJsonAsync("/api/v1/businesses", new { name = "Other " + Tag(), memberIds = new[] { fx.Db.MemberId } }), HttpStatusCode.Created);

        var tag = Tag();
        var mapped = await Ok<ProductDto>(await admin.PostAsJsonAsync("/api/v1/products", ProductBody("MAP-" + tag, businessIds: [biz.Id])), HttpStatusCode.Created);
        await Ok<ProductDto>(await admin.PostAsJsonAsync("/api/v1/products", ProductBody("OTH-" + tag, businessIds: [other.Id])), HttpStatusCode.Created);
        await Ok<ProductDto>(await admin.PostAsJsonAsync("/api/v1/products", ProductBody("OFF-" + tag, active: false, businessIds: [biz.Id])), HttpStatusCode.Created);

        var res = await member.GetAsync($"/api/v1/businesses/{biz.Id}/products?q={tag}");
        var body = await res.Content.ReadAsStringAsync();
        res.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().Contain("MAP-" + tag).And.NotContain("OTH-" + tag).And.NotContain("OFF-" + tag);
        body.ToLowerInvariant().Should().NotContain("purchaseprice", "cost price is admin-only");

        // Re-mapping from the admin side takes effect immediately.
        await Ok<ProductDto>(await admin.PutAsJsonAsync($"/api/v1/products/{mapped.Id}/businesses", new { businessIds = new[] { other.Id } }));
        (await member.GetAsync($"/api/v1/businesses/{biz.Id}/products?q={tag}")).Content.ReadAsStringAsync().Result.Should().NotContain("MAP-" + tag);
    }

    [SqlFact]
    public async Task Catalogue_endpoints_enforce_admin_role_and_permission()
    {
        var (noPerm, _, biz, _) = await MemberInBusinessAsync();               // ViewBusiness only
        (await noPerm.GetAsync($"/api/v1/businesses/{biz.Id}/products")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await noPerm.GetAsync("/api/v1/products")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await noPerm.PostAsJsonAsync("/api/v1/products", ProductBody("X-" + Tag()))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var (otherMember, _, _, _) = await MemberInBusinessAsync("CanViewProducts");
        (await otherMember.GetAsync($"/api/v1/businesses/{biz.Id}/products")).StatusCode.Should().Be(HttpStatusCode.NotFound, "not assigned to that business");
    }

    // ------------------------------------------------------------------------------ customers
    [SqlFact]
    public async Task Resolving_a_mobile_creates_once_and_tags_each_business_without_duplicates()
    {
        var (mA, _, bizA, admin) = await MemberInBusinessAsync("CanAddCustomers", "CanViewCustomers");
        var (mB, _, bizB, _) = await MemberInBusinessAsync("CanAddCustomers", "CanViewCustomers");
        var mobile = DbFixture.NewMobile();

        var first = await Ok<ResolveCustomerResponse>(await mA.PostAsJsonAsync($"/api/v1/businesses/{bizA.Id}/customers/resolve",
            new { mobile = "+91 " + mobile, name = "Asha Patel", address = "Pune" }));
        first.Created.Should().BeTrue();
        first.NewlyLinked.Should().BeTrue();
        first.Customer.Mobile.Should().Be(mobile);

        // Same person buys from another business: same global record, new tag, no duplicate.
        var second = await Ok<ResolveCustomerResponse>(await mB.PostAsJsonAsync($"/api/v1/businesses/{bizB.Id}/customers/resolve", new { mobile }));
        second.Created.Should().BeFalse();
        second.NewlyLinked.Should().BeTrue();
        second.Customer.Id.Should().Be(first.Customer.Id);
        second.Customer.Name.Should().Be("Asha Patel");
        second.Customer.BusinessIds.Should().BeEquivalentTo([bizB.Id], "a member never learns about businesses they are not assigned to");

        var again = await Ok<ResolveCustomerResponse>(await mB.PostAsJsonAsync($"/api/v1/businesses/{bizB.Id}/customers/resolve", new { mobile }));
        (again.Created, again.NewlyLinked).Should().Be((false, false));

        var viaAdmin = await Ok<CustomerDto>(await admin.GetAsync($"/api/v1/customers/{first.Customer.Id}"));
        viaAdmin.BusinessIds.Should().BeEquivalentTo([bizA.Id, bizB.Id]);

        await using var db = fx.Db.Create();
        (await db.Customers.CountAsync(c => c.Mobile == mobile)).Should().Be(1);
    }

    [SqlFact]
    public async Task New_customers_need_a_name_and_a_valid_mobile_and_gstin()
    {
        var (m, _, biz, _) = await MemberInBusinessAsync("CanAddCustomers");
        var url = $"/api/v1/businesses/{biz.Id}/customers/resolve";
        (await m.PostAsJsonAsync(url, new { mobile = DbFixture.NewMobile() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await m.PostAsJsonAsync(url, new { mobile = "12345", name = "Bad" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await m.PostAsJsonAsync(url, new { mobile = DbFixture.NewMobile(), name = "Bad Gst", gstin = "27AAPFU0939F1ZX" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var ok = await Ok<ResolveCustomerResponse>(await m.PostAsJsonAsync(url, new { mobile = DbFixture.NewMobile(), name = "With Gst", gstin = "27AAPFU0939F1ZV" }));
        ok.Customer.StateCode.Should().Be("27");
    }

    [SqlFact]
    public async Task Lookup_is_read_only_and_resolve_requires_AddCustomers()
    {
        var (viewer, _, biz, admin) = await MemberInBusinessAsync("CanViewCustomers");
        var mobile = DbFixture.NewMobile();
        (await viewer.GetAsync($"/api/v1/businesses/{biz.Id}/customers/lookup?mobile={mobile}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await viewer.PostAsJsonAsync($"/api/v1/businesses/{biz.Id}/customers/resolve", new { mobile, name = "Nope" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var (adder, _, bizAdd, _) = await MemberInBusinessAsync("CanAddCustomers");
        await Ok<ResolveCustomerResponse>(await adder.PostAsJsonAsync($"/api/v1/businesses/{bizAdd.Id}/customers/resolve", new { mobile, name = "Exists" }));

        // Viewer of ANOTHER business can look the number up (global DB) but it does not tag their business.
        var found = await Ok<CustomerDto>(await viewer.GetAsync($"/api/v1/businesses/{biz.Id}/customers/lookup?mobile={mobile}"));
        found.Name.Should().Be("Exists");
        found.BusinessIds.Should().BeEmpty();
        var list = await Ok<PagedResult<CustomerDto>>(await viewer.GetAsync($"/api/v1/businesses/{biz.Id}/customers"));
        list.Items.Should().NotContain(c => c.Mobile == mobile, "lookup must not create a tag");

        (await admin.GetAsync("/api/v1/customers?pageSize=5")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.GetAsync("/api/v1/customers")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SqlFact]
    public async Task Editing_a_shared_customer_needs_EditCustomers_a_tag_in_the_business_and_is_audited()
    {
        var (editor, user, biz, admin) = await MemberInBusinessAsync("CanAddCustomers", "CanViewCustomers", "CanEditCustomers");
        var (viewer, _, bizV, _) = await MemberInBusinessAsync("CanViewCustomers");
        var mobile = DbFixture.NewMobile();
        var c = (await Ok<ResolveCustomerResponse>(await editor.PostAsJsonAsync($"/api/v1/businesses/{biz.Id}/customers/resolve", new { mobile, name = "Old Name" }))).Customer;

        var edited = await Ok<CustomerDto>(await editor.PutAsJsonAsync($"/api/v1/businesses/{biz.Id}/customers/{c.Id}", new { name = "New Name", address = "Mumbai" }));
        (edited.Name, edited.Address).Should().Be(("New Name", "Mumbai"));

        (await viewer.PutAsJsonAsync($"/api/v1/businesses/{bizV.Id}/customers/{c.Id}", new { name = "Hack" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var (editor2, _, biz2, _) = await MemberInBusinessAsync("CanEditCustomers");
        (await editor2.PutAsJsonAsync($"/api/v1/businesses/{biz2.Id}/customers/{c.Id}", new { name = "Not tagged here" })).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "customer is not tagged to that member's business");

        await using var db = fx.Db.Create();
        var audit = await db.AuditLogs.Where(a => a.EntityName == "Customer" && a.EntityId == c.Id && a.Action == AuditAction.Update).SingleAsync();
        audit.UserId.Should().Be(user.Id);
        audit.OldValues.Should().Contain("Old Name");
        audit.NewValues.Should().Contain("New Name");
    }

    [SqlFact]
    public async Task Admin_can_edit_any_customer_and_search_by_mobile()
    {
        var (adder, _, biz, admin) = await MemberInBusinessAsync("CanAddCustomers");
        var mobile = DbFixture.NewMobile();
        var c = (await Ok<ResolveCustomerResponse>(await adder.PostAsJsonAsync($"/api/v1/businesses/{biz.Id}/customers/resolve", new { mobile, name = "Searchable" }))).Customer;
        var found = await Ok<PagedResult<CustomerDto>>(await admin.GetAsync($"/api/v1/customers?q={mobile}"));
        found.Items.Should().ContainSingle().Which.Id.Should().Be(c.Id);
        var edited = await Ok<CustomerDto>(await admin.PutAsJsonAsync($"/api/v1/customers/{c.Id}", new { name = "Renamed By Admin" }));
        edited.Name.Should().Be("Renamed By Admin");
    }
}
