using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace AI.Customer.Service.Infrastructure.Adapters;

/// <summary>
/// 从服务端受控配置读取助手及其可信归属。
/// </summary>
/// <param name="options"> 部署维护的最小运行定义。 </param>
public sealed class ConfiguredAssistantRegistry(IOptions<AssistantOptions> options) : IAssistantRegistry
{
    /// <summary>
    /// 查找明确允许匿名访问的助手，匹配结果不是唯一一项时拒绝请求。
    /// </summary>
    public AssistantDefinition RequirePublic(string assistant)
    {
        var candidates = options.Value.Items.Where(x => x.Id == assistant && x.AllowAnonymous).ToArray();
        return candidates.Length == 1 ? candidates[0] : throw BusinessException.Forbidden("助手未开放匿名咨询");
    }
    /// <summary>
    /// 从受控配置读取指定租户的助手，助手未注册时拒绝请求。
    /// </summary>
    public AssistantDefinition Require(string assistant) =>
        options.Value.Items.SingleOrDefault(x => x.Id == assistant)
        ?? throw BusinessException.Forbidden("助手未注册");

    /// <summary>
    /// 列举受控配置中属于指定租户的助手。
    /// </summary>
    public IReadOnlyList<AssistantDefinition> List() => options.Value.Items.ToArray();
}
