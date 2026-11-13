namespace AI.Customer.Service.Domain.Models;

/// <summary>客服 owner 的组织级分组、成员关联和助手路由，整体版本提交。</summary>
public sealed class RoutingConfiguration : Aggregate
{
    /// <summary>四类本地接待组，账号只保存用户中心引用。</summary>
    public List<ReceptionGroup> Groups { get; set; } = [];

    /// <summary>每个助手的四类主题到本地组的映射。</summary>
    public List<AssistantRoute> Routes { get; set; } = [];
}

/// <summary>本地接待组，独立于用户中心人员候选分组。</summary>
/// <param name="Id">稳定组引用。</param>
/// <param name="Category">固定主题类别。</param>
/// <param name="Name">可配置展示名。</param>
/// <param name="Members">经实时外部核验的账号引用。</param>
public sealed record ReceptionGroup(string Id, Category Category, string Name, List<Member> Members);

/// <summary>已核验的外部账号引用，不包含邮箱或电话。</summary>
/// <param name="AccountId">用户中心账号。</param>
/// <param name="DisplayName">权威来源的展示名快照，不授予权限。</param>
public sealed record Member(string AccountId, string DisplayName);

/// <summary>助手四类咨询主题的持久映射。</summary>
/// <param name="AssistantConfigId">服务端可信助手。</param>
/// <param name="Groups">主题到本地组的完整映射。</param>
public sealed record AssistantRoute(string AssistantConfigId, Dictionary<Category, string> Groups);

/// <summary>模型只能提供主题分类，不能决定实际组或身份。</summary>
/// <param name="Category">分类结果。</param>
/// <param name="Confidence">零至一置信度。</param>
/// <param name="Source">MODEL 或明确降级原因。</param>
/// <param name="Policy">策略版本。</param>
public sealed record Classification(Category Category, double Confidence, string Source, string Policy = "context-semantic-v1");

/// <summary>外部用户中心最小目录分页，不可用时不能返回伪空列表。</summary>
/// <param name="Status">READY 或 UNAVAILABLE。</param>
/// <param name="Items">已做范围核验的最小候选。</param>
/// <param name="NextCursor">绑定当前查询范围的游标。</param>
/// <param name="HasMore">是否有下一页。</param>
public sealed record DirectoryPage<T>(string Status, IReadOnlyList<T> Items, string? NextCursor, bool HasMore);

/// <summary>用户中心维护的只读人员候选组。</summary>
/// <param name="GroupId">外部分组引用。</param>
/// <param name="Name">外部分组展示名。</param>
public sealed record DirectoryGroup(string GroupId, string Name);
