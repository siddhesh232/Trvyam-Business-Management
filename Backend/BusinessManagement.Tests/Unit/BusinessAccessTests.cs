using BusinessManagement.Application.Authorization;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using FluentAssertions;

namespace BusinessManagement.Tests.Unit;

public class BusinessAccessTests
{
    private static BusinessAccess Member(BusinessPermission p, long uid = 5) => new(1, uid, UserRole.Member, p);

    [Fact]
    public void Admin_can_do_everything_and_modify_any_record()
    {
        var a = new BusinessAccess(1, 1, UserRole.Admin, null);
        Enum.GetValues<Permission>().Should().OnlyContain(p => a.Can(p));
        a.CanModifyOwn(999, Permission.EditOwnExpenses).Should().BeTrue();
    }

    [Fact]
    public void Member_may_modify_only_own_records_and_only_with_the_flag()
    {
        var a = Member(new BusinessPermission { CanEditOwnExpenses = true }, uid: 5);
        a.CanModifyOwn(5, Permission.EditOwnExpenses).Should().BeTrue();
        a.CanModifyOwn(6, Permission.EditOwnExpenses).Should().BeFalse("never another member's record");
        Member(new BusinessPermission(), 5).CanModifyOwn(5, Permission.EditOwnExpenses).Should().BeFalse();
    }

    [Fact]
    public void Member_wise_expense_visibility_does_not_grant_edit_rights()
    {
        var a = Member(new BusinessPermission { CanViewMemberWiseExpenses = true });
        a.Can(Permission.ViewMemberWiseExpenses).Should().BeTrue();
        a.CanModifyOwn(6, Permission.EditOwnExpenses).Should().BeFalse();
        a.Can(Permission.EditOwnExpenses).Should().BeFalse();
    }

    [Fact]
    public void Employee_is_view_only_even_if_write_flags_are_set()
    {
        var all = new BusinessPermission();
        foreach (var p in Enum.GetValues<Permission>()) typeof(BusinessPermission).GetProperty("Can" + p)!.SetValue(all, true);
        var e = new BusinessAccess(1, 9, UserRole.Employee, all);
        foreach (var p in Enum.GetValues<Permission>())
            e.Can(p).Should().Be(p.ToString().StartsWith("View"), $"{p}");
        e.CanModifyOwn(9, Permission.EditOwnSales).Should().BeFalse();
    }

    [Fact]
    public void No_permission_row_means_no_capabilities() =>
        Enum.GetValues<Permission>().Should().OnlyContain(p => !new BusinessAccess(1, 5, UserRole.Member, null).Can(p));
}
