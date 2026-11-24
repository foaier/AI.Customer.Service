using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using Microsoft.EntityFrameworkCore;

namespace AI.Customer.Service.Infrastructure.Persistence;

/// <summary>
/// Conversation owner 的类型化 ORM 仓储，完整隔离条件不向应用层泄露 DbSet。
/// </summary>
/// <param name="db"> 共享事务上下文。 </param>
public sealed class ConversationRepository(CustomerServiceDbContext db) : IConversationRepository
{
    /// <summary>
    /// 按更新时间读取未处于人工接待且最近完成轮次尚未评估调查资格的会话，限制返回数量。
    /// </summary>
    public Task<List<Conversation>> SurveyCandidatesAsync(int limit, CancellationToken ct) =>
        db.Set<Conversation>().AsNoTracking().Where(x => x.HumanEpisodeId == null && x.LastCompletedTurnId != null &&
            x.LastCompletedTurnId != x.SurveyCheckedTurnId).OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id).Take(limit).ToListAsync(ct);
    /// <summary>
    /// 在指定租户内查找会话；事务外使用只读查询，事务内保留变更跟踪。
    /// </summary>
    public Task<Conversation?> FindAsync(string id, CancellationToken ct) =>
        (db.Database.CurrentTransaction is null ? db.Set<Conversation>().AsNoTracking() : db.Set<Conversation>())
            .SingleOrDefaultAsync(x => x.Id == id, ct);
    /// <summary>
    /// 将新会话加入当前上下文，等待共享事务提交。
    /// </summary>
    public void Add(Conversation value) => db.Add(value);
    /// <summary>
    /// 将真实 AI 轮次加入当前上下文，等待共享事务提交。
    /// </summary>
    public void AddTurn(AiTurn value) => db.Add(value);
    /// <summary>
    /// 按租户、组织、会话、助手和大会范围查找指定 AI 轮次。
    /// </summary>
    public Task<AiTurn?> TurnAsync(Scope scope, string id, CancellationToken ct) =>
        InScope<AiTurn>(scope).SingleOrDefaultAsync(x => x.Id == id, ct);
    /// <summary>
    /// 读取完整会话范围内最近的指定数量 AI 轮次，并按创建时间正序返回。
    /// </summary>
    public async Task<List<AiTurn>> TurnsAsync(Scope scope, int limit, CancellationToken ct) =>
        (await InScope<AiTurn>(scope).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(limit).ToListAsync(ct))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToList();
    /// <summary>
    /// 将人工时间线消息加入当前上下文，等待共享事务提交。
    /// </summary>
    public void AddMessage(HumanMessage value) => db.Add(value);
    /// <summary>
    /// 按完整会话范围、发送主体和客户端消息键查找消息，供幂等校验使用。
    /// </summary>
    public Task<HumanMessage?> MessageByKeyAsync(Scope scope, string actor, string key, CancellationToken ct) =>
        InScope<HumanMessage>(scope).SingleOrDefaultAsync(x => x.SenderId == actor && x.ClientMsgId == key, ct);
    /// <summary>
    /// 按完整会话范围读取指定序列之后的人工消息，按序列升序分页返回。
    /// </summary>
    public Task<List<HumanMessage>> MessagesAsync(Scope scope, long afterSeq, int limit, CancellationToken ct) =>
        InScope<HumanMessage>(scope).Where(x => x.Seq > afterSeq).OrderBy(x => x.Seq).Take(limit).ToListAsync(ct);
    /// <summary>
    /// 读取完整会话范围内指定主体的消息回执水位。
    /// </summary>
    public Task<MessageReceipt?> ReceiptAsync(Scope scope, string actor, CancellationToken ct) =>
        InScope<MessageReceipt>(scope).SingleOrDefaultAsync(x => x.ActorId == actor, ct);
    /// <summary>
    /// 统计当前客服有权处理的进行中接待，汇总访客未读消息数、最新序列和已读水位。
    /// </summary>
    public async Task<List<InboxMessageStats>> InboxMessageStatsAsync(string? assistant, IReadOnlyCollection<string> groups, string actor, CancellationToken ct)
    {
        var episodes = db.Set<Episode>().Where(e =>
            (assistant == null || e.AssistantConfigId == assistant) && groups.Contains(e.GroupId) && e.AgentId == actor &&
            (e.Status == EpisodeStatus.CLAIMED || e.Status == EpisodeStatus.ACTIVE));
        var owned = await episodes.ToListAsync(ct);
        if (owned.Count == 0) return [];
        var episodeIds = owned.Select(x => x.Id).ToArray();
        var conversationIds = owned.Select(x => x.ConversationId).Distinct().ToArray();
        var messages = db.Set<HumanMessage>().Where(m =>
            episodeIds.Contains(m.EpisodeId) && m.SenderRole == "VISITOR");
        var latest = await messages.GroupBy(m => m.EpisodeId).Select(g => new
        {
            EpisodeId = g.Key,
            LastVisitorSeq = g.Max(m => m.Seq),
            LastVisitorMessageAt = g.Max(m => m.CreatedAt)
        }).ToListAsync(ct);
        var unread = await messages.Where(m => m.Seq > (db.Set<MessageReceipt>().Where(r =>
                r.ConversationId == m.ConversationId && r.ActorId == actor).Select(r => (long?)r.ReadSeq).FirstOrDefault() ?? 0))
            .GroupBy(m => m.EpisodeId).Select(g => new { EpisodeId = g.Key, Count = g.Count() }).ToListAsync(ct);
        var receipts = await db.Set<MessageReceipt>().AsNoTracking().Where(r =>
            r.ActorId == actor && conversationIds.Contains(r.ConversationId)).Select(r => new { r.ConversationId, r.ReadSeq }).ToListAsync(ct);
        var lastById = latest.ToDictionary(x => x.EpisodeId, StringComparer.Ordinal);
        var unreadById = unread.ToDictionary(x => x.EpisodeId, x => x.Count, StringComparer.Ordinal);
        var readByConversation = receipts.ToDictionary(x => x.ConversationId, x => x.ReadSeq, StringComparer.Ordinal);
        return owned.Select(episode =>
        {
            lastById.TryGetValue(episode.Id, out var last);
            unreadById.TryGetValue(episode.Id, out var count);
            readByConversation.TryGetValue(episode.ConversationId, out var readSeq);
            return new InboxMessageStats(episode.Id, count, last?.LastVisitorSeq ?? 0, readSeq, last?.LastVisitorMessageAt);
        }).ToList();
    }
    /// <summary>
    /// 将主体首次消息回执加入当前上下文，等待共享事务提交。
    /// </summary>
    public void AddReceipt(MessageReceipt value) => db.Add(value);
    /// <summary>
    /// 按租户、组织和助手过滤会话，并按创建时间及标识稳定分页。
    /// </summary>
    public Task<List<Conversation>> PageAsync(string assistant, int page, int size, CancellationToken ct) =>
        db.Set<Conversation>().Where(x => x.AssistantConfigId == assistant)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);

    /// <summary>
    /// 查询始终绑定租户、组织、会话、助手和大会。
    /// </summary>
    private IQueryable<T> InScope<T>(Scope scope) where T : ConversationAggregate =>
        db.Set<T>().Where(x => x.ConversationId == scope.ConversationId && x.AssistantConfigId == scope.AssistantConfigId && x.BventId == scope.BventId);
}

/// <summary>
/// Customer Service owner 的接待、建议、等待和不可变回执 ORM 仓储。
/// </summary>
/// <param name="db"> 共享事务上下文。 </param>
public sealed class ReceptionRepository(CustomerServiceDbContext db) : IReceptionRepository
{
    /// <summary>
    /// 在完整会话范围内按申请时间和标识倒序读取最新接待周期，无记录时返回空。
    /// </summary>
    public Task<Episode?> LatestAsync(Scope scope, CancellationToken ct) =>
        InScope<Episode>(scope).OrderByDescending(x => x.RequestedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    /// <summary>
    /// 按完整会话范围和固定周期标识查询接待记录。
    /// </summary>
    public Task<Episode?> EpisodeAsync(Scope scope, string id, CancellationToken ct) => InScope<Episode>(scope).SingleOrDefaultAsync(x => x.Id == id, ct);
    /// <summary>
    /// 将新接待周期加入当前上下文，等待共享事务提交。
    /// </summary>
    public void Add(Episode value) => db.Add(value);
    /// <summary>
    /// 读取当前权限组内待接单或本人处理中的接待，按申请顺序最多返回一百条。
    /// </summary>
    public Task<List<Episode>> QueueAsync(string assistant, IReadOnlyCollection<string> groups, string actor, CancellationToken ct) =>
        db.Set<Episode>().Where(x => x.AssistantConfigId == assistant &&
            groups.Contains(x.GroupId) && (x.Status == EpisodeStatus.REQUESTED ||
                ((x.Status == EpisodeStatus.CLAIMED || x.Status == EpisodeStatus.ACTIVE) && x.AgentId == actor)))
            .OrderBy(x => x.RequestedAt).ThenBy(x => x.Id).Take(100).ToListAsync(ct);
    /// <summary>
    /// 读取当前权限组内待接单或本人处理中的完整接待列表，支持按助手筛选。
    /// </summary>
    public Task<List<Episode>> InboxEpisodesAsync(string? assistant, IReadOnlyCollection<string> groups, string actor, CancellationToken ct) =>
        db.Set<Episode>().AsNoTracking().Where(x => (assistant == null || x.AssistantConfigId == assistant) && groups.Contains(x.GroupId) &&
            (x.Status == EpisodeStatus.REQUESTED || ((x.Status == EpisodeStatus.CLAIMED || x.Status == EpisodeStatus.ACTIVE) && x.AgentId == actor)))
            .OrderBy(x => x.RequestedAt).ThenBy(x => x.Id).ToListAsync(ct);
    /// <summary>
    /// 按截止时间读取已到期的 ACTIVE 接待候选，供应用层复核并自动结单。
    /// </summary>
    public Task<List<Episode>> AutoCloseCandidatesAsync(DateTime now, int limit, CancellationToken ct) =>
        db.Set<Episode>().AsNoTracking().Where(x => x.Status == EpisodeStatus.ACTIVE && x.AutoCloseAt != null && x.AutoCloseAt <= now)
            .OrderBy(x => x.AutoCloseAt).ThenBy(x => x.Id).Take(limit).ToListAsync(ct);
    /// <summary>
    /// 统计指定客服在同租户组织下所有助手的已认领和进行中接待数量。
    /// </summary>
    public Task<int> ActiveCountAsync(string actor, CancellationToken ct) =>
        db.Set<Episode>().CountAsync(x => x.AgentId == actor &&
            (x.Status == EpisodeStatus.CLAIMED || x.Status == EpisodeStatus.ACTIVE), ct);
    /// <summary>
    /// 按完整会话范围和原接待周期读取等待事实。
    /// </summary>
    public Task<Waiting?> WaitingAsync(Scope scope, string episode, CancellationToken ct) => InScope<Waiting>(scope).SingleOrDefaultAsync(x => x.EpisodeId == episode, ct);
    /// <summary>
    /// 将首次建立的等待窗口加入当前上下文，等待共享事务提交。
    /// </summary>
    public void AddWaiting(Waiting value) => db.Add(value);
    /// <summary>
    /// 将受限工具生成的建议卡加入当前上下文，等待共享事务提交。
    /// </summary>
    public void AddProposal(Proposal value) => db.Add(value);
    /// <summary>
    /// 在完整会话范围内查找指定建议卡。
    /// </summary>
    public Task<Proposal?> ProposalAsync(Scope scope, string id, CancellationToken ct) => InScope<Proposal>(scope).SingleOrDefaultAsync(x => x.Id == id, ct);
    /// <summary>
    /// 读取完整会话范围内尚未处理且未过期的建议卡，按创建时间最多返回二十条。
    /// </summary>
    public Task<List<Proposal>> PendingAsync(Scope scope, DateTime now, CancellationToken ct) =>
        InScope<Proposal>(scope).Where(x => x.Status == ProposalStatus.PENDING && x.ExpiresAt > now).OrderBy(x => x.CreatedAt).Take(20).ToListAsync(ct);
    /// <summary>
    /// 按完整会话范围、操作者、动作和请求键读取首次成功命令回执。
    /// </summary>
    public Task<CommandReceipt?> CommandAsync(Scope scope, string actor, string action, string key, CancellationToken ct) =>
        InScope<CommandReceipt>(scope).SingleOrDefaultAsync(x => x.ActorId == actor && x.Action == action && x.RequestId == key, ct);
    /// <summary>
    /// 将不可变的成功命令回执加入当前上下文，等待共享事务提交。
    /// </summary>
    public void AddCommand(CommandReceipt value) => db.Add(value);
    /// <summary>
    /// 所有客服会话聚合均保留完整可信范围。
    /// </summary>
    private IQueryable<T> InScope<T>(Scope scope) where T : ConversationAggregate =>
        db.Set<T>().Where(x => x.ConversationId == scope.ConversationId && x.AssistantConfigId == scope.AssistantConfigId && x.BventId == scope.BventId);
}

/// <summary>
/// 路由、日历和在线运行事实的组织隔离 ORM 仓储。
/// </summary>
/// <param name="db"> 共享事务上下文。 </param>
public sealed class ConfigurationRepository(CustomerServiceDbContext db) : IConfigurationRepository
{
    /// <summary>
    /// 按租户和组织读取群通知配置，尚未初始化时返回空。
    /// </summary>
    public Task<ChatNotificationConfiguration?> ChatNotificationAsync(CancellationToken ct) =>
        db.Set<ChatNotificationConfiguration>().SingleOrDefaultAsync(ct);
    /// <summary>
    /// 将首次创建的组织群通知配置加入当前上下文，等待共享事务提交。
    /// </summary>
    public void AddChatNotification(ChatNotificationConfiguration value) => db.Add(value);
    /// <summary>
    /// 按租户和组织读取整体路由配置。
    /// </summary>
    public Task<RoutingConfiguration?> RoutingAsync(CancellationToken ct) =>
        db.Set<RoutingConfiguration>().SingleOrDefaultAsync(ct);
    /// <summary>
    /// 将首次创建的组织路由配置加入当前上下文，等待共享事务提交。
    /// </summary>
    public void AddRouting(RoutingConfiguration value) => db.Add(value);
    /// <summary>
    /// 按租户和组织读取营业日历，未配置时返回空，由应用层使用部署默认值。
    /// </summary>
    public Task<CalendarPolicy?> CalendarAsync(CancellationToken ct) =>
        db.Set<CalendarPolicy>().SingleOrDefaultAsync(ct);
    /// <summary>
    /// 将首次创建的组织营业日历加入当前上下文，等待共享事务提交。
    /// </summary>
    public void AddCalendar(CalendarPolicy value) => db.Add(value);
    /// <summary>
    /// 按租户、组织和账号标识读取在线运行状态。
    /// </summary>
    public Task<Availability?> AvailabilityAsync(string account, CancellationToken ct) =>
        db.Set<Availability>().SingleOrDefaultAsync(x => x.Id == account, ct);
    /// <summary>
    /// 将账号首次在线运行记录加入当前上下文，等待共享事务提交。
    /// </summary>
    public void AddAvailability(Availability value) => db.Add(value);
}

/// <summary>
/// 原跟进表单及最终回复的隔离 ORM 仓储，不直接操作 Conversation 表。
/// </summary>
/// <param name="db"> 共享事务上下文。 </param>
public sealed class FollowupRepository(CustomerServiceDbContext db) : IFollowupRepository
{
    /// <summary>
    /// 按完整会话范围和原接待周期查找跟进工单。
    /// </summary>
    public Task<Followup?> FindAsync(Scope s, string episode, CancellationToken ct) =>
        db.Set<Followup>().SingleOrDefaultAsync(x => x.ConversationId == s.ConversationId && x.EpisodeId == episode && x.AssistantConfigId == s.AssistantConfigId && x.BventId == s.BventId, ct);
    /// <summary>
    /// 将跟进工单加入当前上下文，等待共享事务提交。
    /// </summary>
    public void Add(Followup value) => db.Add(value);
    /// <summary>
    /// 按完整会话范围和原接待周期读取最终邮件回复。
    /// </summary>
    public Task<FollowupResponse?> ResponseAsync(Scope s, string episode, CancellationToken ct) =>
        db.Set<FollowupResponse>().SingleOrDefaultAsync(x => x.ConversationId == s.ConversationId && x.EpisodeId == episode && x.AssistantConfigId == s.AssistantConfigId && x.BventId == s.BventId, ct);
    /// <summary>
    /// 将最终邮件回复正文加入当前上下文，等待共享事务提交。
    /// </summary>
    public void AddResponse(FollowupResponse value) => db.Add(value);
    /// <summary>
    /// 读取指定租户、组织、助手及权限组内的跟进工单，按提交顺序最多返回一百条。
    /// </summary>
    public Task<List<Followup>> QueueAsync(string assistant, IReadOnlyCollection<string> groups, CancellationToken ct) =>
        db.Set<Followup>().Where(x => x.AssistantConfigId == assistant && groups.Contains(x.GroupId))
            .OrderBy(x => x.SubmittedAt).ThenBy(x => x.Id).Take(100).ToListAsync(ct);
    /// <summary>
    /// 读取指定授权范围内由访客表单创建的完整留言列表，支持按助手筛选。
    /// </summary>
    public Task<List<Followup>> InboxAsync(string? assistant, IReadOnlyCollection<string> groups, CancellationToken ct) =>
        db.Set<Followup>().AsNoTracking().Where(x => (assistant == null || x.AssistantConfigId == assistant) && groups.Contains(x.GroupId) && x.Origin == "FORM")
            .OrderBy(x => x.SubmittedAt).ThenBy(x => x.Id).ToListAsync(ct);
    /// <summary>
    /// 批量查询指定客服已查看的留言周期标识，空候选列表直接返回空集合。
    /// </summary>
    public async Task<HashSet<string>> SeenEpisodeIdsAsync(IReadOnlyCollection<string> episodes, string actor, CancellationToken ct) =>
        episodes.Count == 0 ? [] : (await db.Set<FollowupReceipt>().AsNoTracking().Where(x => x.SeenBy == actor && episodes.Contains(x.EpisodeId)).Select(x => x.EpisodeId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
    /// <summary>
    /// 按租户、组织、会话、原接待周期和客服标识读取首次查看回执。
    /// </summary>
    public Task<FollowupReceipt?> ReceiptAsync(Scope scope, string episode, string actor, CancellationToken ct) =>
        db.Set<FollowupReceipt>().SingleOrDefaultAsync(x => x.ConversationId == scope.ConversationId && x.EpisodeId == episode && x.SeenBy == actor, ct);
    /// <summary>
    /// 将客服首次查看留言的回执加入当前上下文，等待共享事务提交。
    /// </summary>
    public void AddReceipt(FollowupReceipt receipt) => db.Add(receipt);
    /// <summary>
    /// 批量查询指定租户组织内最终回复已结案的周期标识，空候选列表直接返回空集合。
    /// </summary>
    public async Task<HashSet<string>> ClosedEpisodeIdsAsync(IReadOnlyCollection<string> episodes, CancellationToken ct) =>
        episodes.Count == 0 ? [] : (await db.Set<FollowupResponse>().AsNoTracking().Where(x => x.State == DeliveryState.CLOSED && episodes.Contains(x.EpisodeId)).Select(x => x.EpisodeId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
}

/// <summary>
/// 调查 owner 的一会话一调查 ORM 仓储。
/// </summary>
/// <param name="db"> 共享事务上下文。 </param>
public sealed class SurveyRepository(CustomerServiceDbContext db) : ISurveyRepository
{
    /// <summary>
    /// 将绑定真实轮次的结束证据加入当前上下文，等待共享事务提交。
    /// </summary>
    public void AddEvidence(ResolutionEvidence value) => db.Add(value);
    /// <summary>
    /// 按完整会话范围、轮次和证据类型查找已有证据，供幂等校验使用。
    /// </summary>
    public Task<ResolutionEvidence?> EvidenceAsync(Scope s, string turn, string kind, CancellationToken ct) =>
        db.Set<ResolutionEvidence>().SingleOrDefaultAsync(x => x.ConversationId == s.ConversationId && x.AssistantConfigId == s.AssistantConfigId && x.BventId == s.BventId &&
            x.TurnId == turn && x.Kind == kind, ct);
    /// <summary>
    /// 读取完整会话范围内唯一的满意度调查，不创建新调查。
    /// </summary>
    public Task<Survey?> FindAsync(Scope s, CancellationToken ct) =>
        db.Set<Survey>().SingleOrDefaultAsync(x => x.ConversationId == s.ConversationId && x.AssistantConfigId == s.AssistantConfigId && x.BventId == s.BventId, ct);
    /// <summary>
    /// 将经过资格校验的满意度调查加入当前上下文，等待共享事务提交。
    /// </summary>
    public void Add(Survey value) => db.Add(value);
}

/// <summary>
/// 可靠投递任务 ORM 仓储，领取前过滤通道并保留租户组织条件。
/// </summary>
/// <param name="db"> 共享事务上下文。 </param>
public sealed class TaskRepository(CustomerServiceDbContext db) : ITaskRepository
{
    /// <summary>
    /// 按租户、组织和稳定事件键查找可靠任务。
    /// </summary>
    public Task<ReliableTask?> FindAsync(string key, CancellationToken ct) =>
        db.Set<ReliableTask>().SingleOrDefaultAsync(x => x.EventKey == key, ct);
    /// <summary>
    /// 将可靠任务加入当前上下文，与业务变更在同一事务内提交。
    /// </summary>
    public void Add(ReliableTask value) => db.Add(value);
    /// <summary>
    /// 按已启用通道及可选的组织通知开关筛选到期或租约失效的任务，按到期时间限制返回数量。
    /// </summary>
    public Task<List<ReliableTask>> DueAsync(IReadOnlyCollection<string> kinds, DateTime now, int limit, CancellationToken ct, bool includeConfiguredChatGroups = false) =>
        db.Set<ReliableTask>().AsNoTracking().Where(x =>
            ((!includeConfiguredChatGroups && kinds.Contains(x.Kind)) ||
             (includeConfiguredChatGroups &&
              ((kinds.Contains(x.Kind) && (x.Kind == "EVENT" || x.Kind == "SYSTEM_HANDOFF")) ||
               ((kinds.Contains(x.Kind) || x.Kind == "INTERNAL" || x.Kind == "HANDOFF_NOTIFY") &&
                db.Set<ChatNotificationConfiguration>().Any(c =>  c.Enabled))))) &&
            ((x.State == DeliveryState.PENDING && x.DueAt <= now) || (x.State == DeliveryState.CLAIMED && x.LeaseUntil <= now)))
            .OrderBy(x => x.DueAt).ThenBy(x => x.Id).Take(limit).ToListAsync(ct);
    /// <summary>
    /// 按租户、组织和任务类型读取最近创建的任务，最多返回一千条。
    /// </summary>
    public Task<List<ReliableTask>> ListAsync(string kind, CancellationToken ct) =>
        db.Set<ReliableTask>().Where(x => x.Kind == kind)
            .OrderByDescending(x => x.CreatedAt).Take(1000).ToListAsync(ct);
}
