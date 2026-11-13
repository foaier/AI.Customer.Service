using AI.Customer.Service.Api.Contracts;
using AI.Customer.Service.Common;
using AI.Customer.Service.Core;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Policies;
using AI.Customer.Service.Domain.Ports;

namespace AI.Customer.Service.Service;

/// <summary>人工消息和回执应用编排，正文只经 Conversation owner 单一端口保存。</summary>
/// <param name="conversations">会话及时间线 owner。</param>
/// <param name="reception">客服周期 owner。</param>
/// <param name="transactions">共享事务。</param>
/// <param name="runtime">回复权端口。</param>
/// <param name="access">成员准入。</param>
/// <param name="clock">UTC 时钟。</param>
public sealed class MessagingService(IConversationRepository conversations, IReceptionRepository reception, IUnitOfWork transactions,
    ConversationRuntime runtime, AccessService access, TimeProvider clock)
{
    /// <summary>同键同内容返回原消息，即使周期已结束；新消息必须仍具备当前回复权。</summary>
    public Task<object> SendAsync(Actor actor, Scope scope, bool agent, MessageCommand command, CancellationToken ct) =>
        transactions.ExecuteAsync(scope.TenantId, scope.TopOrgId, async () =>
        {
            Guard.Text(command.ClientMsgId, "消息键");
            Guard.Text(command.Content, "消息正文", 4000);
            var episode = await AuthorizeAsync(actor, scope, agent, ct);
            var prior = await conversations.MessageByKeyAsync(scope, actor.AccountId, command.ClientMsgId, ct);
            if (prior is not null)
            {
                if (prior.Content != command.Content || prior.SenderRole != (agent ? "AGENT" : "VISITOR"))
                {
                    throw BusinessException.Conflict("同一消息键不能修改内容");
                }
                return View(prior);
            }
            if (!episode.Status.IsOpen() || agent && episode.Status == EpisodeStatus.REQUESTED)
            {
                throw BusinessException.Conflict("当前接待不允许发送新消息");
            }
            var conversation = await runtime.RequireAsync(scope, ct);
            if (conversation.HumanEpisodeId != episode.Id)
            {
                throw BusinessException.Conflict("人工回复权已变化");
            }
            conversation.NextSeq++;
            conversation.Version++;
            var message = ConversationRuntime.Bind<HumanMessage>(scope, conversation.PrincipalId, Guard.Id("msg"));
            message.Seq = conversation.NextSeq;
            message.EpisodeId = episode.Id;
            message.SenderRole = agent ? "AGENT" : "VISITOR";
            message.SenderId = actor.AccountId;
            message.ClientMsgId = command.ClientMsgId;
            message.Content = command.Content;
            message.CreatedAt = clock.GetUtcNow().UtcDateTime;
            conversations.AddMessage(message);
            if (agent && episode.Status == EpisodeStatus.CLAIMED)
            {
                episode.Status = ReceptionRules.Transition(episode.Status, "FIRST_MESSAGE");
                episode.FirstResponseAt ??= message.CreatedAt;
                episode.Version++;
            }
            return View(message);
        }, ct);

    /// <summary>持锁复核认领后读取时间线，改派和撤权提交后旧客服不能读取正文。</summary>
    public Task<object> TimelineAsync(Actor actor, Scope scope, bool agent, long afterSeq, int limit, CancellationToken ct) =>
        transactions.ExecuteAsync<object>(scope.TenantId, scope.TopOrgId, async () =>
        {
            if (afterSeq < 0 || limit is < 1 or > 100)
            {
                throw BusinessException.Invalid("消息游标或页大小无效");
            }
            var episode = await AuthorizeAsync(actor, scope, agent, ct);
            var messages = await conversations.MessagesAsync(scope, afterSeq, limit, ct);
            var receipt = await conversations.ReceiptAsync(scope, actor.AccountId, ct);
            var peer = agent ? episode.PrincipalId : episode.AgentId;
            var peerReceipt = peer is null ? null : await conversations.ReceiptAsync(scope, peer, ct);
            return new { messages = messages.Select(View), receipt = ReceiptView(receipt), peerReceipt = ReceiptView(peerReceipt) };
        }, ct);

    /// <summary>只推进认证主体水位，READ 同时推进 DELIVERED，拒绝确认未来消息。</summary>
    public Task<object> ReceiptAsync(Actor actor, Scope scope, bool agent, ReceiptCommand command, CancellationToken ct) =>
        transactions.ExecuteAsync(scope.TenantId, scope.TopOrgId, async () =>
        {
            await AuthorizeAsync(actor, scope, agent, ct);
            var conversation = await runtime.RequireAsync(scope, ct);
            if (command.Kind is not "READ" and not "DELIVERED" || command.Seq < 0 || command.Seq > conversation.NextSeq)
            {
                throw BusinessException.Invalid("回执类型或序列无效");
            }
            var receipt = await conversations.ReceiptAsync(scope, actor.AccountId, ct);
            if (receipt is null)
            {
                receipt = ConversationRuntime.Bind<MessageReceipt>(scope, conversation.PrincipalId, Guard.Id("receipt"));
                receipt.ActorId = actor.AccountId;
                conversations.AddReceipt(receipt);
            }
            if (command.Seq > receipt.DeliveredSeq || command.Kind == "READ" && command.Seq > receipt.ReadSeq)
            {
                receipt.DeliveredSeq = Math.Max(receipt.DeliveredSeq, command.Seq);
                if (command.Kind == "READ")
                {
                    receipt.ReadSeq = Math.Max(receipt.ReadSeq, command.Seq);
                }
                receipt.Version++;
            }
            return ReceiptView(receipt);
        }, ct);

    /// <summary>当前认领者读取最小 AI 文本历史，不包含提示词或请求 DTO。</summary>
    public Task<object> HistoryAsync(Actor actor, Scope scope, CancellationToken ct) =>
        transactions.ExecuteAsync<object>(scope.TenantId, scope.TopOrgId, async () =>
        {
            await AuthorizeAsync(actor, scope, true, ct);
            return (await conversations.TurnsAsync(scope, 100, ct)).Where(x => x.Status == "SUCCEEDED")
                .SelectMany(x => new[] { new { id = x.Id + ":user", role = "user", content = x.UserContent },
                    new { id = x.Id + ":assistant", role = "assistant", content = x.AssistantContent! } }).ToArray();
        }, ct);

    /// <summary>在读取或写入的同一事务内执行本地资格复核。</summary>
    private async Task<Episode> AuthorizeAsync(Actor actor, Scope scope, bool agent, CancellationToken ct)
    {
        var episode = await reception.LatestAsync(scope, ct) ?? throw BusinessException.Forbidden("人工周期不存在");
        if (agent)
        {
            await access.RequireGroupAsync(actor, episode, ct);
            ReceptionRules.RequireAssignee(episode, actor.AccountId);
        }
        else if (episode.PrincipalId != actor.AccountId || episode.TenantId != actor.TenantId)
        {
            throw BusinessException.Forbidden();
        }
        return episode;
    }

    /// <summary>兼容原前端的消息投影，纯文本渲染由页面执行。</summary>
    public static object View(HumanMessage message) => new
    {
        message.Seq,
        messageId = message.Id,
        message.EpisodeId,
        message.SenderRole,
        message.SenderId,
        message.ClientMsgId,
        message.Content,
        message.CreatedAt
    };

    /// <summary>空回执使用零水位，不推断对方已接收或已读。</summary>
    private static object ReceiptView(MessageReceipt? receipt) => new { deliveredSeq = receipt?.DeliveredSeq ?? 0, readSeq = receipt?.ReadSeq ?? 0 };
}
