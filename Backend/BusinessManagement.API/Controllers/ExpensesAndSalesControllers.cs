using BusinessManagement.Application.Common;
using BusinessManagement.Application.Expenses;
using BusinessManagement.Application.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusinessManagement.API.Controllers;

/// <remarks>Permissions are enforced inside the services (they need per-record rules), always on the server.</remarks>
[ApiController, Authorize]
[Route("api/v1/businesses/{businessId:long}/expenses")]
public sealed class ExpensesController(IExpenseService expenses) : ControllerBase
{
    /// <summary>Expense records. Members without the member-wise permission see only their own records.</summary>
    [HttpGet]
    public Task<PagedResult<ExpenseDto>> List(long businessId, [FromQuery] ExpenseListQuery query, CancellationToken ct) => expenses.ListAsync(businessId, query, ct);

    [HttpPost]
    public async Task<ActionResult<ExpenseDto>> Create(long businessId, SaveExpenseRequest request, CancellationToken ct)
    {
        var created = await expenses.CreateAsync(businessId, request, ct);
        return Created($"api/v1/businesses/{businessId}/expenses/{created.Id}", created);
    }

    [HttpPut("{id:long}")]
    public Task<ExpenseDto> Update(long businessId, long id, SaveExpenseRequest request, CancellationToken ct) => expenses.UpdateAsync(businessId, id, request, ct);

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long businessId, long id, CancellationToken ct)
    {
        await expenses.DeleteAsync(businessId, id, ct);
        return NoContent();
    }

    /// <summary>Total business expenses for the period (needs the expense-total permission).</summary>
    [HttpGet("summary")]
    public Task<ExpenseSummaryDto> Summary(long businessId, [FromQuery] ExpenseRangeQuery query, CancellationToken ct) => expenses.SummaryAsync(businessId, query, ct);

    /// <summary>Per-member totals for the same period/filters; reconciles with the summary. Needs the dedicated member-wise permission.</summary>
    [HttpGet("by-member")]
    public Task<ExpenseByMemberDto> ByMember(long businessId, [FromQuery] ExpenseByMemberQuery query, CancellationToken ct) => expenses.ByMemberAsync(businessId, query, ct);
}

[ApiController, Authorize]
[Route("api/v1/businesses/{businessId:long}/sales")]
public sealed class SalesController(ISalesService sales) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<SalesListItemDto>> List(long businessId, [FromQuery] SalesListQuery query, CancellationToken ct) => sales.ListAsync(businessId, query, ct);

    [HttpGet("{id:long}")]
    public Task<SalesOrderDto> Get(long businessId, long id, CancellationToken ct) => sales.GetAsync(businessId, id, ct);

    /// <summary>Creates a multi-product order from the catalogue. Totals, discounts and GST are calculated by the server.</summary>
    [HttpPost]
    public async Task<ActionResult<SalesOrderDto>> Create(long businessId, CreateSaleRequest request, CancellationToken ct)
    {
        var created = await sales.CreateAsync(businessId, request, ct);
        return CreatedAtAction(nameof(Get), new { businessId, id = created.Id }, created);
    }

    [HttpPut("{id:long}")]
    public Task<SalesOrderDto> Update(long businessId, long id, UpdateSaleRequest request, CancellationToken ct) => sales.UpdateAsync(businessId, id, request, ct);

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long businessId, long id, CancellationToken ct)
    {
        await sales.DeleteAsync(businessId, id, ct);
        return NoContent();
    }
}
