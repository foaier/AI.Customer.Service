using System.ClientModel.Primitives;
using AI.Customer.Service.Domain.Models;

namespace AI.Customer.Service.Core.Adapters;

/// <summary>向本轮所有模型请求（含工具续轮）传播可信上下文，不依赖线程本地变量。</summary>
/// <param name="scope">服务端绑定的完整会话范围。</param>
/// <param name="turnId">生成轮次；独立分类时为空。</param>
/// <param name="traceId">当前生成跟踪；独立分类时为空。</param>
/// <param name="anonymous">无密钥兼容端点移除 SDK 占位认证头。</param>
internal sealed class ModelContextPolicy(Scope scope, string? turnId, string? traceId, bool anonymous) : PipelinePolicy
{
    /// <inheritdoc />
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        AddHeaders(message.Request.Headers);
        ProcessNext(message, pipeline, currentIndex);
    }

    /// <inheritdoc />
    public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        AddHeaders(message.Request.Headers);
        return ProcessNextAsync(message, pipeline, currentIndex);
    }

    /// <summary>仅设置当前运行的元数据，API 密钥不进入日志、工具或会话历史。</summary>
    private void AddHeaders(PipelineRequestHeaders headers)
    {
        headers.Set("X-Tenant-Id", scope.TenantId);
        headers.Set("X-Top-Org-Id", scope.TopOrgId);
        headers.Set("X-Session-Id", scope.ConversationId);
        headers.Set("X-Assistant-Config-Id", scope.AssistantConfigId);
        if (scope.BventId is not null)
        {
            headers.Set("X-Bvent-Id", scope.BventId);
        }
        if (turnId is not null)
        {
            headers.Set("X-Turn-Id", turnId);
        }
        if (traceId is not null)
        {
            headers.Set("X-Trace-Id", traceId);
        }
        if (anonymous)
        {
            headers.Remove("Authorization");
        }
    }
}
