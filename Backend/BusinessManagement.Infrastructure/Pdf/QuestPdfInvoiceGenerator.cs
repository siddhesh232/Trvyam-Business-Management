using System.Globalization;
using BusinessManagement.Application.Invoices;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BusinessManagement.Infrastructure.Pdf;

/// <summary>
/// Renders the frozen invoice snapshot as an A4 PDF. Uses QuestPDF under its Community licence
/// (free for organisations below US$1M annual revenue - confirm this applies to you before production use).
/// </summary>
public sealed class QuestPdfInvoiceGenerator : IInvoicePdfGenerator
{
    private static readonly CultureInfo Inr = new("en-IN");
    static QuestPdfInvoiceGenerator() => QuestPDF.Settings.License = LicenseType.Community;

    private static string Money(decimal v) => v.ToString("N2", Inr);

    public byte[] Generate(InvoiceSnapshot inv, bool cancelled)
    {
        var gstColumns = inv.IsInterState ? new[] { "IGST" } : new[] { "CGST", "SGST" };

        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(24);
            page.DefaultTextStyle(t => t.FontSize(9));

            page.Header().Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(inv.Seller.Name).FontSize(15).Bold();
                        if (!string.IsNullOrWhiteSpace(inv.Seller.Address)) c.Item().Text(inv.Seller.Address);
                        c.Item().Text($"GSTIN: {inv.Seller.Gstin}   State code: {inv.Seller.StateCode}");
                    });
                    row.ConstantItem(220).AlignRight().Column(c =>
                    {
                        c.Item().Text(inv.DocTitle).FontSize(15).Bold();
                        c.Item().Text($"No: {inv.InvoiceNumber}");
                        c.Item().Text($"Date: {inv.InvoiceDate:dd-MMM-yyyy}");
                        if (!string.IsNullOrWhiteSpace(inv.OriginalInvoiceNumber)) c.Item().Text($"Against invoice: {inv.OriginalInvoiceNumber}");
                        if (cancelled) c.Item().Text("*** CANCELLED ***").Bold().FontColor(Colors.Red.Medium);
                    });
                });
                col.Item().PaddingVertical(6).LineHorizontal(1);
            });

            page.Content().Column(col =>
            {
                col.Spacing(8);
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Bill to").Bold();
                        c.Item().Text(inv.Buyer.Name);
                        if (!string.IsNullOrWhiteSpace(inv.Buyer.Address)) c.Item().Text(inv.Buyer.Address);
                        if (!string.IsNullOrWhiteSpace(inv.Buyer.Mobile)) c.Item().Text($"Mobile: {inv.Buyer.Mobile}");
                        if (!string.IsNullOrWhiteSpace(inv.Buyer.Gstin)) c.Item().Text($"GSTIN: {inv.Buyer.Gstin}");
                    });
                    row.RelativeItem().AlignRight().Column(c =>
                    {
                        c.Item().Text($"Place of supply (state code): {inv.PlaceOfSupplyState}");
                        c.Item().Text(inv.IsInterState ? "Inter-state supply (IGST)" : "Intra-state supply (CGST + SGST)");
                    });
                });

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(22); c.RelativeColumn(4); c.ConstantColumn(48); c.ConstantColumn(40); c.ConstantColumn(36);
                        c.ConstantColumn(56); c.ConstantColumn(50); c.ConstantColumn(62); c.ConstantColumn(34);
                        foreach (var _ in gstColumns) c.ConstantColumn(54);
                        c.ConstantColumn(66);
                    });
                    table.Header(h =>
                    {
                        foreach (var title in new[] { "#", "Description", "HSN", "Qty", "Unit", "Rate", "Disc.", "Taxable", "GST %" }) h.Cell().Element(Head).Text(title);
                        foreach (var g in gstColumns) h.Cell().Element(Head).AlignRight().Text(g);
                        h.Cell().Element(Head).AlignRight().Text("Total");
                    });
                    foreach (var l in inv.Lines)
                    {
                        table.Cell().Element(Body).Text(l.No.ToString());
                        table.Cell().Element(Body).Text(l.Description);
                        table.Cell().Element(Body).Text(l.Hsn ?? "");
                        table.Cell().Element(Body).AlignRight().Text(l.Quantity.ToString("0.###", Inr));
                        table.Cell().Element(Body).Text(l.Unit);
                        table.Cell().Element(Body).AlignRight().Text(Money(l.UnitPrice));
                        table.Cell().Element(Body).AlignRight().Text(Money(l.Discount));
                        table.Cell().Element(Body).AlignRight().Text(Money(l.Taxable));
                        table.Cell().Element(Body).AlignRight().Text(l.GstRate.ToString("0.##", Inr));
                        if (inv.IsInterState) table.Cell().Element(Body).AlignRight().Text(Money(l.Igst));
                        else { table.Cell().Element(Body).AlignRight().Text(Money(l.Cgst)); table.Cell().Element(Body).AlignRight().Text(Money(l.Sgst)); }
                        table.Cell().Element(Body).AlignRight().Text(Money(l.Total));
                    }
                });

                col.Item().AlignRight().Width(260).Column(c =>
                {
                    Row(c, "Subtotal", inv.Subtotal);
                    Row(c, "Discount", -inv.TotalDiscount);
                    Row(c, "Taxable value", inv.Taxable);
                    if (inv.IsInterState) Row(c, "IGST", inv.Igst);
                    else { Row(c, "CGST", inv.Cgst); Row(c, "SGST", inv.Sgst); }
                    c.Item().PaddingTop(2).LineHorizontal(1);
                    c.Item().Row(r =>
                    {
                        r.RelativeItem().Text("Grand total").Bold().FontSize(11);
                        r.ConstantItem(110).AlignRight().Text("Rs. " + Money(inv.GrandTotal)).Bold().FontSize(11);
                    });
                });

                col.Item().PaddingTop(10).Text("This is a computer-generated invoice.").FontSize(8).FontColor(Colors.Grey.Darken1);
            });

            page.Footer().AlignCenter().Text(t => { t.Span("Page ").FontSize(8); t.CurrentPageNumber().FontSize(8); t.Span(" of ").FontSize(8); t.TotalPages().FontSize(8); });
        })).GeneratePdf();

        static IContainer Head(IContainer c) => c.Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(3).DefaultTextStyle(t => t.Bold());
        static IContainer Body(IContainer c) => c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(3);
        static void Row(ColumnDescriptor c, string label, decimal value) =>
            c.Item().Row(r => { r.RelativeItem().Text(label); r.ConstantItem(110).AlignRight().Text(Money(value)); });
    }
}
