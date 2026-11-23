using System.Data;
using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Ports;
using Microsoft.EntityFrameworkCore;

namespace AI.Customer.Service.Infrastructure.Persistence;

/// <summary>
/// 通过共享数据库事务保证聚合、回执和待投递事件原子提交。
/// </summary>
public sealed class OrmUnitOfWork(CustomerServiceDbContext db) : IUnitOfWork
{
    /// <summary>
    /// 在串行化事务内执行本地业务并保存变更。
    /// </summary>
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("禁止嵌套本地业务事务");
        }
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var result = await action();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
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
}