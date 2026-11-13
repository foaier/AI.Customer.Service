using System.Text.RegularExpressions;
using AI.Customer.Service.Domain.Models;

namespace AI.Customer.Service.Domain.Policies;

/// <summary>从 Java explicit-resolution-evidence-v4 迁移的保守结束资格规则，无网络及数据库操作。</summary>
public static class SurveyContentPolicy
{
    /// <summary>完整成功历史、真实业务问题和明确结束证据全部满足才返回证据类型。</summary>
    public static string? Assess(IReadOnlyList<AiTurn> turns)
    {
        if (turns.Count is < 2 or > 100 || turns.Any(t => t.Status != "SUCCEEDED" || t.AssistantContent is null))
        {
            return null;
        }
        var all = string.Join("\n", turns.Select(t => t.UserContent));
        if (Regex.IsMatch(all, "广告|推广|代开发|加微信|博彩|刷单|spam|没有解决|没解决|未解决|不准确|错误|不对|不知道|无法解决|人工客服", RegexOptions.IgnoreCase) ||
            turns.Any(t => Regex.IsMatch(t.AssistantContent!, "暂时没有|无法提供|无法直接|未能查到|资料不足|信息不足|不能确定")))
        {
            return null;
        }
        var input = turns[^1].UserContent.Trim().TrimEnd('。', '！', '!', '.', ' ');
        var explicitEnd = Regex.IsMatch(input, "^(问题(已经|已)?解决了?|已经解决了|已解决|解决了|没有其他问题了?|没有别的问题了?)$");
        var previous = turns[^2].AssistantContent!;
        var confirmed = Regex.IsMatch(input, "^(没有|没有了|不用了|不需要了)$") &&
            Regex.IsMatch(previous, "(还有|其他|其它).*(问题|帮|帮助|需要).*[?？]");
        var question = turns.Take(turns.Count - 1).Any(t => t.UserContent.Length >= 6 &&
            Regex.IsMatch(t.UserContent, "[?？]|如何|怎么|哪里|什么|多少|是否|能否|可以|请问"));
        return question && (explicitEnd || confirmed)
            ? (explicitEnd ? "USER_EXPLICIT_END" : "USER_CONFIRMED_NO_FURTHER_QUESTION") + ":explicit-resolution-evidence-v4"
            : null;
    }
}
