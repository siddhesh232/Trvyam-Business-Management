using BusinessManagement.Application.Adjustments;
using BusinessManagement.Application.Audit;
using BusinessManagement.Application.Common;
using BusinessManagement.Application.Financials;
using BusinessManagement.Application.Returns;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusinessManagement.API.Controllers;

/// <summary>Sales returns (Admin). A credit note is issued automatically when the sale has an issued tax invoice.</summary>
[ApiController, Authorize]
[Route("api/v1/businesses/{businessId:long}/sales/{saleId:long}/returns")]
public sealed class ReturnsController(IReturnService returns) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<SalesReturnDto>> List(long businessId, long saleId, CancellationToken ct) => returns.ListAsync(businessId, saleId, ct);

    [HttpPost]
    public async Task<ActionResult<SalesReturnDto>> Create(long businessId, long saleId, CreateReturnRequest request, CancellationToken ct)
    {
        var created = await returns.CreateAsync(businessId, saleId, request, ct);
        return Created($"api/v1/businesses/{businessId}/sales/{saleId}/returns", created);
    }
}

/// <summary>Other income / losses / accounting adjustments that feed net profit (Admin).</summary>
[ApiController, Authorize]
[Route("api/v1/businesses/{businessId:long}/adjustments")]
public sealed class AdjustmentsController(IAdjustmentService adjustments) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AdjustmentDto>> List(long businessId, [FromQuery] AdjustmentListQuery query, CancellationToken ct) => adjustments.ListAsync(businessId, query, ct);

    [HttpPost]
    public async Task<ActionResult<AdjustmentDto>> Create(long businessId, CreateAdjustmentRequest request, CancellationToken ct)
    {
        var created = await adjustments.CreateAsync(businessId, request, ct);
        return Created($"api/v1/businesses/{businessId}/adjustments/{created.Id}", created);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long businessId, long id, CancellationToken ct)
    {
        await adjustments.DeleteAsync(businessId, id, ct);
        return NoContent();
    }
}

[ApiController, Authorize]
public sealed class DashboardController(IDashboardService dashboards, IReportService reports) : ControllerBase
{
    /// <summary>Per-business dashboard. Sections the caller may not see are returned as null.</summary>
    [HttpGet("api/v1/businesses/{businessId:long}/dashboard")]
    public Task<DashboardDto> Business(long businessId, [FromQuery] DashboardQuery query, CancellationToken ct) => dashboards.GetAsync(businessId, query, ct);

    /// <summary>Admin only: all businesses combined, with a per-business breakdown.</summary>
    [HttpGet("api/v1/dashboard/consolidated"), Authorize(Roles = "Admin")]
    public Task<ConsolidatedDashboardDto> Consolidated([FromQuery] DashboardQuery query, CancellationToken ct) => dashboards.GetConsolidatedAsync(query, ct);

    /// <summary>Daily, weekly, monthly or custom period report (needs the reports permission).</summary>
    [HttpGet("api/v1/businesses/{businessId:long}/reports")]
    public Task<ReportDto> Report(long businessId, [FromQuery] ReportQuery query, CancellationToken ct) => reports.GetAsync(businessId, query, ct);
}

[ApiController, Authorize(Roles = "Admin")]
[Route("api/v1/audit-logs")]
public sealed class AuditLogsController(IAuditLogService audit) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AuditLogDto>> List([FromQuery] AuditLogQuery query, CancellationToken ct) => audit.ListAsync(query, ct);
}
