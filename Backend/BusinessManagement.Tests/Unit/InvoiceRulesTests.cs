using BusinessManagement.Application.Invoices;
using BusinessManagement.Domain.Billing;
using BusinessManagement.Infrastructure.Pdf;
using FluentAssertions;

namespace BusinessManagement.Tests.Unit;

public class InvoiceRulesTests
{
    [Theory]
    [InlineData("2026-03-31", "2025-26")]
    [InlineData("2026-04-01", "2026-27")]
    [InlineData("2027-03-31", "2026-27")]
    [InlineData("2027-04-01", "2027-28")]
    public void Financial_year_runs_april_to_march(string date, string expected) =>
        InvoiceRules.FinancialYear(DateOnly.Parse(date)).Should().Be(expected);

    [Fact]
    public void Numbers_follow_the_gst_format_and_length_limit()
    {
        InvoiceRules.FormatNumber("INV", "2026-27", 1).Should().Be("INV/26-27/00001");
        InvoiceRules.FormatNumber("INV", "2026-27", 999_999).Length.Should().BeLessThanOrEqualTo(16);
        FluentActions.Invoking(() => InvoiceRules.FormatNumber("INV", "2026-27", 1_000_000)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => InvoiceRules.FormatNumber("INV", "2026-27", 0)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(180, false, 90, 90, 0)]
    [InlineData(0.51, false, 0.25, 0.26, 0)]
    [InlineData(157.58, false, 78.79, 78.79, 0)]
    [InlineData(180, true, 0, 0, 180)]
    public void Gst_split_always_adds_up(double gst, bool inter, double cgst, double sgst, double igst)
    {
        var (c, s, i) = InvoiceRules.SplitGst((decimal)gst, inter);
        (c, s, i).Should().Be(((decimal)cgst, (decimal)sgst, (decimal)igst));
        (c + s + i).Should().Be((decimal)gst);
    }

    [Fact]
    public void Ist_date_rolls_over_before_utc()
    {
        // 19:00 UTC on 31 March is already 1 April in India: a different financial year.
        var d = InvoiceRules.TodayIst(new DateTime(2026, 3, 31, 19, 0, 0, DateTimeKind.Utc));
        InvoiceRules.FinancialYear(d).Should().Be("2026-27");
    }

    [Fact]
    public void Pdf_generator_renders_intra_and_inter_state_and_cancelled_invoices()
    {
        InvoiceSnapshot Make(bool inter) => new("TAX INVOICE", "INV/26-27/00001", new DateOnly(2026, 11, 23),
            new InvoiceParty("Seller Ltd", "27ABCDE1234F1Z0", "1 Main Rd, Pune", "27", null),
            new InvoiceParty("Buyer", inter ? "29ABCDE1234F1ZW" : null, "Addr", inter ? "29" : "27", "9876543210"),
            inter ? "29" : "27", inter, [new InvoiceLine(1, "Widget", "8471", 2, "piece", 500m, 0m, 1000m, 18m, inter ? 0 : 90m, inter ? 0 : 90m, inter ? 180m : 0, 1180m)],
            1000m, 0m, 1000m, inter ? 0 : 90m, inter ? 0 : 90m, inter ? 180m : 0, 1180m, 1, new DateOnly(2026, 11, 23));

        var gen = new QuestPdfInvoiceGenerator();
        foreach (var (inter, cancelled) in new[] { (false, false), (true, false), (false, true) })
        {
            var bytes = gen.Generate(Make(inter), cancelled);
            System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
            bytes.Length.Should().BeGreaterThan(1500);
        }
    }
}
