using AI.Customer.Service.Common;
using AI.Customer.Service.Core;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;

namespace AI.Customer.Service.Service;

/// <summary>统一认证后准入，外部账号核验在事务外，本地成员权限在事务内重新读取。</summary>
/// <param name="accounts">用户中心只读端口。</param>
/// <param name="configuration">本地业务组仓储。</param>
/// <param name="assistants">可信助手注册表。</param>
/// <param name="runtime">Conversation owner 的范围端口。</param>
public sealed class AccessService(IAccountDirectory accounts, IConfigurationRepository configuration, IAssistantRegistry assistants, ConversationRuntime runtime)
{
    /// <summary>即时核验用户中心账号和组织启用，设置权限不等于接单资格。</summary>
    public Task<Member> RequireAccountAsync(Actor actor, string org, CancellationToken ct) =>
        accounts.RequireAsync(actor.TenantId, Guard.Text(org, "组织", 64), actor.AccountId, ct);

    /// <summary>事务外核验客服外部资格及可信助手归属。</summary>
    public async Task RequireAgentAsync(Actor actor, string org, string assistant, CancellationToken ct)
    {
        RequireAssistant(actor, org, assistant);
        await RequireAccountAsync(actor, org, ct);
        _ = await GroupsAsync(actor, org, ct);
    }

    /// <summary>每次业务操作读取本地成员，配置撤销后下一次读写立即拒绝。</summary>
    public async Task<HashSet<string>> GroupsAsync(Actor actor, string org, CancellationToken ct)
    {
        var current = await configuration.RoutingAsync(actor.TenantId, org, ct);
        var groups = current?.Groups.Where(g => g.Members.Any(m => m.AccountId == actor.AccountId)).Select(g => g.Id).ToHashSet() ?? [];
        if (groups.Count == 0)
        {
            throw BusinessException.Forbidden("账号未关联本组织客服业务组");
        }
        return groups;
    }

    /// <summary>按原周期冻结组授权，后续修改助手映射不能授予其它组历史权限。</summary>
    public async Task RequireGroupAsync(Actor actor, Episode episode, CancellationToken ct)
    {
        if (actor.TenantId != episode.TenantId || !(await GroupsAsync(actor, episode.TopOrgId, ct)).Contains(episode.GroupId))
        {
            throw BusinessException.Forbidden("该接待不属于当前客服组");
        }
    }

    /// <summary>校验助手的可信租户和目标组织，不信任 URL 中的组织字段。</summary>
    public AssistantDefinition RequireAssistant(Actor actor, string org, string assistant)
    {
        var value = assistants.Require(actor.TenantId, assistant);
        if (value.TopOrgId != org)
        {
            throw BusinessException.Forbidden("助手不属于目标组织");
        }
        return value;
    }

    /// <summary>访客从持久会话解析完整范围，客户端不能指定归属。</summary>
    public async Task<Scope> VisitorAsync(Actor actor, string conversation, CancellationToken ct) =>
        (await runtime.VisitorAsync(actor, conversation, ct)).Scope;

    /// <summary>客服按目标组织读取持久范围，后续仍需冻结组及认领校验。</summary>
    public async Task<Scope> AgentScopeAsync(Actor actor, string org, string conversation, IConversationRepository conversations, CancellationToken ct)
    {
        var value = await conversations.FindAsync(actor.TenantId, conversation, ct);
        if (value is null || value.TopOrgId != org)
        {
            throw BusinessException.Forbidden("会话不存在或不属于目标组织");
        }
        RequireAssistant(actor, org, value.AssistantConfigId);
        return value.Scope;
    }
}
