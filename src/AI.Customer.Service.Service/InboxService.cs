using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;

namespace AI.Customer.Service.Service;

/// <summary>
/// 授权坐席范围的未读汇总与不含正文的队列提醒投影。
/// </summary>
public sealed class InboxService(AccessService access, IReceptionRepository reception, IConversationRepository conversations, IFollowupRepository followups)
{
    /// <summary>
    /// 完整权限范围计算稳定摘要；页面分页不影响角标及变化检测。
    /// </summary>
    public async Task<InboxSummary> SummaryAsync(Actor actor, string? assistant, CancellationToken ct)
    {
        var groups = await access.GroupsAsync(actor, ct);
        if (assistant is not null) access.RequireAssistant(actor, assistant);
        var episodes = await reception.InboxEpisodesAsync(assistant, groups, actor.AccountId, ct);
        var stats = await conversations.InboxMessageStatsAsync(assistant, groups, actor.AccountId, ct);
        var forms = await followups.InboxAsync(assistant, groups, ct);
        var ids = forms.Select(x => x.EpisodeId).ToArray();
        var seen = await followups.SeenEpisodeIdsAsync(ids, actor.AccountId, ct);
        var closed = await followups.ClosedEpisodeIdsAsync(ids, ct);
        var visibleForms = forms.Where(x => x.State == "OPEN" || x.AssignedTo == actor.AccountId)
            .Select(x => new
            {
                x.EpisodeId,
                x.TicketId,
                x.State,
                x.AssignedTo,
                x.SubmittedAt,
                hasUnseen = !seen.Contains(x.EpisodeId) && !closed.Contains(x.EpisodeId),
                isClosed = closed.Contains(x.EpisodeId)
            }).ToArray();
        var unreadCount = stats.Sum(x => x.UnreadCount);
        var unreadConversations = stats.Count(x => x.UnreadCount > 0);
        var newFollowups = visibleForms.Count(x => x.hasUnseen && !x.isClosed);
        var pending = episodes.Where(x => x.Status == EpisodeStatus.REQUESTED).ToArray();
        var projection = new
        {
            assistant,
            actor.AccountId,
            unread = stats.OrderBy(x => x.EpisodeId).Select(x => new { x.EpisodeId, x.UnreadCount, x.LastVisitorSeq, x.ReadSeq, x.LastVisitorMessageAt }),
            receptions = episodes.OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.AgentId, x.Version }),
            followups = visibleForms.OrderBy(x => x.EpisodeId),
            pending = pending.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal)
        };
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(projection))));
        return new InboxSummary(unreadCount, unreadConversations, newFollowups, pending.Length, hash);
    }

    /// <summary>
    /// 返回分页队列当前已加载周期的真实消息水位，不返回正文。
    /// </summary>
    public async Task<object> QueueAsync(Actor actor, string assistant, CancellationToken ct)
    {
        var groups = await access.GroupsAsync(actor, ct);
        var episodes = await reception.QueueAsync(assistant, groups, actor.AccountId, ct);
        var stats = (await conversations.InboxMessageStatsAsync(assistant, groups, actor.AccountId, ct))
            .ToDictionary(x => x.EpisodeId, StringComparer.Ordinal);
        var queue = new List<object>(episodes.Count);
        foreach (var x in episodes)
        {
            var canRead = x.AgentId == actor.AccountId && x.Status is EpisodeStatus.CLAIMED or EpisodeStatus.ACTIVE;
            stats.TryGetValue(x.Id, out var current);
            var waiting = await reception.WaitingAsync(x.Scope, x.Id, ct);
            var form = await followups.FindAsync(x.Scope, x.Id, ct);
            queue.Add(new
            {
                x.Scope,
                episodeId = x.Id,
                x.Status,
                x.Version,
                x.PrincipalId,
                x.AgentId,
                x.GroupId,
                x.RequestedAt,
                x.TriggerSource,
                x.ReasonCode,
                x.AutoCloseAt,
                x.ResolutionReason,
                contactEmail = waiting?.Email,
                contactName = form?.ContactName,
                unreadCount = canRead ? current?.UnreadCount ?? 0 : (int?)null,
                lastVisitorSeq = canRead ? current?.LastVisitorSeq ?? 0 : (long?)null,
                readSeq = canRead ? current?.ReadSeq ?? 0 : (long?)null,
                lastVisitorMessageAt = canRead ? current?.LastVisitorMessageAt : null
            });
        }
        return new { groupId = (string?)null, episodes = queue };
    }
}

/// <summary>
/// 坐席当前授权收件箱的聚合角标。
/// </summary>
public sealed record InboxSummary(int UnreadMessageCount, int UnreadConversationCount, int NewFollowupCount, int PendingReceptionCount, string ChangeToken);
