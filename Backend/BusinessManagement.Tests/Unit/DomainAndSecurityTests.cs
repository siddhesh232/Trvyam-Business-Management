using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Infrastructure.Security;
using FluentAssertions;

namespace BusinessManagement.Tests.Unit;

public class PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_then_verify_succeeds_and_wrong_password_fails()
    {
        var hash = _hasher.Hash("Correct-Horse-9");
        _hasher.Verify("Correct-Horse-9", hash).Should().BeTrue();
        _hasher.Verify("correct-horse-9", hash).Should().BeFalse();
    }

    [Fact]
    public void Hashes_are_salted_and_never_contain_the_password()
    {
        var a = _hasher.Hash("Same-Password-1");
        var b = _hasher.Hash("Same-Password-1");
        a.Should().NotBe(b);
        a.Should().NotContain("Same-Password-1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("v1.600000.@@@.@@@")]
    public void Malformed_hashes_never_verify(string hash) =>
        _hasher.Verify("anything", hash).Should().BeFalse();
}

public class PermissionTests
{
    [Fact]
    public void Every_Permission_enum_value_maps_to_its_own_flag()
    {
        foreach (var permission in Enum.GetValues<Permission>())
        {
            var prop = typeof(BusinessPermission).GetProperty("Can" + permission);
            prop.Should().NotBeNull($"Permission.{permission} must have a Can{permission} column");

            var p = new BusinessPermission();
            if (permission != Permission.ViewBusiness) p.Has(permission).Should().BeFalse("permissions are deny-by-default");
            prop!.SetValue(p, true);
            p.Has(permission).Should().BeTrue();
        }
    }

    [Fact]
    public void Member_wise_expense_visibility_is_independent_of_expense_total()
    {
        var p = new BusinessPermission { CanViewExpenseTotal = true };
        p.Has(Permission.ViewMemberWiseExpenses).Should().BeFalse();
        p.CanViewMemberWiseExpenses = true; p.CanViewExpenseTotal = false;
        p.Has(Permission.ViewExpenseTotal).Should().BeFalse();
    }
}

public class ExpenseTests
{
    [Fact]
    public void Total_is_rounded_to_two_decimals_away_from_zero() =>
        Expense.ComputeTotal(3, 12.345m).Should().Be(37.04m);

    [Fact]
    public void Password_reset_token_expires_after_six_hours_and_is_single_use()
    {
        var now = DateTime.UtcNow;
        var t = new PasswordResetToken { ExpiresAt = now + PasswordResetToken.Lifetime };
        PasswordResetToken.Lifetime.Should().Be(TimeSpan.FromHours(6));
        t.IsUsable(now.AddHours(5.9)).Should().BeTrue();
        t.IsUsable(now.AddHours(6.1)).Should().BeFalse();
        t.UsedAt = now; t.IsUsable(now).Should().BeFalse();
    }
}
