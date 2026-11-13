using System.ComponentModel;
using System.Text.Json;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using Microsoft.Extensions.AI;

namespace AI.Customer.Service.Core.Adapters;

/// <summary>由 Agent Framework 实际调用的受限工具；仅收集本轮候选，无仓储或外部写入权限。</summary>
internal sealed class AgentProposalTools
{
    /// <summary>当前运行的候选，完整生成成功前不会交给应用层。</summary>
    public List<ModelDelta> Candidates { get; } = [];

    /// <summary>将明确业务动作注册为框架可执行函数，框架负责参数绑定、执行与结果续轮。</summary>
    public IList<AITool> CreateTools() =>
    [
        AIFunctionFactory.Create(ProposeHandoff, "proposeHandoff", "建议人工，需要访客明确确认，工具不会申请人工。"),
        AIFunctionFactory.Create(ProposeResolution, "proposeResolution", "记录可能解决的候选，不能关闭会话或代替用户评价。"),
        AIFunctionFactory.Create(RequestHandoff, "requestHandoff", "检查转人工授权边界；模型不能代替访客授权，明确请求由业务入口处理。")
    ];

    /// <summary>保存合法人工建议到本轮内存，实际建议卡须通过成功轮次围栏后创建。</summary>
    private string ProposeHandoff(
        [Description("建议原因枚举，如 KNOWLEDGE_GAP、LOW_CONFIDENCE、COMPLEX_CASE，不能使用系统或用户授权原因。")] string reasonCode,
        [Description("建议置信度，0到1。")] double confidence,
        [Description("供访客确认的简短原因，最多500字符，不含URL。")] string userFacingReason)
    {
        if (!Enum.TryParse<ReasonCode>(reasonCode, out var reason) || !Enum.IsDefined(reason) ||
            reason is ReasonCode.USER_REQUEST or ReasonCode.SYSTEM_FAILURE or ReasonCode.SAFETY_OR_URGENT or ReasonCode.SURVEY_DISSATISFIED ||
            !ValidConfidence(confidence) || string.IsNullOrWhiteSpace(userFacingReason) || userFacingReason.Length > 500 ||
            userFacingReason.Contains("://", StringComparison.Ordinal))
        {
            return "INVALID_PROPOSAL: 建议参数无效，未创建人工申请。";
        }
        Candidates.Add(new(null, "proposeHandoff", JsonSerializer.Serialize(new { reasonCode, confidence, userFacingReason })));
        return "PROPOSAL_ONLY: 已记录本轮候选，需完整回答成功后生成建议卡，并由访客确认，尚未申请人工。";
    }

    /// <summary>只记录解决候选，不能产生调查、用户答案或会话结束事实。</summary>
    private string ProposeResolution([Description("问题可能解决的置信度，0到1。")] double confidence)
    {
        if (!ValidConfidence(confidence))
        {
            return "INVALID_PROPOSAL: 置信度必须为0到1。";
        }
        Candidates.Add(new(null, "proposeResolution", JsonSerializer.Serialize(new { confidence })));
        return "PROPOSAL_ONLY: 仅记录候选，仍需真实用户结束证据，不代表已关闭、已评价或已创建调查。";
    }

    /// <summary>明确拒绝模型自行申请人工；原话授权和按钮申请由应用入口独立处理。</summary>
    private static string RequestHandoff() => "USER_CONSENT_REQUIRED: 模型无权创建人工申请。请访客使用转人工按钮或明确提出请求。";

    /// <summary>置信度只接受有限的0到1数值，阈值由业务 owner 再次判定。</summary>
    private static bool ValidConfidence(double value) => double.IsFinite(value) && value is >= 0 and <= 1;
}
