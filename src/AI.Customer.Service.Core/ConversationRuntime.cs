using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;

namespace AI.Customer.Service.Core;

/// <summary>Conversation owner 的应用端口，独占维护回复权、轮次和成功历史。</summary>
/// <param name="conversations">本 owner 的仓储。</param>
/// <param name="transactions">客服与会话共享的短事务。</param>
/// <param name="clock">可替换的 UTC 时钟。</param>
public sealed class ConversationRuntime(IConversationRepository conversations, IUnitOfWork transactions, TimeProvider clock)
{
    /// <summary>正式创建会话，组织和大会来自受信助手注册表。</summary>
    public Task<Conversation> CreateAsync(Actor actor, AssistantDefinition assistant, string id, CancellationToken ct) =>
        transactions.ExecuteAsync(actor.TenantId, assistant.TopOrgId, async () =>
        {
            var existing = await conversations.FindAsync(actor.TenantId, id, ct);
            if (existing is not null)
            {
                RequireVisitor(existing, actor);
                if (existing.AssistantConfigId != assistant.Id || existing.TopOrgId != assistant.TopOrgId)
                {
                    throw BusinessException.Forbidden("会话已绑定其它助手");
                }
                return existing;
            }
            var now = clock.GetUtcNow().UtcDateTime;
            var value = new Conversation
            {
                Id = Guard.Text(id, "会话标识"),
                ConversationId = id,
                TenantId = actor.TenantId,
                TopOrgId = assistant.TopOrgId,
                AssistantConfigId = assistant.Id,
                BventId = assistant.BventId,
                PrincipalId = actor.AccountId,
                CreatedAt = now,
                UpdatedAt = now
            };
            conversations.Add(value);
            return value;
        }, ct);

    /// <summary>读取并校验原访客关系，不能只凭持有会话 ID 读取。</summary>
    public async Task<Conversation> VisitorAsync(Actor actor, string id, CancellationToken ct)
    {
        var value = await conversations.FindAsync(actor.TenantId, id, ct) ?? throw BusinessException.Forbidden("会话不存在或不可访问");
        RequireVisitor(value, actor);
        return value;
    }

    /// <summary>短事务登记 AI 轮次，不把网络生成放进事务。</summary>
    public Task<AiTurn> BeginAsync(Actor actor, Scope scope, string input, CancellationToken ct) =>
        transactions.ExecuteAsync(scope.TenantId, scope.TopOrgId, async () =>
        {
            var conversation = await RequireAsync(scope, ct);
            RequireVisitor(conversation, actor);
            if (conversation.HumanEpisodeId is not null)
            {
                throw BusinessException.Conflict("当前正在人工接待，请通过人工消息接口发送");
            }
            var now = clock.GetUtcNow().UtcDateTime;
            if (conversation.CurrentTurnId is not null && conversation.TurnLeaseUntil > now)
            {
                throw BusinessException.Conflict("已有 AI 轮次正在生成");
            }
            var turn = Bind<AiTurn>(scope, conversation.PrincipalId, Guard.Id("turn"));
            turn.UserContent = Guard.Text(input, "咨询内容", 16000);
            conversation.Epoch++;
            turn.Epoch = conversation.Epoch;
            turn.CreatedAt = now;
            turn.TraceId = actor.TraceId;
            conversation.CurrentTurnId = turn.Id;
            conversation.TurnLeaseUntil = now.AddMinutes(5);
            conversation.Version++;
            conversation.UpdatedAt = now;
            conversations.AddTurn(turn);
            return turn;
        }, ct);

    /// <summary>每次向访客发送文本前检查当前围栏，人工接管后立即停止旧轮次。</summary>
    public async Task<bool> IsCurrentAsync(Scope scope, string turn, long epoch, CancellationToken ct)
    {
        var conversation = await RequireAsync(scope, ct);
        return conversation.HumanEpisodeId is null && conversation.Epoch == epoch && conversation.CurrentTurnId == turn;
    }

    /// <summary>完整成功回答只能在原代次提交；迟到内容标记 FENCED 且不进入展示历史。</summary>
    public Task<bool> FinishAsync(Scope scope, string turnId, long epoch, string? content, string state, CancellationToken ct) =>
        transactions.ExecuteAsync(scope.TenantId, scope.TopOrgId, async () =>
        {
            var conversation = await RequireAsync(scope, ct);
            var turn = await conversations.TurnAsync(scope, turnId, ct) ?? throw BusinessException.Conflict("AI 轮次不存在");
            if (turn.Status != "RUNNING")
            {
                return false;
            }
            if (state is not "SUCCEEDED" and not "FAILED")
            {
                throw BusinessException.Invalid("AI 完成状态必须为 SUCCEEDED 或 FAILED");
            }
            var current = turn.Epoch == epoch && conversation.HumanEpisodeId is null && conversation.Epoch == epoch && conversation.CurrentTurnId == turnId;
            turn.Status = current ? state : "FENCED";
            turn.AssistantContent = current && state == "SUCCEEDED" ? Guard.Text(content, "模型回答", 65535) : null;
            turn.CompletedAt = clock.GetUtcNow().UtcDateTime;
            turn.Version++;
            if (current && state == "SUCCEEDED")
            {
                conversation.LastCompletedTurnId = turnId;
                conversation.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            }
            if (conversation.CurrentTurnId == turnId)
            {
                conversation.CurrentTurnId = null;
                conversation.TurnLeaseUntil = null;
                conversation.Version++;
            }
            return current;
        }, ct);

    /// <summary>事务内取得完整会话范围，不允许跨 owner 直接访问持久化对象。</summary>
    public async Task<Conversation> RequireAsync(Scope scope, CancellationToken ct)
    {
        var value = await conversations.FindAsync(scope.TenantId, scope.ConversationId, ct);
        if (value is null || value.TopOrgId != scope.TopOrgId || value.AssistantConfigId != scope.AssistantConfigId || value.BventId != scope.BventId)
        {
            throw BusinessException.Forbidden("会话范围不一致");
        }
        return value;
    }

    /// <summary>在共享事务中转移回复权并使所有旧 AI 响应失效。</summary>
    public async Task TakeHumanAsync(Scope scope, string episode, CancellationToken ct)
    {
        var value = await RequireAsync(scope, ct);
        if (value.HumanEpisodeId is not null)
        {
            throw BusinessException.Conflict("人工周期尚未结束");
        }
        value.HumanEpisodeId = episode;
        value.CurrentTurnId = null;
        value.TurnLeaseUntil = null;
        value.Epoch++;
        value.Version++;
    }

    /// <summary>仅原人工周期能够在同一事务内恢复 AI，不把释放等同于问题已解决。</summary>
    public async Task ResumeAiAsync(Scope scope, string episode, CancellationToken ct)
    {
        var value = await RequireAsync(scope, ct);
        if (value.HumanEpisodeId != episode)
        {
            throw BusinessException.Conflict("回复权与原人工周期不一致");
        }
        value.HumanEpisodeId = null;
        value.Epoch++;
        value.Version++;
    }

    /// <summary>绑定新的领域事实，不从模型或访客请求复制归属。</summary>
    public static T Bind<T>(Scope scope, string principal, string id) where T : ConversationAggregate, new() =>
        new()
        {
            Id = id,
            TenantId = scope.TenantId,
            TopOrgId = scope.TopOrgId,
            ConversationId = scope.ConversationId,
            AssistantConfigId = scope.AssistantConfigId,
            BventId = scope.BventId,
            PrincipalId = principal
        };

    /// <summary>校验认证租户和原会话主体。</summary>
    private static void RequireVisitor(Conversation value, Actor actor)
    {
        if (value.TenantId != actor.TenantId || value.PrincipalId != actor.AccountId)
        {
            throw BusinessException.Forbidden("当前账号不是原会话访客");
        }
    }
}
