using AI.Customer.Service.Domain.Models;

namespace AI.Customer.Service.Domain.Ports;

/// <summary>用户中心只读防腐端口，本地业务组不能替代外部账号组织启用关系。</summary>
public interface IAccountDirectory
{
    /// <summary>实时核验账号、租户及目标组织启用关系。</summary>
    Task<Member> RequireAsync(string tenant, string org, string account, CancellationToken ct);
    /// <summary>分页读取用户中心既有人员组。</summary>
    Task<DirectoryPage<DirectoryGroup>> GroupsAsync(string tenant, string org, string? keyword, string? cursor, int limit, CancellationToken ct);
    /// <summary>先核验组归属，再分页读取最小账号候选。</summary>
    Task<DirectoryPage<Member>> AccountsAsync(string tenant, string org, string group, string? keyword, string? cursor, int limit, CancellationToken ct);
}

/// <summary>可信助手注册端口，组织和大会信息只能来自服务端配置。</summary>
public interface IAssistantRegistry
{
    /// <summary>读取属于指定租户的可信助手，未知助手拒绝。</summary>
    AssistantDefinition Require(string tenant, string assistant);
    /// <summary>列举指定租户可选择的可信助手。</summary>
    IReadOnlyList<AssistantDefinition> List(string tenant);
}

/// <summary>部署配置或助手服务维护的运行定义，不包含模型密钥。</summary>
/// <param name="Id">助手配置引用。</param>
/// <param name="TenantId">数据归属租户。</param>
/// <param name="TopOrgId">数据归属组织。</param>
/// <param name="BventId">大会引用。</param>
/// <param name="Name">助手展示名。</param>
/// <param name="Language">默认语言。</param>
public sealed record AssistantDefinition(string Id, string TenantId, string TopOrgId, string? BventId = null, string Name = "智能客服", string Language = "zh-CN");

/// <summary>模型协议隔离端口，业务状态只能由应用服务推进。</summary>
public interface IChatModel
{
    /// <summary>生成文本增量及受限工具结果，显式传播完整范围和轮次。</summary>
    IAsyncEnumerable<ModelDelta> StreamAsync(ModelRequest request, CancellationToken ct);
    /// <summary>只识别主题，失败或低置信时由适配器降 OTHER。</summary>
    Task<Classification> ClassifyAsync(Scope scope, string context, CancellationToken ct);
}

/// <summary>模型输入，可信范围单独绑定，模型不得覆盖。</summary>
/// <param name="Scope">完整可信范围。</param>
/// <param name="TurnId">服务端 AI 轮次。</param>
/// <param name="TraceId">跨调用追踪标识。</param>
/// <param name="History">最小成功历史。</param>
/// <param name="UserInput">当前原始用户输入。</param>
public sealed record ModelRequest(Scope Scope, string TurnId, string TraceId, IReadOnlyList<AiTurn> History, string UserInput);

/// <summary>模型的文本或受限工具候选，工具字段仍需服务端验证。</summary>
/// <param name="Text">可展示文本增量。</param>
/// <param name="ToolName">白名单工具名。</param>
/// <param name="Arguments">工具 JSON 参数，不含可信身份。</param>
public sealed record ModelDelta(string? Text, string? ToolName = null, string? Arguments = null);

/// <summary>外部通知协议端口，受理不等于最终送达。</summary>
public interface INotificationPort
{
    /// <summary>返回真实可发送的任务类型，关闭时 Worker 不查相应任务。</summary>
    IReadOnlyCollection<string> SupportedKinds { get; }
    /// <summary>按稳定 eventKey 投递并验证网关明确受理，在业务事务外执行。</summary>
    Task SendAsync(ReliableTask task, CancellationToken ct);
}
