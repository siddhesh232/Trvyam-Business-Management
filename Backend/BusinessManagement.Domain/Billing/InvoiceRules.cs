namespace BusinessManagement.Domain.Billing;

/// <summary>Pure GST invoice rules: Indian financial year (1 Apr - 31 Mar), number format, CGST/SGST vs IGST split.</summary>
public static class InvoiceRules
{
    /// <summary>"2026-27" for any date from 2026-04-01 to 2027-03-31.</summary>
    public static string FinancialYear(DateOnly date)
    {
        var start = date.Month >= 4 ? date.Year : date.Year - 1;
        return $"{start}-{(start + 1) % 100:D2}";
    }

    /// <summary>Format: PREFIX/26-27/00001. GST rules cap an invoice number at 16 characters
    /// (letters, digits, '/' and '-'); "INV/26-27/00001" is 15, which allows up to 999,999 per year.</summary>
    public static string FormatNumber(string prefix, string financialYear, int sequence)
    {
        if (sequence < 1 || sequence > 999_999) throw new ArgumentOutOfRangeException(nameof(sequence));
        var number = $"{prefix}/{financialYear[2..4]}-{financialYear[5..]}/{sequence:D5}";
        if (number.Length > 16) throw new InvalidOperationException("Invoice number exceeds 16 characters.");
        return number;
    }

    /// <summary>Intra-state supply: GST is split into CGST + SGST; inter-state: all IGST.
    /// CGST + SGST always equals the line's GST exactly; if the GST has an odd paisa, SGST carries it.</summary>
    public static (decimal Cgst, decimal Sgst, decimal Igst) SplitGst(decimal gst, bool interState)
    {
        if (interState) return (0m, 0m, gst);
        var cgst = Math.Floor(gst * 50m) / 100m;
        return (cgst, gst - cgst, 0m);
    }

    /// <summary>Indian Standard Time date for "today" (invoices are dated in the business's local day).</summary>
    public static DateOnly TodayIst(DateTime utcNow) => DateOnly.FromDateTime(utcNow.AddHours(5.5));
}
