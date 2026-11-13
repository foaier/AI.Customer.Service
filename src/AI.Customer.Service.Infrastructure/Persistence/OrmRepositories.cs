using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using Microsoft.EntityFrameworkCore;

namespace AI.Customer.Service.Infrastructure.Persistence;

/// <summary>Conversation owner 的类型化 ORM 仓储，完整隔离条件不向应用层泄露 DbSet。</summary>
/// <param name="db">共享事务上下文。</param>
public sealed class ConversationRepository(CustomerServiceDbContext db) : IConversationRepository
{
    /// <inheritdoc />
    public Task<List<Conversation>> SurveyCandidatesAsync(int limit, CancellationToken ct) =>
        db.Set<Conversation>().AsNoTracking().Where(x => x.HumanEpisodeId == null && x.LastCompletedTurnId != null &&
            x.LastCompletedTurnId != x.SurveyCheckedTurnId).OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id).Take(limit).ToListAsync(ct);
    /// <inheritdoc />
    public Task<Conversation?> FindAsync(string tenant, string id, CancellationToken ct) =>
        (db.Database.CurrentTransaction is null ? db.Set<Conversation>().AsNoTracking() : db.Set<Conversation>())
            .SingleOrDefaultAsync(x => x.TenantId == tenant && x.Id == id, ct);
    /// <inheritdoc />
    public void Add(Conversation value) => db.Add(value);
    /// <inheritdoc />
    public void AddTurn(AiTurn value) => db.Add(value);
    /// <inheritdoc />
    public Task<AiTurn?> TurnAsync(Scope scope, string id, CancellationToken ct) =>
        InScope<AiTurn>(scope).SingleOrDefaultAsync(x => x.Id == id, ct);
    /// <inheritdoc />
    public async Task<List<AiTurn>> TurnsAsync(Scope scope, int limit, CancellationToken ct) =>
        (await InScope<AiTurn>(scope).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(limit).ToListAsync(ct))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToList();
    /// <inheritdoc />
    public void AddMessage(HumanMessage value) => db.Add(value);
    /// <inheritdoc />
    public Task<HumanMessage?> MessageByKeyAsync(Scope scope, string actor, string key, CancellationToken ct) =>
        InScope<HumanMessage>(scope).SingleOrDefaultAsync(x => x.SenderId == actor && x.ClientMsgId == key, ct);
    /// <inheritdoc />
    public Task<List<HumanMessage>> MessagesAsync(Scope scope, long afterSeq, int limit, CancellationToken ct) =>
        InScope<HumanMessage>(scope).Where(x => x.Seq > afterSeq).OrderBy(x => x.Seq).Take(limit).ToListAsync(ct);
    /// <inheritdoc />
    public Task<MessageReceipt?> ReceiptAsync(Scope scope, string actor, CancellationToken ct) =>
        InScope<MessageReceipt>(scope).SingleOrDefaultAsync(x => x.ActorId == actor, ct);
    /// <inheritdoc />
    public void AddReceipt(MessageReceipt value) => db.Add(value);
    /// <inheritdoc />
    public Task<List<Conversation>> PageAsync(string tenant, string org, string assistant, int page, int size, CancellationToken ct) =>
        db.Set<Conversation>().Where(x => x.TenantId == tenant && x.TopOrgId == org && x.AssistantConfigId == assistant)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);

    /// <summary>查询始终绑定租户、组织、会话、助手和大会。</summary>
    private IQueryable<T> InScope<T>(Scope scope) where T : ConversationAggregate =>
        db.Set<T>().Where(x => x.TenantId == scope.TenantId && x.TopOrgId == scope.TopOrgId &&
            x.ConversationId == scope.ConversationId && x.AssistantConfigId == scope.AssistantConfigId && x.BventId == scope.BventId);
}

/// <summary>Customer Service owner 的接待、建议、等待和不可变回执 ORM 仓储。</summary>
/// <param name="db">共享事务上下文。</param>
public sealed class ReceptionRepository(CustomerServiceDbContext db) : IReceptionRepository
{
    /// <inheritdoc />
    public Task<Episode?> LatestAsync(Scope scope, CancellationToken ct) =>
        InScope<Episode>(scope).OrderByDescending(x => x.RequestedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    /// <inheritdoc />
    public Task<Episode?> EpisodeAsync(Scope scope, string id, CancellationToken ct) => InScope<Episode>(scope).SingleOrDefaultAsync(x => x.Id == id, ct);
    /// <inheritdoc />
    public void Add(Episode value) => db.Add(value);
    /// <inheritdoc />
    public Task<List<Episode>> QueueAsync(string tenant, string org, string assistant, IReadOnlyCollection<string> groups, string actor, CancellationToken ct) =>
        db.Set<Episode>().Where(x => x.TenantId == tenant && x.TopOrgId == org && x.AssistantConfigId == assistant &&
            groups.Contains(x.GroupId) && (x.Status == EpisodeStatus.REQUESTED ||
                ((x.Status == EpisodeStatus.CLAIMED || x.Status == EpisodeStatus.ACTIVE) && x.AgentId == actor)))
            .OrderBy(x => x.RequestedAt).ThenBy(x => x.Id).Take(100).ToListAsync(ct);
    /// <inheritdoc />
    public Task<int> ActiveCountAsync(string tenant, string org, string actor, CancellationToken ct) =>
        db.Set<Episode>().CountAsync(x => x.TenantId == tenant && x.TopOrgId == org && x.AgentId == actor &&
            (x.Status == EpisodeStatus.CLAIMED || x.Status == EpisodeStatus.ACTIVE), ct);
    /// <inheritdoc />
    public Task<Waiting?> WaitingAsync(Scope scope, string episode, CancellationToken ct) => InScope<Waiting>(scope).SingleOrDefaultAsync(x => x.EpisodeId == episode, ct);
    /// <inheritdoc />
    public void AddWaiting(Waiting value) => db.Add(value);
    /// <inheritdoc />
    public void AddProposal(Proposal value) => db.Add(value);
    /// <inheritdoc />
    public Task<Proposal?> ProposalAsync(Scope scope, string id, CancellationToken ct) => InScope<Proposal>(scope).SingleOrDefaultAsync(x => x.Id == id, ct);
    /// <inheritdoc />
    public Task<List<Proposal>> PendingAsync(Scope scope, DateTime now, CancellationToken ct) =>
        InScope<Proposal>(scope).Where(x => x.Status == ProposalStatus.PENDING && x.ExpiresAt > now).OrderBy(x => x.CreatedAt).Take(20).ToListAsync(ct);
    /// <inheritdoc />
    public Task<CommandReceipt?> CommandAsync(Scope scope, string actor, string action, string key, CancellationToken ct) =>
        InScope<CommandReceipt>(scope).SingleOrDefaultAsync(x => x.ActorId == actor && x.Action == action && x.RequestId == key, ct);
    /// <inheritdoc />
    public void AddCommand(CommandReceipt value) => db.Add(value);
    /// <summary>所有客服会话聚合均保留完整可信范围。</summary>
    private IQueryable<T> InScope<T>(Scope scope) where T : ConversationAggregate =>
        db.Set<T>().Where(x => x.TenantId == scope.TenantId && x.TopOrgId == scope.TopOrgId &&
            x.ConversationId == scope.ConversationId && x.AssistantConfigId == scope.AssistantConfigId && x.BventId == scope.BventId);
}

/// <summary>路由、日历和在线运行事实的组织隔离 ORM 仓储。</summary>
/// <param name="db">共享事务上下文。</param>
public sealed class ConfigurationRepository(CustomerServiceDbContext db) : IConfigurationRepository
{
    /// <inheritdoc />
    public Task<RoutingConfiguration?> RoutingAsync(string tenant, string org, CancellationToken ct) =>
        db.Set<RoutingConfiguration>().SingleOrDefaultAsync(x => x.TenantId == tenant && x.TopOrgId == org, ct);
    /// <inheritdoc />
    public void AddRouting(RoutingConfiguration value) => db.Add(value);
    /// <inheritdoc />
    public Task<CalendarPolicy?> CalendarAsync(string tenant, string org, CancellationToken ct) =>
        db.Set<CalendarPolicy>().SingleOrDefaultAsync(x => x.TenantId == tenant && x.TopOrgId == org, ct);
    /// <inheritdoc />
    public void AddCalendar(CalendarPolicy value) => db.Add(value);
    /// <inheritdoc />
    public Task<Availability?> AvailabilityAsync(string tenant, string org, string account, CancellationToken ct) =>
        db.Set<Availability>().SingleOrDefaultAsync(x => x.TenantId == tenant && x.TopOrgId == org && x.Id == account, ct);
    /// <inheritdoc />
    public void AddAvailability(Availability value) => db.Add(value);
}

/// <summary>原跟进表单及最终回复的隔离 ORM 仓储，不直接操作 Conversation 表。</summary>
/// <param name="db">共享事务上下文。</param>
public sealed class FollowupRepository(CustomerServiceDbContext db) : IFollowupRepository
{
    /// <inheritdoc />
    public Task<Followup?> FindAsync(Scope s, string episode, CancellationToken ct) =>
        db.Set<Followup>().SingleOrDefaultAsync(x => x.TenantId == s.TenantId && x.TopOrgId == s.TopOrgId &&
            x.ConversationId == s.ConversationId && x.EpisodeId == episode && x.AssistantConfigId == s.AssistantConfigId && x.BventId == s.BventId, ct);
    /// <inheritdoc />
    public void Add(Followup value) => db.Add(value);
    /// <inheritdoc />
    public Task<FollowupResponse?> ResponseAsync(Scope s, string episode, CancellationToken ct) =>
        db.Set<FollowupResponse>().SingleOrDefaultAsync(x => x.TenantId == s.TenantId && x.TopOrgId == s.TopOrgId &&
            x.ConversationId == s.ConversationId && x.EpisodeId == episode && x.AssistantConfigId == s.AssistantConfigId && x.BventId == s.BventId, ct);
    /// <inheritdoc />
    public void AddResponse(FollowupResponse value) => db.Add(value);
    /// <inheritdoc />
    public Task<List<Followup>> QueueAsync(string tenant, string org, string assistant, IReadOnlyCollection<string> groups, CancellationToken ct) =>
        db.Set<Followup>().Where(x => x.TenantId == tenant && x.TopOrgId == org && x.AssistantConfigId == assistant && groups.Contains(x.GroupId))
            .OrderBy(x => x.SubmittedAt).ThenBy(x => x.Id).Take(100).ToListAsync(ct);
}

/// <summary>调查 owner 的一会话一调查 ORM 仓储。</summary>
/// <param name="db">共享事务上下文。</param>
public sealed class SurveyRepository(CustomerServiceDbContext db) : ISurveyRepository
{
    /// <inheritdoc />
    public void AddEvidence(ResolutionEvidence value) => db.Add(value);
    /// <inheritdoc />
    public Task<ResolutionEvidence?> EvidenceAsync(Scope s, string turn, string kind, CancellationToken ct) =>
        db.Set<ResolutionEvidence>().SingleOrDefaultAsync(x => x.TenantId == s.TenantId && x.TopOrgId == s.TopOrgId &&
            x.ConversationId == s.ConversationId && x.AssistantConfigId == s.AssistantConfigId && x.BventId == s.BventId &&
            x.TurnId == turn && x.Kind == kind, ct);
    /// <inheritdoc />
    public Task<Survey?> FindAsync(Scope s, CancellationToken ct) =>
        db.Set<Survey>().SingleOrDefaultAsync(x => x.TenantId == s.TenantId && x.TopOrgId == s.TopOrgId &&
            x.ConversationId == s.ConversationId && x.AssistantConfigId == s.AssistantConfigId && x.BventId == s.BventId, ct);
    /// <inheritdoc />
    public void Add(Survey value) => db.Add(value);
}

/// <summary>可靠投递任务 ORM 仓储，领取前过滤通道并保留租户组织条件。</summary>
/// <param name="db">共享事务上下文。</param>
public sealed class TaskRepository(CustomerServiceDbContext db) : ITaskRepository
{
    /// <inheritdoc />
    public Task<ReliableTask?> FindAsync(string tenant, string org, string key, CancellationToken ct) =>
        db.Set<ReliableTask>().SingleOrDefaultAsync(x => x.TenantId == tenant && x.TopOrgId == org && x.EventKey == key, ct);
    /// <inheritdoc />
    public void Add(ReliableTask value) => db.Add(value);
    /// <inheritdoc />
    public Task<List<ReliableTask>> DueAsync(IReadOnlyCollection<string> kinds, DateTime now, int limit, CancellationToken ct) =>
        db.Set<ReliableTask>().AsNoTracking().Where(x => kinds.Contains(x.Kind) &&
            ((x.State == DeliveryState.PENDING && x.DueAt <= now) || (x.State == DeliveryState.CLAIMED && x.LeaseUntil <= now)))
            .OrderBy(x => x.DueAt).ThenBy(x => x.Id).Take(limit).ToListAsync(ct);
    /// <inheritdoc />
    public Task<List<ReliableTask>> ListAsync(string tenant, string org, string kind, CancellationToken ct) =>
        db.Set<ReliableTask>().Where(x => x.TenantId == tenant && x.TopOrgId == org && x.Kind == kind)
            .OrderByDescending(x => x.CreatedAt).Take(1000).ToListAsync(ct);
}
