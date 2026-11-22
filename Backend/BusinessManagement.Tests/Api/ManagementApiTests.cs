using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BusinessManagement.API.Controllers;
using BusinessManagement.Application.Auth;
using BusinessManagement.Application.Businesses;
using BusinessManagement.Application.Common;
using BusinessManagement.Application.Users;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Tests.Database;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Tests.Api;

[Collection("Api")]
public sealed class ManagementApiTests(ApiFixture fx)
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private async Task<HttpClient> SignedInAsync(string username, string password)
    {
        var c = fx.CreateClient();
        var res = await c.PostAsJsonAsync("/api/v1/auth/login", new { username, password });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await res.Content.ReadFromJsonAsync<AuthResponse>())!;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return c;
    }

    private Task<HttpClient> AdminAsync() => SignedInAsync("admin", ApiFixture.AdminPassword);

    private static async Task<T> Ok<T>(HttpResponseMessage res, HttpStatusCode expected = HttpStatusCode.OK)
    {
        res.StatusCode.Should().Be(expected, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<UserListItemDto> CreateUserAsync(HttpClient admin, string role = "Member", IEnumerable<long>? businessIds = null, string? tag = null)
    {
        tag ??= Tag();
        var res = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            role, name = "Person " + tag, username = "p" + tag, mobile = DbFixture.NewMobile(), password = "Valid-Pass-12345",
            businessIds = businessIds?.ToArray()
        });
        return await Ok<UserListItemDto>(res, HttpStatusCode.Created);
    }

    private static async Task<BusinessDetailDto> CreateBusinessAsync(HttpClient admin, IEnumerable<long> memberIds, string? gst = null, IEnumerable<long>? employeeIds = null, string? name = null)
    {
        var res = await admin.PostAsJsonAsync("/api/v1/businesses", new
        {
            name = name ?? "Biz " + Tag(), category = "Retail", gstNumber = gst,
            memberIds = memberIds.ToArray(), employeeIds = employeeIds?.ToArray()
        });
        return await Ok<BusinessDetailDto>(res, HttpStatusCode.Created);
    }

    // ------------------------------------------------------------------------------ users
    [SqlFact]
    public async Task Creating_a_member_normalises_the_mobile_and_maps_businesses_with_view_only_permissions()
    {
        var admin = await AdminAsync();
        var m1 = await CreateUserAsync(admin);
        var biz = await CreateBusinessAsync(admin, [m1.Id]);

        var res = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            name = "Mapped Member", username = "mm" + Tag(), mobile = "+91 98765-43210", password = "Valid-Pass-12345", businessIds = new[] { biz.Id }
        });
        var created = await Ok<UserListItemDto>(res, HttpStatusCode.Created);
        created.Mobile.Should().Be("9876543210");
        created.Role.Should().Be("Member");
        created.BusinessIds.Should().Contain(biz.Id);

        var perms = await Ok<BusinessPermissionsDto>(await admin.GetAsync($"/api/v1/businesses/{biz.Id}/members/{created.Id}/permissions"));
        perms.CanViewBusiness.Should().BeTrue();
        perms.Should().BeEquivalentTo(new BusinessPermissionsDto(true, false, false, false, false, false, false, false, false,
            false, false, false, false, false, false, false, false, false, false, false, false), "everything else is off until the Admin grants it");
    }

    [SqlFact]
    public async Task User_creation_rejects_bad_input_and_duplicates()
    {
        var admin = await AdminAsync();
        var u = await CreateUserAsync(admin);

        (await admin.PostAsJsonAsync("/api/v1/users", new { name = "Dup", username = u.Username, mobile = DbFixture.NewMobile(), password = "Valid-Pass-12345" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsJsonAsync("/api/v1/users", new { name = "Weak", username = "weak" + Tag(), mobile = DbFixture.NewMobile(), password = "short" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/v1/users", new { name = "Bad", username = "bad" + Tag(), mobile = "123", password = "Valid-Pass-12345" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/v1/users", new { role = "Admin", name = "Second admin", username = "adm" + Tag(), mobile = DbFixture.NewMobile(), password = "Valid-Pass-12345" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "there is exactly one Admin");
    }

    [SqlFact]
    public async Task Only_the_admin_can_manage_users()
    {
        var member = await SignedInAsync("member1", ApiFixture.MemberPassword);
        (await member.GetAsync("/api/v1/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PostAsJsonAsync("/api/v1/users", new { name = "x", username = "x" + Tag(), mobile = DbFixture.NewMobile(), password = "Valid-Pass-12345" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PutAsJsonAsync("/api/v1/me", new { name = "x", mobile = DbFixture.NewMobile(), email = "x@example.test" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SqlFact]
    public async Task User_list_supports_filtering_search_sorting_and_paging_limits()
    {
        var admin = await AdminAsync();
        var tag = Tag();
        await CreateUserAsync(admin, tag: tag + "a");
        await CreateUserAsync(admin, "Employee", tag: tag + "b");

        var members = await Ok<PagedResult<UserListItemDto>>(await admin.GetAsync($"/api/v1/users?role=Member&q={tag}"));
        members.Items.Should().ContainSingle().Which.Username.Should().Be("p" + tag + "a");
        var employees = await Ok<PagedResult<UserListItemDto>>(await admin.GetAsync($"/api/v1/users?role=Employee&q={tag}"));
        employees.Items.Should().ContainSingle().Which.Role.Should().Be("Employee");

        (await admin.GetAsync("/api/v1/users?pageSize=1000")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.GetAsync("/api/v1/users?role=Admin")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var page = await Ok<PagedResult<UserListItemDto>>(await admin.GetAsync("/api/v1/users?pageSize=1&sort=-name"));
        page.Items.Count.Should().Be(1);
        page.Total.Should().BeGreaterThan(1);
    }

    [SqlFact]
    public async Task Admin_edits_own_profile()
    {
        var admin = await AdminAsync();
        var res = await admin.PutAsJsonAsync("/api/v1/me", new { name = "Site Admin", mobile = "9000000000", email = "admin@example.test" });
        (await Ok<UserDto>(res)).Name.Should().Be("Site Admin");
        (await admin.PutAsJsonAsync("/api/v1/me", new { name = "Site Admin", mobile = "9000000000", email = "" })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "the Admin needs an email for password reset");
    }

    // ------------------------------------------------------------------------------ businesses
    [SqlFact]
    public async Task Business_requires_at_least_one_valid_member_and_a_valid_gstin()
    {
        var admin = await AdminAsync();
        (await admin.PostAsJsonAsync("/api/v1/businesses", new { name = "No members " + Tag(), memberIds = Array.Empty<long>() }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var m = await CreateUserAsync(admin);
        var emp = await CreateUserAsync(admin, "Employee");
        (await admin.PostAsJsonAsync("/api/v1/businesses", new { name = "Bad gst " + Tag(), gstNumber = "27AAPFU0939F1ZX", memberIds = new[] { m.Id } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/v1/businesses", new { name = "Emp as member " + Tag(), memberIds = new[] { emp.Id } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "an employee cannot fill the mandatory member slot");
        (await admin.PostAsJsonAsync("/api/v1/businesses", new { name = "Ghost " + Tag(), memberIds = new long[] { 9999999 } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var ok = await CreateBusinessAsync(admin, [m.Id], gst: "27ABCDE1234F1Z0", employeeIds: [emp.Id]);
        ok.StateCode.Should().Be("27", "state code is taken from the GSTIN");
        ok.Members.Select(x => x.UserId).Should().BeEquivalentTo([m.Id, emp.Id]);
    }

    [SqlFact]
    public async Task Business_name_and_gst_number_must_be_unique()
    {
        var admin = await AdminAsync();
        var m = await CreateUserAsync(admin);
        var name = "Unique " + Tag();
        await CreateBusinessAsync(admin, [m.Id], gst: "29ABCDE1234F1ZW", name: name);
        (await admin.PostAsJsonAsync("/api/v1/businesses", new { name, memberIds = new[] { m.Id } })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsJsonAsync("/api/v1/businesses", new { name = "Other " + Tag(), gstNumber = "29ABCDE1234F1ZW", memberIds = new[] { m.Id } }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [SqlFact]
    public async Task Members_only_see_and_open_the_businesses_they_are_assigned_to()
    {
        var admin = await AdminAsync();
        var tag = Tag();
        var alice = await CreateUserAsync(admin, tag: "al" + tag);
        var bob = await CreateUserAsync(admin, tag: "bo" + tag);
        var bizA = await CreateBusinessAsync(admin, [alice.Id], name: "A " + tag);
        var bizB = await CreateBusinessAsync(admin, [bob.Id], name: "B " + tag);

        var aliceClient = await SignedInAsync(alice.Username, "Valid-Pass-12345");
        var list = await Ok<PagedResult<BusinessListItemDto>>(await aliceClient.GetAsync($"/api/v1/businesses?q={tag}"));
        list.Items.Select(b => b.Id).Should().BeEquivalentTo([bizA.Id]);

        (await aliceClient.GetAsync($"/api/v1/businesses/{bizA.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await aliceClient.GetAsync($"/api/v1/businesses/{bizB.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var adminList = await Ok<PagedResult<BusinessListItemDto>>(await admin.GetAsync($"/api/v1/businesses?q={tag}"));
        adminList.Items.Select(b => b.Id).Should().BeEquivalentTo([bizA.Id, bizB.Id]);

        // Members cannot create/modify businesses.
        (await aliceClient.PostAsJsonAsync("/api/v1/businesses", new { name = "Mine " + Tag(), memberIds = new[] { alice.Id } })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await aliceClient.DeleteAsync($"/api/v1/businesses/{bizA.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await aliceClient.PutAsJsonAsync($"/api/v1/businesses/{bizA.Id}/members", new { memberIds = new[] { alice.Id } })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SqlFact]
    public async Task Replacing_business_members_revokes_access_for_removed_members_but_keeps_history_rows()
    {
        var admin = await AdminAsync();
        var a = await CreateUserAsync(admin);
        var b = await CreateUserAsync(admin);
        var biz = await CreateBusinessAsync(admin, [a.Id, b.Id]);
        var bClient = await SignedInAsync(b.Username, "Valid-Pass-12345");
        (await bClient.GetAsync($"/api/v1/businesses/{biz.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await Ok<BusinessDetailDto>(await admin.PutAsJsonAsync($"/api/v1/businesses/{biz.Id}/members", new { memberIds = new[] { a.Id } }));
        updated.Members.Select(m => m.UserId).Should().BeEquivalentTo([a.Id]);
        (await bClient.GetAsync($"/api/v1/businesses/{biz.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = fx.Db.Create();
        (await db.BusinessMembers.AnyAsync(m => m.BusinessId == biz.Id && m.UserId == b.Id && !m.IsActive)).Should().BeTrue("removed members are deactivated, not erased");

        (await admin.PutAsJsonAsync($"/api/v1/businesses/{biz.Id}/members", new { memberIds = Array.Empty<long>() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Re-adding restores access.
        await Ok<BusinessDetailDto>(await admin.PutAsJsonAsync($"/api/v1/businesses/{biz.Id}/members", new { memberIds = new[] { a.Id, b.Id } }));
        (await bClient.GetAsync($"/api/v1/businesses/{biz.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SqlFact]
    public async Task A_business_can_never_be_left_without_a_member_via_user_changes()
    {
        var admin = await AdminAsync();
        var sole = await CreateUserAsync(admin);
        var other = await CreateUserAsync(admin);
        var biz = await CreateBusinessAsync(admin, [sole.Id]);

        // Removing the sole member through the user's own business list is blocked...
        var res = await admin.PutAsJsonAsync($"/api/v1/users/{sole.Id}", new { name = sole.Name, mobile = sole.Mobile, isActive = true, businessIds = Array.Empty<long>() });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // ...as is deleting them...
        (await admin.DeleteAsync($"/api/v1/users/{sole.Id}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Ok<BusinessDetailDto>(await admin.GetAsync($"/api/v1/businesses/{biz.Id}"))).Members.Should().ContainSingle(m => m.UserId == sole.Id);

        // ...but works once another member exists.
        await Ok<BusinessDetailDto>(await admin.PutAsJsonAsync($"/api/v1/businesses/{biz.Id}/members", new { memberIds = new[] { sole.Id, other.Id } }));
        (await admin.DeleteAsync($"/api/v1/users/{sole.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/v1/users/{sole.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await fx.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { username = sole.Username, password = "Valid-Pass-12345" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "deleted users cannot sign in");
    }

    [SqlFact]
    public async Task Deleting_a_business_is_a_soft_delete()
    {
        var admin = await AdminAsync();
        var m = await CreateUserAsync(admin);
        var biz = await CreateBusinessAsync(admin, [m.Id]);
        (await admin.DeleteAsync($"/api/v1/businesses/{biz.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/v1/businesses/{biz.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = fx.Db.Create();
        (await db.Businesses.IgnoreQueryFilters().SingleAsync(b => b.Id == biz.Id)).IsDeleted.Should().BeTrue();
    }

    // ------------------------------------------------------------------------------ permissions
    [SqlFact]
    public async Task Admin_configures_permissions_per_member_and_per_business_and_changes_are_audited()
    {
        var admin = await AdminAsync();
        var m = await CreateUserAsync(admin);
        var bizA = await CreateBusinessAsync(admin, [m.Id]);
        var bizB = await CreateBusinessAsync(admin, [m.Id]);
        var memberClient = await SignedInAsync(m.Username, "Valid-Pass-12345");

        var grant = new BusinessPermissionsDto(true, true, true, false, true, true, true, true, false,
            true, true, true, false, false /* member-wise breakdown stays hidden */, true, true, false, false, true, true, true);
        var saved = await Ok<BusinessPermissionsDto>(await admin.PutAsJsonAsync($"/api/v1/businesses/{bizA.Id}/members/{m.Id}/permissions", grant));
        saved.Should().BeEquivalentTo(grant);

        // Different business, different permissions (FRD s.3): bizB is untouched.
        var a = await Ok<MyPermissionsResponse>(await memberClient.GetAsync($"/api/v1/businesses/{bizA.Id}/my-permissions"));
        var b = await Ok<MyPermissionsResponse>(await memberClient.GetAsync($"/api/v1/businesses/{bizB.Id}/my-permissions"));
        a.Permissions["AddSales"].Should().BeTrue();
        a.Permissions["ViewMemberWiseExpenses"].Should().BeFalse();
        b.Permissions["AddSales"].Should().BeFalse();

        // Members cannot read or change permissions; non-assigned targets are 404.
        (await memberClient.GetAsync($"/api/v1/businesses/{bizA.Id}/members/{m.Id}/permissions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await memberClient.PutAsJsonAsync($"/api/v1/businesses/{bizA.Id}/members/{m.Id}/permissions", grant)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var stranger = await CreateUserAsync(admin);
        (await admin.GetAsync($"/api/v1/businesses/{bizA.Id}/members/{stranger.Id}/permissions")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = fx.Db.Create();
        var audit = await db.AuditLogs.Where(l => l.EntityName == "BusinessPermission" && l.BusinessId == bizA.Id && l.Action == AuditAction.Update).ToListAsync();
        audit.Should().NotBeEmpty();
        audit.Should().Contain(l => l.NewValues!.Contains("CanAddSales"));
        audit.Should().OnlyContain(l => l.UserId == fx.Db.AdminId);
    }
}
