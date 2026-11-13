using AI.Customer.Service.Api.Contracts;
using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Service;
using AI.Customer.Service.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace AI.Customer.Service.Web.Controllers;

/// <summary>在线展示、同组改派和系统失败恢复 HTTP Adapter。</summary>
/// <param name="request">正式客服准入。</param>
/// <param name="availability">运行状态应用端口。</param>
/// <param name="reception">接待应用端口。</param>
/// <param name="configuration">成员只读端口。</param>
/// <param name="accounts">用户中心只读核验。</param>
/// <param name="tasks">任务只读 owner。</param>
/// <param name="processor">系统任务人工恢复。</param>
public sealed class AgentOperationsController(RequestAccess request, AvailabilityService availability, ReceptionService reception,
    IConfigurationRepository configuration, IAccountDirectory accounts, ITaskRepository tasks, ReliableTaskProcessor processor) : CustomerServiceControllerBase(request)
{
    /// <summary>读取展示状态，不自动上线或续约。</summary>
    [HttpGet("agents/availability")]
    public async Task<object> Get(string topOrgId, string assistantConfigId, CancellationToken ct)
    {
        await RequestAccess.AgentListAsync(topOrgId, assistantConfigId, ct);
        return Success(await availability.GetAsync(Actor, topOrgId, ct));
    }

    /// <summary>明确状态选择，推进在线代次。</summary>
    [HttpPost("agents/availability")]
    public async Task<object> Set(string topOrgId, string assistantConfigId, AvailabilityCommand command, CancellationToken ct)
    {
        await RequestAccess.AgentListAsync(topOrgId, assistantConfigId, ct);
        return Success(await availability.SetAsync(Actor, topOrgId, command.Status, ct));
    }

    /// <summary>仅续有效同代次 ONLINE。</summary>
    [HttpPost("agents/availability/heartbeat")]
    public async Task<object> Heartbeat(string topOrgId, string assistantConfigId, HeartbeatCommand command, CancellationToken ct)
    {
        await RequestAccess.AgentListAsync(topOrgId, assistantConfigId, ct);
        return Success(await availability.HeartbeatAsync(Actor, topOrgId, command.Version, ct));
    }

    /// <summary>同组候选只暴露最小账号引用，真正改派仍实时核验在线和容量。</summary>
    [HttpGet("agents/transfer-candidates")]
    public async Task<object> Candidates(string topOrgId, string assistantConfigId, CancellationToken ct, string? afterCursor = null)
    {
        await RequestAccess.AgentListAsync(topOrgId, assistantConfigId, ct);
        var config = await configuration.RoutingAsync(Actor.TenantId, topOrgId, ct);
        var members = config!.Groups.Where(g => g.Members.Any(m => m.AccountId == Actor.AccountId))
            .SelectMany(g => g.Members).Where(m => m.AccountId != Actor.AccountId).DistinctBy(m => m.AccountId).OrderBy(m => m.AccountId)
            .Where(m => afterCursor is null || string.CompareOrdinal(m.AccountId, afterCursor) > 0).Take(101).ToArray();
        return Success(new { candidates = members.Take(100), hasMore = members.Length > 100, nextCursor = members.Length > 100 ? members[99].AccountId : null });
    }

    /// <summary>事务外实时核验目标账号，事务内再次核验原组、在线和容量。</summary>
    [HttpPost("agents/conversations/{id}/transfer")]
    public async Task<object> Transfer(string id, string topOrgId, TransferCommand command, CancellationToken ct)
    {
        var scope = await RequestAccess.AgentAsync(topOrgId, id, ct);
        await accounts.RequireAsync(Actor.TenantId, topOrgId, Guard.Text(command.TargetAccountId, "目标账号"), ct);
        return Success(await reception.TransferAsync(Actor, scope, command, ct));
    }

    /// <summary>组织系统任务最小失败投影，不返回载荷或正文。</summary>
    [HttpGet("agents/system-handoffs")]
    public async Task<object> SystemTasks(string topOrgId, string assistantConfigId, CancellationToken ct)
    {
        await RequestAccess.AgentListAsync(topOrgId, assistantConfigId, ct);
        var values = await tasks.ListAsync(Actor.TenantId, topOrgId, "SYSTEM_HANDOFF", ct);
        return Success(values.Where(x => x.AssistantConfigId == assistantConfigId && x.State == DeliveryState.DEAD)
            .Select(x => new { x.ConversationId, x.EventKey, x.State, x.Attempts, x.LastError, x.Version }));
    }

    /// <summary>仅目标组织正式账号可恢复本会话 DEAD 系统任务。</summary>
    [HttpPost("agents/conversations/{id}/system-handoff/retry")]
    public async Task<object> Retry(string id, string topOrgId, CancellationToken ct)
    {
        var scope = await RequestAccess.AgentAsync(topOrgId, id, ct);
        var failed = (await tasks.ListAsync(Actor.TenantId, topOrgId, "SYSTEM_HANDOFF", ct))
            .FirstOrDefault(x => x.ConversationId == id && x.State == DeliveryState.DEAD) ?? throw BusinessException.Conflict("没有可重试的失败任务");
        return Success(await processor.RetrySystemAsync(Actor, scope, failed.EventKey, ct));
    }
}
