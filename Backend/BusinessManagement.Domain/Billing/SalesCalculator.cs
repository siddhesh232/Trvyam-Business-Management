using BusinessManagement.Domain.Enums;
using BusinessManagement.Domain.Exceptions;

namespace BusinessManagement.Domain.Billing;

public sealed record SaleLineInput(decimal Quantity, decimal UnitPrice, decimal ItemDiscount, decimal GstRate);

/// <param name="Discount">Item-level discount PLUS this line's share of the order-level discount, so Gross - Discount = Taxable.</param>
public sealed record SaleLineResult(decimal Gross, decimal Discount, decimal Taxable, decimal Gst, decimal Total);

/// <param name="Subtotal">Sum of line gross values (before any discount or tax).</param>
/// <param name="Discount">Total discount (item-level + order-level).</param>
public sealed record SaleTotals(decimal Subtotal, decimal Discount, decimal Taxable, decimal Gst, decimal Final, IReadOnlyList<SaleLineResult> Lines);

/// <summary>
/// Server-side sales arithmetic (FRD s.9). Prices are GST-EXCLUSIVE. Order of operations:
/// gross = qty x price -> minus item discount -> minus allocated order discount = taxable value -> GST on taxable value.
/// All money is decimal, rounded to 2 places away from zero per line; order totals are sums of rounded lines,
/// so they always reconcile exactly with the stored items.
/// </summary>
public static class SalesCalculator
{
    public static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public static SaleTotals Calculate(IReadOnlyList<SaleLineInput> lines, decimal orderDiscount)
    {
        if (lines.Count == 0) throw new DomainException("A sale needs at least one item.", "invalid_amount");
        if (orderDiscount < 0) throw new DomainException("Discount cannot be negative.", "invalid_amount");
        orderDiscount = Round(orderDiscount);

        var gross = new decimal[lines.Count];
        var net = new decimal[lines.Count];            // after item discount
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l.Quantity <= 0) throw new DomainException("Quantity must be greater than zero.", "invalid_amount");
            if (l.UnitPrice < 0 || l.ItemDiscount < 0) throw new DomainException("Price and discount cannot be negative.", "invalid_amount");
            if (l.GstRate < 0 || l.GstRate > 100) throw new DomainException("GST rate must be between 0 and 100.", "invalid_amount");
            gross[i] = Round(l.Quantity * l.UnitPrice);
            var d = Round(l.ItemDiscount);
            if (d > gross[i]) throw new DomainException("An item discount cannot exceed the item value.", "invalid_amount");
            net[i] = gross[i] - d;
        }

        var sumNet = net.Sum();
        if (orderDiscount > sumNet) throw new DomainException("Order discount cannot exceed the discounted order value.", "invalid_amount");
        var alloc = Allocate(orderDiscount, net);

        var results = new List<SaleLineResult>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var itemDiscount = Round(lines[i].ItemDiscount);
            var taxable = net[i] - alloc[i];
            var gst = Round(taxable * lines[i].GstRate / 100m);
            results.Add(new SaleLineResult(gross[i], itemDiscount + alloc[i], taxable, gst, taxable + gst));
        }

        var subtotal = results.Sum(r => r.Gross);
        var discount = results.Sum(r => r.Discount);
        var taxableTotal = results.Sum(r => r.Taxable);
        var gstTotal = results.Sum(r => r.Gst);
        return new SaleTotals(subtotal, discount, taxableTotal, gstTotal, taxableTotal + gstTotal, results);
    }

    /// <summary>Splits <paramref name="discount"/> across lines in proportion to their value, to the paisa, using the
    /// largest-remainder method so the shares add up to exactly the discount and never exceed a line's value.</summary>
    internal static decimal[] Allocate(decimal discount, decimal[] net)
    {
        var shares = new decimal[net.Length];
        if (discount == 0) return shares;

        var totalCents = net.Sum(n => (long)(n * 100m));
        var discountCents = (long)(discount * 100m);
        var floors = new long[net.Length];
        var fractions = new decimal[net.Length];
        for (var i = 0; i < net.Length; i++)
        {
            var exact = (decimal)discountCents * (long)(net[i] * 100m) / totalCents;
            floors[i] = (long)Math.Floor(exact);
            fractions[i] = exact - floors[i];
        }

        var remaining = discountCents - floors.Sum();
        foreach (var i in Enumerable.Range(0, net.Length).OrderByDescending(i => fractions[i]).ThenBy(i => i).Take((int)remaining))
            floors[i]++;

        for (var i = 0; i < net.Length; i++) shares[i] = floors[i] / 100m;
        return shares;
    }
}

public static class PaymentMath
{
    /// <summary>Unpaid: nothing received. Paid: full amount received. Otherwise partially paid (FRD s.10).</summary>
    public static PaymentStatus StatusFor(decimal finalAmount, decimal totalReceived) =>
        totalReceived <= 0 ? PaymentStatus.Unpaid : totalReceived >= finalAmount ? PaymentStatus.Paid : PaymentStatus.PartiallyPaid;

    public static decimal Outstanding(decimal finalAmount, decimal totalReceived) => finalAmount - totalReceived;
}
