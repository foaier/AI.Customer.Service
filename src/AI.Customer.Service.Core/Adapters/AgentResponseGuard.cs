using System.Runtime.CompilerServices;
using System.Text.Json;
using AI.Customer.Service.Common;
using Microsoft.Extensions.AI;

namespace AI.Customer.Service.Core.Adapters;

/// <summary>在框架工具循环的每次模型响应边界校验完整性和参数白名单，失败不得提交候选。</summary>
/// <param name="innerClient">官方 OpenAI provider 的类型化聊天客户端。</param>
internal sealed class AgentResponseGuard(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    /// <summary>本轮已出现的工具标识，防止重复调用与超过八个工具。</summary>
    private readonly HashSet<string> calls = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        RequireComplete(response.FinishReason);
        foreach (var message in response.Messages)
        {
            ValidateTools(message.Contents);
        }
        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatFinishReason? finish = null;
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            if (update.FinishReason is { } reason)
            {
                RequireComplete(reason);
                finish = reason;
            }
            ValidateTools(update.Contents);
            yield return update;
        }
        RequireComplete(finish);
    }

    /// <summary>仅 stop 和 tool_calls 表示完整服务响应；截断、过滤和缺失结束信号一律失败。</summary>
    private static void RequireComplete(ChatFinishReason? reason)
    {
        if (reason != ChatFinishReason.Stop && reason != ChatFinishReason.ToolCalls)
        {
            throw BusinessException.Unavailable("模型响应未完整结束");
        }
    }

    /// <summary>工具参数在自动绑定前核验，模型不能借助额外字段携带身份或越权状态。</summary>
    private void ValidateTools(IEnumerable<AIContent> contents)
    {
        foreach (var call in contents.OfType<FunctionCallContent>())
        {
            if (string.IsNullOrWhiteSpace(call.CallId) || !calls.Add(call.CallId) || calls.Count > 8 || call.Exception is not null)
            {
                throw BusinessException.Unavailable("模型工具标识、数量或参数无效");
            }
            string[] allowed = call.Name switch
            {
                "proposeHandoff" => ["reasonCode", "confidence", "userFacingReason"],
                "proposeResolution" => ["confidence"],
                "requestHandoff" => [],
                _ => throw BusinessException.Unavailable("模型返回未授权工具")
            };
            var arguments = call.Arguments;
            if (arguments is null && allowed.Length > 0 || arguments is not null &&
                (arguments.Count != allowed.Length || arguments.Keys.Any(key => !allowed.Contains(key, StringComparer.Ordinal)) ||
                 JsonSerializer.Serialize(arguments).Length > 4000))
            {
                throw BusinessException.Unavailable("模型工具参数不符合白名单");
            }
            if (arguments is not null)
            {
                var json = JsonSerializer.SerializeToElement(arguments);
                foreach (var name in allowed)
                {
                    var value = json.GetProperty(name);
                    var valid = name == "confidence"
                        ? value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)
                        : value.ValueKind == JsonValueKind.String;
                    if (!valid)
                    {
                        throw BusinessException.Unavailable("模型工具参数类型无效");
                    }
                }
            }
        }
    }
}
