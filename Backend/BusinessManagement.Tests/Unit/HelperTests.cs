using BusinessManagement.Application.Businesses;
using BusinessManagement.Application.Common;
using BusinessManagement.Domain.Billing;
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

public class SalesCalculatorTests
{
    private static SaleLineInput L(decimal q, decimal price, decimal itemDiscount, decimal rate) => new(q, price, itemDiscount, rate);
    private static SaleTotals Calc(decimal orderDiscount, params SaleLineInput[] lines) => SalesCalculator.Calculate(lines, orderDiscount);

    [Fact]
    public void Plain_line_adds_gst_on_top_of_the_exclusive_price()
    {
        var t = Calc(0, L(2, 500m, 0, 18m));
        (t.Subtotal, t.Discount, t.Taxable, t.Gst, t.Final).Should().Be((1000m, 0m, 1000m, 180m, 1180m));
    }

    [Fact]
    public void Item_discount_reduces_the_taxable_value_before_gst()
    {
        var t = Calc(0, L(2, 500m, 100m, 18m));
        (t.Taxable, t.Gst, t.Final).Should().Be((900m, 162m, 1062m));
    }

    [Fact]
    public void Order_discount_is_spread_pro_rata_and_gst_is_charged_on_the_discounted_values()
    {
        var t = Calc(30, L(1, 100m, 0, 18m), L(1, 200m, 0, 5m));
        t.Lines.Select(l => l.Discount).Should().Equal(10m, 20m);
        (t.Taxable, t.Gst, t.Final).Should().Be((270m, 25.2m, 295.2m));
    }

    [Fact]
    public void Discount_shares_always_add_up_to_exactly_the_discount()
    {
        var t = Calc(10, L(1, 100m, 0, 0), L(1, 100m, 0, 0), L(1, 100m, 0, 0));
        t.Lines.Sum(l => l.Discount).Should().Be(10m);
        t.Lines.Select(l => l.Discount).Should().BeEquivalentTo(new[] { 3.34m, 3.33m, 3.33m });
    }

    [Theory]
    [InlineData(0.01, 3)]
    [InlineData(99.99, 7)]
    [InlineData(1234.56, 5)]
    public void Invariants_hold_for_awkward_amounts(double discount, int lineCount)
    {
        var lines = Enumerable.Range(1, lineCount).Select(i => new SaleLineInput(i, 33.33m * i, 0.17m, i % 2 == 0 ? 18m : 5m)).ToArray();
        var gross = lines.Sum(l => SalesCalculator.Round(l.Quantity * l.UnitPrice) - l.ItemDiscount);
        var d = (decimal)discount > gross ? 0m : (decimal)discount;
        var t = Calc(d, lines);
        t.Lines.Sum(l => l.Discount).Should().Be(t.Discount);
        t.Final.Should().Be(t.Taxable + t.Gst);
        foreach (var l in t.Lines)
        {
            (l.Gross - l.Discount).Should().Be(l.Taxable);
            l.Total.Should().Be(l.Taxable + l.Gst);
            l.Taxable.Should().BeGreaterThanOrEqualTo(0m);
        }
    }

    [Fact]
    public void Gst_rounds_half_away_from_zero_to_the_paisa() =>
        Calc(0, L(1, 10.10m, 0, 5m)).Gst.Should().Be(0.51m);

    [Fact]
    public void A_full_discount_leaves_nothing_to_tax() =>
        Calc(150, L(1, 100m, 0, 18m), L(1, 50m, 0, 18m)).Final.Should().Be(0m);

    [Fact]
    public void Invalid_amounts_are_rejected()
    {
        FluentActions.Invoking(() => Calc(0)).Should().Throw<BusinessManagement.Domain.Exceptions.DomainException>();
        FluentActions.Invoking(() => Calc(0, L(0, 10m, 0, 5m))).Should().Throw<BusinessManagement.Domain.Exceptions.DomainException>();
        FluentActions.Invoking(() => Calc(0, L(1, 10m, 11m, 5m))).Should().Throw<BusinessManagement.Domain.Exceptions.DomainException>("item discount above value");
        FluentActions.Invoking(() => Calc(11, L(1, 10m, 0, 5m))).Should().Throw<BusinessManagement.Domain.Exceptions.DomainException>("order discount above value");
        FluentActions.Invoking(() => Calc(-1, L(1, 10m, 0, 5m))).Should().Throw<BusinessManagement.Domain.Exceptions.DomainException>();
    }
}

public class PaymentMathTests
{
    [Theory]
    [InlineData(1000, 0, PaymentStatus.Unpaid)]
    [InlineData(1000, 0.01, PaymentStatus.PartiallyPaid)]
    [InlineData(1000, 999.99, PaymentStatus.PartiallyPaid)]
    [InlineData(1000, 1000, PaymentStatus.Paid)]
    [InlineData(0, 0, PaymentStatus.Paid)]          // nothing due (fully returned) counts as paid
    [InlineData(400, 600, PaymentStatus.Paid)]      // over-received after a return: paid, refund owed
    public void Status_is_calculated_from_received_vs_due(double final, double received, PaymentStatus expected) =>
        PaymentMath.StatusFor((decimal)final, (decimal)received).Should().Be(expected);
}
