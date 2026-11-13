using System.Data;
using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Ports;
using Microsoft.EntityFrameworkCore;

namespace AI.Customer.Service.Infrastructure.Persistence;

/// <summary>通过组织级 ORM 写门闩建立共享事务，覆盖跨会话容量、撤权及 generation 围栏。</summary>
/// <param name="db">当前请求唯一共享上下文。</param>
public sealed class OrmUnitOfWork(CustomerServiceDbContext db) : IUnitOfWork
{
    /// <inheritdoc />
    public async Task<T> ExecuteAsync<T>(string tenant, string org, Func<Task<T>> action, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("禁止嵌套本地业务事务");
        }
        await EnsureGateAsync(tenant, org, ct);
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await db.Set<OrganizationGate>().Where(x => x.TenantId == tenant && x.TopOrgId == org)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.Version, p => p.Version + 1), ct);
            var result = await action();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            await transaction.DisposeAsync();
            db.ChangeTracker.Clear();
            throw BusinessException.Conflict("并发写入冲突，请使用原请求键恢复结果");
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>独立初始化门闩，唯一键竞争必须完整回滚后重新读取，绝不沿用失败事务。</summary>
    private async Task EnsureGateAsync(string tenant, string org, CancellationToken ct)
    {
        if (await db.Set<OrganizationGate>().AnyAsync(x => x.TenantId == tenant && x.TopOrgId == org, ct))
        {
            return;
        }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Set<OrganizationGate>().Add(new OrganizationGate { TenantId = tenant, TopOrgId = org });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            await transaction.DisposeAsync();
            db.ChangeTracker.Clear();
            if (!await db.Set<OrganizationGate>().AnyAsync(x => x.TenantId == tenant && x.TopOrgId == org, ct))
            {
                throw;
            }
        }
    }
}
