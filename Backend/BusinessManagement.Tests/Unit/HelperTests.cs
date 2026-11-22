using BusinessManagement.Application.Businesses;
using BusinessManagement.Application.Common;
using BusinessManagement.Domain.Enums;
using FluentAssertions;

namespace BusinessManagement.Tests.Unit;

public class GstinTests
{
    [Theory]
    [InlineData("27AAPFU0939F1ZV")]   // widely published sample GSTIN
    [InlineData("27ABCDE1234F1Z0")]
    [InlineData("29ABCDE1234F1ZW")]
    [InlineData("07AAACG1234F1Z6")]
    public void Valid_gstins_pass(string g) => Gstin.IsValid(g).Should().BeTrue();

    [Theory]
    [InlineData("27AAPFU0939F1ZX")]   // bad checksum
    [InlineData("27AAPFU0939F1AV")]   // 14th char must be Z
    [InlineData("27AAPFU0939F1Z")]    // too short
    [InlineData("00AAPFU0939F1ZV")]   // invalid state code
    [InlineData("")]
    [InlineData("not a gstin!!!!")]
    public void Invalid_gstins_fail(string g) => Gstin.IsValid(g).Should().BeFalse();

    [Fact]
    public void Lowercase_input_is_normalised_and_state_code_is_the_first_two_digits()
    {
        Gstin.IsValid("27aapfu0939f1zv").Should().BeTrue();
        Gstin.StateCode("27AAPFU0939F1ZV").Should().Be("27");
    }
}

public class MobileNumberTests
{
    [Theory]
    [InlineData("+91 98765-43210", "9876543210")]
    [InlineData("09876543210", "9876543210")]
    [InlineData("919876543210", "9876543210")]
    [InlineData("98765 43210", "9876543210")]
    public void Normalizes_common_indian_formats(string input, string expected) =>
        MobileNumber.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData("12345", false)]
    [InlineData("abcdefghij", false)]
    [InlineData("", false)]
    [InlineData("9876543210", true)]
    public void Validates_length_and_digits(string input, bool valid) => MobileNumber.IsValid(input).Should().Be(valid);
}

public class PermissionDtoTests
{
    [Fact]
    public void Dto_exposes_every_permission_in_the_enum()
    {
        var props = typeof(BusinessPermissionsDto).GetProperties().Select(p => p.Name).ToHashSet();
        foreach (var p in Enum.GetValues<Permission>()) props.Should().Contain("Can" + p);
        props.Count.Should().Be(Enum.GetValues<Permission>().Length, "no stray flags");
    }
}
