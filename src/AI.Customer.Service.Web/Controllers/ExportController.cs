using System.Text.Json;
using AI.Customer.Service.Common;
using AI.Customer.Service.Service;
using AI.Customer.Service.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace AI.Customer.Service.Web.Controllers;

/// <summary>聊天及评价的分页 JSON 下载和客服运行指标。</summary>
/// <param name="request">组织权限。</param>
/// <param name="access">助手归属核验。</param>
/// <param name="exports">安全导出应用端口。</param>
/// <param name="metrics">完整窗口及当前积压聚合端口。</param>
public sealed class ExportController(RequestAccess request, AccessService access, ExportService exports, MetricsService metrics) : CustomerServiceControllerBase(request)
{
    /// <summary>按组织管理权限导出，不暴露认证信息或邮箱。</summary>
    [HttpGet("exports/conversations")]
    public async Task<IActionResult> Export(string topOrgId, string assistantConfigId, CancellationToken ct, int page = 1, int size = 20)
    {
        await RequestAccess.ManagerAsync(topOrgId, ct);
        access.RequireAssistant(Actor, topOrgId, assistantConfigId);
        var data = await exports.ExportAsync(Actor, topOrgId, assistantConfigId, page, size, ct);
        return File(JsonSerializer.SerializeToUtf8Bytes(Success(data), JsonDefaults.Options), "application/json", $"customer-service-{page}.json");
    }

    /// <summary>正式客服读取有范围的可靠任务指标。</summary>
    [HttpGet("agents/metrics")]
    public async Task<object> Metrics(string topOrgId, string assistantConfigId, CancellationToken ct)
    {
        await RequestAccess.AgentListAsync(topOrgId, assistantConfigId, ct);
        return Success(await metrics.ReadAsync(Actor, topOrgId, assistantConfigId, ct));
    }

    /// <summary>正式客服认证下输出同口径 Prometheus gauge，不携带会话或账号标签。</summary>
    [HttpGet("agents/metrics/prometheus")]
    public async Task<IActionResult> Prometheus(string topOrgId, string assistantConfigId, CancellationToken ct)
    {
        await RequestAccess.AgentListAsync(topOrgId, assistantConfigId, ct);
        return Content(MetricsService.Prometheus(await metrics.ReadAsync(Actor, topOrgId, assistantConfigId, ct)), "text/plain; version=0.0.4; charset=utf-8");
    }
}
