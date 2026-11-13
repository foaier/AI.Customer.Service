namespace AI.Customer.Service.Domain.Models;

/// <summary>由认证和持久会话绑定的可信业务范围，客户端不得覆盖归属。</summary>
/// <param name="TenantId">认证租户。</param>
/// <param name="TopOrgId">服务端核验的组织。</param>
/// <param name="ConversationId">AI 与人工共享的会话标识。</param>
/// <param name="AssistantConfigId">可信助手配置引用。</param>
/// <param name="BventId">可空的大会引用。</param>
public sealed record Scope(string TenantId, string TopOrgId, string ConversationId, string AssistantConfigId, string? BventId);

/// <summary>经 JWT 和用户中心确认的请求主体，不保存令牌。</summary>
/// <param name="TenantId">认证租户。</param>
/// <param name="AccountId">认证账号。</param>
/// <param name="TraceId">当前命令的审计关联标识。</param>
public sealed record Actor(string TenantId, string AccountId, string TraceId);

/// <summary>全部持久聚合共同具备的隔离范围及并发版本，不包含伪造审计字段。</summary>
public abstract class Aggregate
{
    /// <summary>owner 内唯一业务标识，与租户、组织共同构成数据库主键。</summary>
    public string Id { get; set; } = "";

    /// <summary>服务端确定的租户，参与每次读写条件。</summary>
    public string TenantId { get; set; } = "";

    /// <summary>服务端核验的组织，参与每次读写条件。</summary>
    public string TopOrgId { get; set; } = "";

    /// <summary>从一递增的乐观并发版本，幂等重放不得递增。</summary>
    public long Version { get; set; } = 1;
}

/// <summary>以同一 conversation 为边界的业务聚合基础信息。</summary>
public abstract class ConversationAggregate : Aggregate
{
    /// <summary>贯穿 AI、人工、调查及异步任务的会话引用。</summary>
    public string ConversationId { get; set; } = "";

    /// <summary>服务端配置的助手引用，不随模型输出变化。</summary>
    public string AssistantConfigId { get; set; } = "";

    /// <summary>来源大会引用，空表示非展会场景。</summary>
    public string? BventId { get; set; }

    /// <summary>原会话的访客账号，仅用于归属校验。</summary>
    public string PrincipalId { get; set; } = "";

    /// <summary>取得跨 owner 端口使用的不可变完整范围。</summary>
    public Scope Scope => new(TenantId, TopOrgId, ConversationId, AssistantConfigId, BventId);
}
