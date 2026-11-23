using BusinessManagement.Application.Common;
using BusinessManagement.Application.Invoices;
using BusinessManagement.Application.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusinessManagement.API.Controllers;

[ApiController, Authorize]
[Route("api/v1/businesses/{businessId:long}/sales/{saleId:long}/payments")]
public sealed class PaymentsController(IPaymentService payments) : ControllerBase
{
    /// <summary>Full payment history of a sale, with calculated received/outstanding/status. Reversed payments are included.</summary>
    [HttpGet]
    public Task<PaymentHistoryDto> History(long businessId, long saleId, CancellationToken ct) => payments.HistoryAsync(businessId, saleId, ct);

    /// <summary>Records a payment. Rejected (422) if it would exceed the outstanding amount. Never creates an invoice.</summary>
    [HttpPost]
    public async Task<ActionResult<PaymentHistoryDto>> Record(long businessId, long saleId, RecordPaymentRequest request, CancellationToken ct)
    {
        var history = await payments.RecordAsync(businessId, saleId, request, ct);
        return Created($"api/v1/businesses/{businessId}/sales/{saleId}/payments", history);
    }

    [HttpPut("{paymentId:long}")]
    public Task<PaymentHistoryDto> Update(long businessId, long saleId, long paymentId, UpdatePaymentRequest request, CancellationToken ct) =>
        payments.UpdateAsync(businessId, saleId, paymentId, request, ct);

    /// <summary>Reverses (voids) a payment with a reason. The record is kept for audit and shown in the history.</summary>
    [HttpPost("{paymentId:long}/reverse")]
    public Task<PaymentHistoryDto> Reverse(long businessId, long saleId, long paymentId, ReversePaymentRequest request, CancellationToken ct) =>
        payments.ReverseAsync(businessId, saleId, paymentId, request, ct);
}

[ApiController, Authorize]
[Route("api/v1/businesses/{businessId:long}")]
public sealed class InvoicesController(IInvoiceService invoices) : ControllerBase
{
    /// <summary>Issues the GST tax invoice for a sale (one active invoice per sale; sequential numbering per business and financial year).</summary>
    [HttpPost("sales/{saleId:long}/invoice")]
    public async Task<ActionResult<InvoiceDto>> Issue(long businessId, long saleId, CancellationToken ct)
    {
        var inv = await invoices.IssueAsync(businessId, saleId, ct);
        return CreatedAtAction(nameof(Get), new { businessId, invoiceId = inv.Id }, inv);
    }

    [HttpGet("invoices")]
    public Task<PagedResult<InvoiceListItemDto>> List(long businessId, [FromQuery] InvoiceListQuery query, CancellationToken ct) => invoices.ListAsync(businessId, query, ct);

    [HttpGet("invoices/{invoiceId:long}")]
    public Task<InvoiceDto> Get(long businessId, long invoiceId, CancellationToken ct) => invoices.GetAsync(businessId, invoiceId, ct);

    /// <summary>Downloadable/printable PDF generated from the frozen invoice snapshot.</summary>
    [HttpGet("invoices/{invoiceId:long}/pdf")]
    public async Task<IActionResult> Pdf(long businessId, long invoiceId, CancellationToken ct)
    {
        var pdf = await invoices.GetPdfAsync(businessId, invoiceId, ct);
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }

    /// <summary>Admin only. Cancels an issued invoice (the number stays in the series); the sale can then be corrected and re-invoiced.</summary>
    [HttpPost("invoices/{invoiceId:long}/cancel")]
    public Task<InvoiceDto> Cancel(long businessId, long invoiceId, CancelInvoiceRequest request, CancellationToken ct) => invoices.CancelAsync(businessId, invoiceId, request, ct);
}
