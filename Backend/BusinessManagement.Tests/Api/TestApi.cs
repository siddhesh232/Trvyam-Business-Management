using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BusinessManagement.Application.Auth;
using BusinessManagement.Application.Businesses;
using BusinessManagement.Application.Customers;
using BusinessManagement.Application.Products;
using BusinessManagement.Application.Users;
using BusinessManagement.Tests.Database;
using FluentAssertions;

namespace BusinessManagement.Tests.Api;

/// <summary>Shared helpers for API tests: sign-in, data builders that go through the real API.</summary>
public sealed class TestApi(ApiFixture fx)
{
    public const string Pw = "Valid-Pass-12345";
    public static string Tag() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>A random but checksum-valid GSTIN for the given 2-digit state code.</summary>
    public static string NewGstin(string state = "27")
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var r = Random.Shared;
        string Letters(int n) => new(Enumerable.Range(0, n).Select(_ => (char)('A' + r.Next(26))).ToArray());
        var b = state + Letters(5) + r.Next(1000, 9999) + Letters(1) + "1Z";
        var sum = 0;
        for (var i = 0; i < 14; i++) { var v = alphabet.IndexOf(b[i]) * (i % 2 == 0 ? 1 : 2); sum += v / 36 + v % 36; }
        return b + alphabet[(36 - sum % 36) % 36];
    }

    /// <summary>Fills in GSTIN + address so the business can issue GST invoices.</summary>
    public async Task<BusinessDetailDto> MakeInvoiceReadyAsync(HttpClient admin, BusinessDetailDto biz, string? gstin = null) =>
        await Ok<BusinessDetailDto>(await admin.PutAsJsonAsync($"/api/v1/businesses/{biz.Id}", new
        { name = biz.Name, gstNumber = gstin ?? NewGstin("27"), addressLine = "12 Market Road, Pune", isActive = true }));

    public async Task<HttpClient> SignInAsync(string username, string password)
    {
        var c = fx.CreateClient();
        var res = await c.PostAsJsonAsync("/api/v1/auth/login", new { username, password });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await res.Content.ReadFromJsonAsync<AuthResponse>())!.AccessToken);
        return c;
    }

    public Task<HttpClient> AdminAsync() => SignInAsync("admin", ApiFixture.AdminPassword);

    public static async Task<T> Ok<T>(HttpResponseMessage res, HttpStatusCode expected = HttpStatusCode.OK)
    {
        res.StatusCode.Should().Be(expected, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<T>())!;
    }

    public static Dictionary<string, bool> Perms(params string[] on)
    {
        var d = typeof(BusinessPermissionsDto).GetProperties().ToDictionary(p => p.Name, _ => false);
        foreach (var o in on) d.Should().ContainKey(o);
        foreach (var o in on) d[o] = true;
        d["CanViewBusiness"] = true;
        return d;
    }

    public async Task<UserListItemDto> NewUserAsync(HttpClient admin, string role = "Member")
    {
        var tag = Tag();
        return await Ok<UserListItemDto>(await admin.PostAsJsonAsync("/api/v1/users", new
        { role, name = "User " + tag, username = "u" + tag, mobile = DbFixture.NewMobile(), password = Pw }), HttpStatusCode.Created);
    }

    /// <summary>A business with one view-only "owner" member (the mandatory first member).</summary>
    public async Task<BusinessDetailDto> NewBusinessAsync(HttpClient admin, string? gst = null)
    {
        var owner = await NewUserAsync(admin);
        return await Ok<BusinessDetailDto>(await admin.PostAsJsonAsync("/api/v1/businesses",
            new { name = "Biz " + Tag(), gstNumber = gst, memberIds = new[] { owner.Id } }), HttpStatusCode.Created);
    }

    /// <summary>Adds a new member to an existing business with exactly the given permissions; returns their signed-in client.</summary>
    public async Task<(HttpClient Client, UserListItemDto User)> AddMemberAsync(HttpClient admin, long businessId, params string[] permissions)
    {
        var user = await NewUserAsync(admin);
        var biz = await Ok<BusinessDetailDto>(await admin.GetAsync($"/api/v1/businesses/{businessId}"));
        var ids = biz.Members.Where(m => m.Role == "Member").Select(m => m.UserId).Append(user.Id).ToArray();
        await Ok<BusinessDetailDto>(await admin.PutAsJsonAsync($"/api/v1/businesses/{businessId}/members", new { memberIds = ids }));
        await Ok<BusinessPermissionsDto>(await admin.PutAsJsonAsync($"/api/v1/businesses/{businessId}/members/{user.Id}/permissions", Perms(permissions)));
        return (await SignInAsync(user.Username, Pw), user);
    }

    public async Task<ProductDto> NewProductAsync(HttpClient admin, long businessId, decimal price, decimal gst, string? name = null)
    {
        var sku = "SKU-" + Tag();
        return await Ok<ProductDto>(await admin.PostAsJsonAsync("/api/v1/products", new
        {
            name = name ?? "Product " + sku, sku, category = "General", sellingPrice = price, purchasePrice = price / 2, gstRate = gst,
            hsnCode = "8471", unit = "piece", isActive = true, businessIds = new[] { businessId }
        }), HttpStatusCode.Created);
    }

    public async Task<CustomerDto> NewCustomerAsync(HttpClient admin, long businessId, string? gstin = null)
    {
        var res = await Ok<ResolveCustomerResponse>(await admin.PostAsJsonAsync($"/api/v1/businesses/{businessId}/customers/resolve",
            new { mobile = DbFixture.NewMobile(), name = "Customer " + Tag(), gstin }));
        return res.Customer;
    }
}
