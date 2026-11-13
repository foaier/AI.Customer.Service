using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace AI.Customer.Service.Infrastructure.Adapters;

/// <summary>从服务端受控配置读取助手，不复制全部 Java 助手配置模块。</summary>
/// <param name="options">部署维护的最小运行定义。</param>
public sealed class ConfiguredAssistantRegistry(IOptions<AssistantOptions> options) : IAssistantRegistry
{
    /// <inheritdoc />
    public AssistantDefinition Require(string tenant, string assistant) =>
        options.Value.Items.SingleOrDefault(x => x.TenantId == tenant && x.Id == assistant)
        ?? throw BusinessException.Forbidden("助手未在当前租户注册");

    /// <inheritdoc />
    public IReadOnlyList<AssistantDefinition> List(string tenant) =>
        options.Value.Items.Where(x => x.TenantId == tenant).ToArray();
}
