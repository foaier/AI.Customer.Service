using System.Text.Json;
using AI.Customer.Service.Common;
using AI.Customer.Service.Core;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;

namespace AI.Customer.Service.Service;

/// <summary>客服 owner 的不可变命令回执与审计事件登记，不承担状态决策。</summary>
/// <param name="repository">本 owner 回执仓储。</param>
/// <param name="tasks">可靠事件仓储。</param>
/// <param name="clock">UTC 时钟。</param>
public sealed class CommandJournal(IReceptionRepository repository, ITaskRepository tasks, TimeProvider clock)
{
    /// <summary>返回首次回执；相同键不同版本或内容必须冲突。</summary>
    public async Task<object?> ReplayAsync(Scope scope, Actor actor, string action, string request, object input, CancellationToken ct)
    {
        Guard.Text(request, "请求幂等键");
        var prior = await repository.CommandAsync(scope, actor.AccountId, action, request, ct);
        if (prior is null)
        {
            return null;
        }
        if (prior.Fingerprint != Fingerprint(input))
        {
            throw BusinessException.Conflict("同一请求键不能更换版本或内容");
        }
        var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(prior.ResultJson, JsonDefaults.Options)!;
        result["replayed"] = JsonSerializer.SerializeToElement(true);
        return result;
    }

    /// <summary>状态写入同事务登记首次成功响应，后续重试不重算版本。</summary>
    public object Save(Scope scope, Actor actor, string principal, string action, string request, object input, object result)
    {
        var receipt = ConversationRuntime.Bind<CommandReceipt>(scope, principal, Guard.Id("cmd"));
        receipt.ActorId = actor.AccountId;
        receipt.Action = action;
        receipt.RequestId = request;
        receipt.Fingerprint = Fingerprint(input);
        receipt.ResultJson = JsonSerializer.Serialize(result, JsonDefaults.Options);
        receipt.CreatedAt = clock.GetUtcNow().UtcDateTime;
        receipt.TraceId = actor.TraceId;
        repository.AddCommand(receipt);
        return result;
    }

    /// <summary>业务写入同事务登记外发或本地审计事实，不能伪造已受理。</summary>
    public async Task EnqueueAsync(Scope scope, string principal, string episode, string kind, string key, object payload, string trace, CancellationToken ct)
    {
        if (await tasks.FindAsync(scope.TenantId, scope.TopOrgId, key, ct) is not null)
        {
            return;
        }
        var task = ConversationRuntime.Bind<ReliableTask>(scope, principal, Guard.Id("task"));
        task.Kind = kind;
        task.EventKey = key;
        task.EpisodeId = episode;
        task.Payload = JsonSerializer.Serialize(payload, JsonDefaults.Options);
        task.TraceId = trace;
        task.CreatedAt = clock.GetUtcNow().UtcDateTime;
        task.DueAt = task.CreatedAt;
        tasks.Add(task);
    }

    /// <summary>命令摘要包含首次版本，响应丢失时必须原样重试。</summary>
    private static string Fingerprint(object input) => Guard.Fingerprint(JsonSerializer.Serialize(input, JsonDefaults.Options));
}
