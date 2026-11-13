using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Service.Configuration;
using Microsoft.Extensions.Options;

namespace AI.Customer.Service.Service;

/// <summary>组织级在线展示和心跳代次，手动接单独立使用组权限及容量。</summary>
/// <param name="repository">运行事实 owner。</param>
/// <param name="reception">容量只读端口。</param>
/// <param name="transactions">组织级共享事务。</param>
/// <param name="clock">UTC 时钟。</param>
/// <param name="options">容量规则。</param>
public sealed class AvailabilityService(IConfigurationRepository repository, IReceptionRepository reception,
    IUnitOfWork transactions, TimeProvider clock, IOptions<CustomerServiceOptions> options)
{
    /// <summary>只读恢复在线状态，过期按 OFFLINE 展示且不隐式续约。</summary>
    public async Task<object> GetAsync(Actor actor, string org, CancellationToken ct)
    {
        var value = await repository.AvailabilityAsync(actor.TenantId, org, actor.AccountId, ct);
        return await ViewAsync(actor, org, value, ct);
    }

    /// <summary>明确状态选择推进版本，旧页面此后不能继续续约。</summary>
    public Task<object> SetAsync(Actor actor, string org, AvailabilityStatus status, CancellationToken ct) =>
        transactions.ExecuteAsync(actor.TenantId, org, async () =>
        {
            if (!Enum.IsDefined(status))
            {
                throw BusinessException.Invalid("在线状态无效");
            }
            var value = await repository.AvailabilityAsync(actor.TenantId, org, actor.AccountId, ct);
            if (value is null)
            {
                value = new Availability { Id = actor.AccountId, TenantId = actor.TenantId, TopOrgId = org, Version = 0 };
                repository.AddAvailability(value);
            }
            value.Status = status;
            value.Version++;
            value.ExpiresAt = status == AvailabilityStatus.ONLINE ? clock.GetUtcNow().UtcDateTime.AddSeconds(45) : null;
            return await ViewAsync(actor, org, value, ct);
        }, ct);

    /// <summary>心跳仅延长有效同代次 ONLINE，不推进代次或复活离线选择。</summary>
    public Task<object> HeartbeatAsync(Actor actor, string org, long version, CancellationToken ct) =>
        transactions.ExecuteAsync(actor.TenantId, org, async () =>
        {
            var value = await repository.AvailabilityAsync(actor.TenantId, org, actor.AccountId, ct);
            if (value is null || value.Version != version || value.Status != AvailabilityStatus.ONLINE ||
                value.ExpiresAt is null || value.ExpiresAt <= clock.GetUtcNow().UtcDateTime)
            {
                throw BusinessException.Conflict("在线代次已失效，请明确重新选择上线");
            }
            value.ExpiresAt = clock.GetUtcNow().UtcDateTime.AddSeconds(45);
            return await ViewAsync(actor, org, value, ct);
        }, ct);

    /// <summary>返回实际接待数及有效展示状态，不授予账号或组权限。</summary>
    private async Task<object> ViewAsync(Actor actor, string org, Availability? value, CancellationToken ct) => new
    {
        status = value?.Status == AvailabilityStatus.ONLINE && value.ExpiresAt <= clock.GetUtcNow().UtcDateTime
            ? AvailabilityStatus.OFFLINE : value?.Status ?? AvailabilityStatus.OFFLINE,
        expiresAt = value?.ExpiresAt,
        version = value?.Version ?? 0,
        activeCount = await reception.ActiveCountAsync(actor.TenantId, org, actor.AccountId, ct),
        capacity = options.Value.Capacity
    };
}
