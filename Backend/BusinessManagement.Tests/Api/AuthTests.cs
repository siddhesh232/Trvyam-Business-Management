using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BusinessManagement.Application.Auth;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Tests.Database;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BusinessManagement.Tests.Api;

[Collection("Api")]
public sealed class AuthTests(ApiFixture fx)
{
    private HttpClient Client() => fx.CreateClient();

    private static async Task<HttpResponseMessage> PostAsync(HttpClient c, string url, object body, string? bearer = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (bearer is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await c.SendAsync(req);
    }

    private async Task<AuthResponse> LoginAsync(string username, string password)
    {
        var res = await PostAsync(Client(), "/api/v1/auth/login", new { username, password });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private static string Sha(string raw) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    // -------------------------------------------------------------------------------- login
    [SqlFact]
    public async Task Login_returns_tokens_and_a_jwt_with_subject_and_role()
    {
        var auth = await LoginAsync("admin", ApiFixture.AdminPassword);
        auth.User.Role.Should().Be("Admin");
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(auth.AccessToken);
        jwt.GetClaim("sub").Value.Should().Be(fx.Db.AdminId.ToString());
        jwt.GetClaim("role").Value.Should().Be("Admin");
        (jwt.ValidTo - DateTime.UtcNow).Should().BeLessThan(TimeSpan.FromMinutes(16));
        auth.RefreshToken.Length.Should().BeGreaterThan(40);

        await using var db = fx.Db.Create();
        var stored = await db.RefreshTokens.SingleAsync(t => t.TokenHash == Sha(auth.RefreshToken));
        stored.TokenHash.Should().NotContain(auth.RefreshToken, "only the hash is stored");
    }

    [SqlFact]
    public async Task Wrong_password_and_unknown_user_get_the_same_generic_401()
    {
        var wrong = await PostAsync(Client(), "/api/v1/auth/login", new { username = "admin", password = "nope-nope-nope" });
        var unknown = await PostAsync(Client(), "/api/v1/auth/login", new { username = "ghost", password = "nope-nope-nope" });
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await wrong.Content.ReadAsStringAsync()).Should().Contain("Invalid username or password.");
        (await unknown.Content.ReadAsStringAsync()).Should().Contain("Invalid username or password.");
    }

    [SqlFact]
    public async Task Deactivated_users_cannot_sign_in()
    {
        var u = await fx.CreateUserAsync(UserRole.Member);
        await using (var db = fx.Db.Create())
        {
            (await db.Users.SingleAsync(x => x.Id == u.Id)).IsActive = false;
            await db.SaveChangesAsync();
        }
        var res = await PostAsync(Client(), "/api/v1/auth/login", new { username = u.Username, password = ApiFixture.MemberPassword });
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SqlFact]
    public async Task Login_validation_errors_return_400_problem_details()
    {
        var res = await PostAsync(Client(), "/api/v1/auth/login", new { username = "", password = "" });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync()).Should().Contain("validation_error");
    }

    [SqlFact]
    public async Task Me_returns_the_signed_in_user()
    {
        var auth = await LoginAsync("member1", ApiFixture.MemberPassword);
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var res = await Client().SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadFromJsonAsync<UserDto>())!.Username.Should().Be("member1");
    }

    // -------------------------------------------------------------------------------- refresh tokens
    [SqlFact]
    public async Task Refresh_rotates_the_token_and_reuse_revokes_every_session()
    {
        var first = await LoginAsync("member1", ApiFixture.MemberPassword);
        var r1 = await PostAsync(Client(), "/api/v1/auth/refresh", new { refreshToken = first.RefreshToken });
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = (await r1.Content.ReadFromJsonAsync<AuthResponse>())!;
        second.RefreshToken.Should().NotBe(first.RefreshToken);

        // Old token presented again => theft suspected => whole family revoked.
        var reuse = await PostAsync(Client(), "/api/v1/auth/refresh", new { refreshToken = first.RefreshToken });
        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var afterReuse = await PostAsync(Client(), "/api/v1/auth/refresh", new { refreshToken = second.RefreshToken });
        afterReuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SqlFact]
    public async Task Expired_refresh_token_is_rejected()
    {
        var auth = await LoginAsync("member1", ApiFixture.MemberPassword);
        await using (var db = fx.Db.Create())
        {
            var t = await db.RefreshTokens.SingleAsync(x => x.TokenHash == Sha(auth.RefreshToken));
            t.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        var res = await PostAsync(Client(), "/api/v1/auth/refresh", new { refreshToken = auth.RefreshToken });
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SqlFact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var auth = await LoginAsync("member1", ApiFixture.MemberPassword);
        var logout = await PostAsync(Client(), "/api/v1/auth/logout", new { refreshToken = auth.RefreshToken }, auth.AccessToken);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var res = await PostAsync(Client(), "/api/v1/auth/refresh", new { refreshToken = auth.RefreshToken });
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -------------------------------------------------------------------------------- admin password reset
    private static string ExtractTemporaryPassword(FakeEmailSender.Sent mail) =>
        Regex.Match(mail.Body, @"Temporary password: (\S+)").Groups[1].Value;

    [SqlFact]
    public async Task Admin_reset_flow_emails_a_temporary_password_that_works_once()
    {
        fx.Email.Clear();
        try
        {
            (await PostAsync(Client(), "/api/v1/auth/admin/forgot-password", new { identifier = "admin" }))
                .StatusCode.Should().Be(HttpStatusCode.Accepted);
            var mail = fx.Email.Messages.Should().ContainSingle().Subject;
            mail.To.Should().Be("admin@example.test");
            var temp = ExtractTemporaryPassword(mail);
            temp.Length.Should().BeGreaterThanOrEqualTo(12);

            await using (var db = fx.Db.Create())
            {
                var row = await db.PasswordResetTokens.OrderByDescending(t => t.Id).FirstAsync(t => t.UserId == fx.Db.AdminId);
                row.TokenHash.Should().NotContain(temp, "temporary passwords are stored hashed");
                (row.ExpiresAt - row.CreatedAt).Should().BeCloseTo(TimeSpan.FromHours(6), TimeSpan.FromSeconds(5));
            }

            var wrong = await PostAsync(Client(), "/api/v1/auth/admin/reset-password",
                new { identifier = "admin", temporaryPassword = "wrong-temp-pw", newPassword = "Brand-New-Pass-77", confirmPassword = "Brand-New-Pass-77" });
            wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var ok = await PostAsync(Client(), "/api/v1/auth/admin/reset-password",
                new { identifier = "admin", temporaryPassword = temp, newPassword = "Brand-New-Pass-77", confirmPassword = "Brand-New-Pass-77" });
            ok.StatusCode.Should().Be(HttpStatusCode.NoContent);

            (await LoginAsync("admin", "Brand-New-Pass-77")).User.Username.Should().Be("admin");
            (await PostAsync(Client(), "/api/v1/auth/login", new { username = "admin", password = ApiFixture.AdminPassword }))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "old password no longer works");

            var reuse = await PostAsync(Client(), "/api/v1/auth/admin/reset-password",
                new { identifier = "admin", temporaryPassword = temp, newPassword = "Another-Pass-88", confirmPassword = "Another-Pass-88" });
            reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "temporary password is single-use");
        }
        finally { await fx.SetPasswordAsync(fx.Db.AdminId, ApiFixture.AdminPassword); }
    }

    [SqlFact]
    public async Task Expired_temporary_password_is_rejected()
    {
        fx.Email.Clear();
        await PostAsync(Client(), "/api/v1/auth/admin/forgot-password", new { identifier = "admin@example.test" });
        var temp = ExtractTemporaryPassword(fx.Email.Messages.Single());

        await using (var db = fx.Db.Create())
        {
            var row = await db.PasswordResetTokens.OrderByDescending(t => t.Id).FirstAsync(t => t.UserId == fx.Db.AdminId);
            row.CreatedAt = DateTime.UtcNow.AddHours(-7);
            row.ExpiresAt = DateTime.UtcNow.AddHours(-1);
            await db.SaveChangesAsync();
        }
        var res = await PostAsync(Client(), "/api/v1/auth/admin/reset-password",
            new { identifier = "admin", temporaryPassword = temp, newPassword = "Brand-New-Pass-77", confirmPassword = "Brand-New-Pass-77" });
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SqlFact]
    public async Task Forgot_password_never_reveals_whether_the_account_exists_and_ignores_members()
    {
        fx.Email.Clear();
        var unknown = await PostAsync(Client(), "/api/v1/auth/admin/forgot-password", new { identifier = "nobody" });
        var member = await PostAsync(Client(), "/api/v1/auth/admin/forgot-password", new { identifier = "member1" });
        unknown.StatusCode.Should().Be(HttpStatusCode.Accepted);
        member.StatusCode.Should().Be(HttpStatusCode.Accepted);
        fx.Email.Messages.Should().BeEmpty("members have no self-service reset (FRD s.2.2)");
    }

    [SqlFact]
    public async Task Weak_new_passwords_are_rejected_with_400()
    {
        var res = await PostAsync(Client(), "/api/v1/auth/admin/reset-password",
            new { identifier = "admin", temporaryPassword = "x", newPassword = "short", confirmPassword = "different" });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // -------------------------------------------------------------------------------- password changes
    [SqlFact]
    public async Task Admin_can_change_own_password_but_members_cannot()
    {
        var member = await LoginAsync("member1", ApiFixture.MemberPassword);
        var denied = await PostAsync(Client(), "/api/v1/auth/change-password",
            new { currentPassword = ApiFixture.MemberPassword, newPassword = "Member-New-9999", confirmPassword = "Member-New-9999" }, member.AccessToken);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        try
        {
            var admin = await LoginAsync("admin", ApiFixture.AdminPassword);
            var bad = await PostAsync(Client(), "/api/v1/auth/change-password",
                new { currentPassword = "wrong-current-1", newPassword = "Admin-Changed-555", confirmPassword = "Admin-Changed-555" }, admin.AccessToken);
            bad.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var ok = await PostAsync(Client(), "/api/v1/auth/change-password",
                new { currentPassword = ApiFixture.AdminPassword, newPassword = "Admin-Changed-555", confirmPassword = "Admin-Changed-555" }, admin.AccessToken);
            ok.StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await PostAsync(Client(), "/api/v1/auth/refresh", new { refreshToken = admin.RefreshToken }))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "sessions are revoked after a password change");
            await LoginAsync("admin", "Admin-Changed-555");
        }
        finally { await fx.SetPasswordAsync(fx.Db.AdminId, ApiFixture.AdminPassword); }
    }

    [SqlFact]
    public async Task Admin_sets_a_member_password_and_the_member_must_sign_in_again()
    {
        var target = await fx.CreateUserAsync(UserRole.Member);
        var oldSession = await LoginAsync(target.Username, ApiFixture.MemberPassword);
        var admin = await LoginAsync("admin", ApiFixture.AdminPassword);

        var res = await PostAsync(Client(), $"/api/v1/users/{target.Id}/set-password", new { newPassword = "Set-By-Admin-4321" }, admin.AccessToken);
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await PostAsync(Client(), "/api/v1/auth/login", new { username = target.Username, password = ApiFixture.MemberPassword }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await LoginAsync(target.Username, "Set-By-Admin-4321");
        (await PostAsync(Client(), "/api/v1/auth/refresh", new { refreshToken = oldSession.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SqlFact]
    public async Task Members_cannot_set_passwords_and_admin_password_is_not_settable_this_way()
    {
        var member = await LoginAsync("member1", ApiFixture.MemberPassword);
        (await PostAsync(Client(), $"/api/v1/users/{fx.Db.MemberId}/set-password", new { newPassword = "Hacker-Pass-1234" }, member.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var admin = await LoginAsync("admin", ApiFixture.AdminPassword);
        (await PostAsync(Client(), $"/api/v1/users/{fx.Db.AdminId}/set-password", new { newPassword = "Whatever-Pass-1234" }, admin.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // -------------------------------------------------------------------------------- authorization
    private async Task<HttpResponseMessage> GetAsync(string url, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client().SendAsync(req);
    }

    [SqlFact]
    public async Task Business_access_follows_membership_and_permission_flags()
    {
        var url = $"/api/v1/businesses/{fx.Db.BusinessId}/my-permissions";

        // Member is assigned but has no permission row yet => access granted by mapping, capability denied.
        var member = await LoginAsync("member1", ApiFixture.MemberPassword);
        (await GetAsync(url, member.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using (var db = fx.Db.Create())
        {
            db.BusinessPermissions.Add(new BusinessPermission
            {
                BusinessId = fx.Db.BusinessId, UserId = fx.Db.MemberId, CanViewBusiness = true, CanViewExpenseTotal = true
            });
            await db.SaveChangesAsync();
        }
        var ok = await GetAsync(url, member.AccessToken);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await ok.Content.ReadFromJsonAsync<BusinessManagement.API.Controllers.MyPermissionsResponse>())!;
        body.Permissions["ViewExpenseTotal"].Should().BeTrue();
        body.Permissions["ViewMemberWiseExpenses"].Should().BeFalse("member-wise visibility is a separate permission");
        body.Permissions["AddSales"].Should().BeFalse();

        // A member NOT assigned to the business cannot even learn that it exists: 404, not 403.
        var outsider = await fx.CreateUserAsync(UserRole.Member);
        var outsiderAuth = await LoginAsync(outsider.Username, ApiFixture.MemberPassword);
        (await GetAsync(url, outsiderAuth.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Admin has full access everywhere.
        var admin = await LoginAsync("admin", ApiFixture.AdminPassword);
        var adminRes = await GetAsync(url, admin.AccessToken);
        adminRes.StatusCode.Should().Be(HttpStatusCode.OK);
        (await adminRes.Content.ReadFromJsonAsync<BusinessManagement.API.Controllers.MyPermissionsResponse>())!
            .Permissions.Values.Should().OnlyContain(v => v);

        // Unknown business => 404 for everyone.
        (await GetAsync("/api/v1/businesses/999999/my-permissions", admin.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
