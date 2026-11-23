using BusinessManagement.Application.Abstractions;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Application.Invoices;

/// <summary>
/// Allocates the next gapless number for (business, financial year, document type). MUST be called inside the same
/// transaction that inserts the document: the UPDATE takes a row lock held until commit, so concurrent issuers queue up,
/// and a rollback also rolls the counter back (no gaps, no duplicates).
/// </summary>
public sealed class InvoiceSequenceAllocator(IAppDbContext db)
{
    public async Task<int> NextAsync(long businessId, string financialYear, InvoiceDocType docType, CancellationToken ct)
    {
        if (!await db.InvoiceSequences.AnyAsync(s => s.BusinessId == businessId && s.FinancialYear == financialYear && s.DocType == docType, ct))
        {
            db.InvoiceSequences.Add(new InvoiceSequence { BusinessId = businessId, FinancialYear = financialYear, DocType = docType, LastNumber = 0 });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { db.ChangeTracker.Clear(); }   // lost a race to create the row: use the winner's
        }
        await db.InvoiceSequences.Where(s => s.BusinessId == businessId && s.FinancialYear == financialYear && s.DocType == docType)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastNumber, x => x.LastNumber + 1), ct);
        return await db.InvoiceSequences.AsNoTracking()
            .Where(s => s.BusinessId == businessId && s.FinancialYear == financialYear && s.DocType == docType)
            .Select(s => s.LastNumber).FirstAsync(ct);
    }
}
