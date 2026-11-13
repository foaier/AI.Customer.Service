using AI.Customer.Service.Domain.Models;

namespace AI.Customer.Service.Domain.Ports;

/// <summary>同数据源短事务端口，归属写锁、聚合、回执和待投递事件必须原子提交。</summary>
public interface IUnitOfWork
{
    /// <summary>在指定租户组织的串行写入边界内执行本地业务，不允许远程调用。</summary>
    Task<T> ExecuteAsync<T>(string tenant, string org, Func<Task<T>> action, CancellationToken ct);
}

/// <summary>Conversation owner 的唯一会话、轮次及消息写入端口。</summary>
public interface IConversationRepository
{
    /// <summary>恢复成功轮次的调查资格评估，已检查轮次不重复扫描。</summary>
    Task<List<Conversation>> SurveyCandidatesAsync(int limit, CancellationToken ct);
    /// <summary>在可信租户内查找会话，后续必须校验组织及主体。</summary>
    Task<Conversation?> FindAsync(string tenant, string id, CancellationToken ct);
    /// <summary>新增共享 AI/人工会话。</summary>
    void Add(Conversation conversation);
    /// <summary>登记真实 AI 轮次。</summary>
    void AddTurn(AiTurn turn);
    /// <summary>按完整范围查找轮次。</summary>
    Task<AiTurn?> TurnAsync(Scope scope, string turn, CancellationToken ct);
    /// <summary>读取最多指定数量的真实 AI 历史。</summary>
    Task<List<AiTurn>> TurnsAsync(Scope scope, int limit, CancellationToken ct);
    /// <summary>保存单一时间线的人工消息。</summary>
    void AddMessage(HumanMessage message);
    /// <summary>同主体稳定消息键查询，用于内容幂等。</summary>
    Task<HumanMessage?> MessageByKeyAsync(Scope scope, string actor, string key, CancellationToken ct);
    /// <summary>按持久序列分页恢复人工消息。</summary>
    Task<List<HumanMessage>> MessagesAsync(Scope scope, long afterSeq, int limit, CancellationToken ct);
    /// <summary>读取当前主体的回执水位。</summary>
    Task<MessageReceipt?> ReceiptAsync(Scope scope, string actor, CancellationToken ct);
    /// <summary>首次建立主体回执。</summary>
    void AddReceipt(MessageReceipt receipt);
    /// <summary>分页导出指定助手的会话，不暴露其它组织。</summary>
    Task<List<Conversation>> PageAsync(string tenant, string org, string assistant, int page, int size, CancellationToken ct);
}

/// <summary>客服 owner 的接待、等待、建议和命令回执端口。</summary>
public interface IReceptionRepository
{
    /// <summary>读取完整范围内最新周期，无记录保持空。</summary>
    Task<Episode?> LatestAsync(Scope scope, CancellationToken ct);
    /// <summary>固定原周期，不随后续申请变化。</summary>
    Task<Episode?> EpisodeAsync(Scope scope, string episode, CancellationToken ct);
    /// <summary>创建接待周期。</summary>
    void Add(Episode episode);
    /// <summary>按助手和当前权限组过滤队列。</summary>
    Task<List<Episode>> QueueAsync(string tenant, string org, string assistant, IReadOnlyCollection<string> groups, string actor, CancellationToken ct);
    /// <summary>统计同组织所有助手的当前接待容量。</summary>
    Task<int> ActiveCountAsync(string tenant, string org, string actor, CancellationToken ct);
    /// <summary>读取原周期等待事实。</summary>
    Task<Waiting?> WaitingAsync(Scope scope, string episode, CancellationToken ct);
    /// <summary>首次建立等待窗口。</summary>
    void AddWaiting(Waiting waiting);
    /// <summary>保存受限工具建议卡。</summary>
    void AddProposal(Proposal proposal);
    /// <summary>读取固定建议卡。</summary>
    Task<Proposal?> ProposalAsync(Scope scope, string id, CancellationToken ct);
    /// <summary>恢复有效建议卡。</summary>
    Task<List<Proposal>> PendingAsync(Scope scope, DateTime now, CancellationToken ct);
    /// <summary>读取首次命令回执，同键仍需比较摘要。</summary>
    Task<CommandReceipt?> CommandAsync(Scope scope, string actor, string action, string key, CancellationToken ct);
    /// <summary>不可变地登记成功命令回执。</summary>
    void AddCommand(CommandReceipt receipt);
}

/// <summary>本地路由和营业日历 owner，账号资料只由外部端口核验。</summary>
public interface IConfigurationRepository
{
    /// <summary>读取组织整体路由配置。</summary>
    Task<RoutingConfiguration?> RoutingAsync(string tenant, string org, CancellationToken ct);
    /// <summary>首次保存本地业务组配置。</summary>
    void AddRouting(RoutingConfiguration configuration);
    /// <summary>读取组织日历，空时由应用使用部署默认。</summary>
    Task<CalendarPolicy?> CalendarAsync(string tenant, string org, CancellationToken ct);
    /// <summary>首次保存组织营业日历。</summary>
    void AddCalendar(CalendarPolicy calendar);
    /// <summary>读取组织级账号在线行。</summary>
    Task<Availability?> AvailabilityAsync(string tenant, string org, string account, CancellationToken ct);
    /// <summary>首次建立默认离线运行行，不自动上线。</summary>
    void AddAvailability(Availability availability);
}

/// <summary>原跟进工单的表单、认领和最终回复端口。</summary>
public interface IFollowupRepository
{
    /// <summary>固定原周期查询留言。</summary>
    Task<Followup?> FindAsync(Scope scope, string episode, CancellationToken ct);
    /// <summary>保存访客明确提交的留言。</summary>
    void Add(Followup form);
    /// <summary>查询原周期最终回复。</summary>
    Task<FollowupResponse?> ResponseAsync(Scope scope, string episode, CancellationToken ct);
    /// <summary>保存最终回复正文，不能在发送前显示已处理。</summary>
    void AddResponse(FollowupResponse response);
    /// <summary>仅返回有资格组的留言工单，应用投影去除正文。</summary>
    Task<List<Followup>> QueueAsync(string tenant, string org, string assistant, IReadOnlyCollection<string> groups, CancellationToken ct);
}

/// <summary>Conversation owner 的唯一满意度事实端口。</summary>
public interface ISurveyRepository
{
    /// <summary>记录模型候选或确定性用户结束证据，固定真实轮次和当前代次。</summary>
    void AddEvidence(ResolutionEvidence evidence);
    /// <summary>恢复同轮同类证据用于同参数幂等。</summary>
    Task<ResolutionEvidence?> EvidenceAsync(Scope scope, string turn, string kind, CancellationToken ct);
    /// <summary>一会话最多一份调查，查询不创建。</summary>
    Task<Survey?> FindAsync(Scope scope, CancellationToken ct);
    /// <summary>保存经过真实结束证据门禁的调查。</summary>
    void Add(Survey survey);
}

/// <summary>事务内登记和事务外投递之间的可靠任务存储端口。</summary>
public interface ITaskRepository
{
    /// <summary>按稳定业务键查找任务。</summary>
    Task<ReliableTask?> FindAsync(string tenant, string org, string eventKey, CancellationToken ct);
    /// <summary>业务事务内保存待投递事实。</summary>
    void Add(ReliableTask task);
    /// <summary>在领取前过滤已启用通道，未启用通道不消耗尝试。</summary>
    Task<List<ReliableTask>> DueAsync(IReadOnlyCollection<string> kinds, DateTime now, int limit, CancellationToken ct);
    /// <summary>查询某组织的任务用于指标和人工恢复。</summary>
    Task<List<ReliableTask>> ListAsync(string tenant, string org, string kind, CancellationToken ct);
}
