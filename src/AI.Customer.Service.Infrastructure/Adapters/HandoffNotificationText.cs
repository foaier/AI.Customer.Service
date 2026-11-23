using System.Text;
using AI.Customer.Service.Domain.Models;

namespace AI.Customer.Service.Infrastructure.Adapters;

/// <summary>
/// 两个聊天通道共用固定纯文本模板，保留完整工作台链接，遵守 2048 UTF-8 字节上限。
/// </summary>
public static class HandoffNotificationText
{
    /// <summary>
    /// 只读取任务快照；缺失资料显示占位符，不推断或查询用户画像。
    /// </summary>
    public static string Render(ReliableTask task, HandoffNotificationSnapshot snapshot, DateTime now)
    {
        var root = snapshot.PublicBaseUrl;
        if (root.Length > 256 || !Uri.TryCreate(root, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Host.Length == 0 || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new NotificationDeliveryException("CONFIG_PUBLIC_URL_INVALID", false);
        var url = root.TrimEnd('/') + "/b/inbox?assistantConfigId=" + Uri.EscapeDataString(task.AssistantConfigId) +
            "&conversationId=" + Uri.EscapeDataString(task.ConversationId);
        var category = snapshot.NotificationCategory switch
        {
            "SALES" => "参展咨询/展位销售（SALES）等展商相关",
            "VISITOR" => "观众咨询/预登记等观众相关（VISITOR）",
            "FINANCE" => "付款/发票/账单等财务相关（FINANCE）",
            _ => "[—]"
        };
        var prefix = $"【实时转接请求】｜{category}\n渠道：网站    语言：{Value(snapshot.Language)}    已等待：{Math.Max(0, (long)(now - snapshot.RequestedAt).TotalMinutes)} 分钟\n用户：[—]｜[—]｜[—]｜[—]\n摘要：";
        const string middle = "\nAI 已回复：";
        var suffix = "（[—]）\n接手对话：" + url + "\n查看完整对话：" + url;
        var budget = 2048 - Encoding.UTF8.GetByteCount(prefix + middle + suffix);
        var summary = Value(snapshot.Summary);
        var reply = Value(snapshot.AiReply);
        if (budget < 2 * Encoding.UTF8.GetByteCount("[—]")) throw new NotificationDeliveryException("MESSAGE_TOO_LONG", false);
        var summaryBudget = Math.Min(Encoding.UTF8.GetByteCount(summary), Math.Max(budget / 2, budget - Encoding.UTF8.GetByteCount(reply)));
        summary = Shorten(summary, summaryBudget);
        return prefix + summary + middle + Shorten(reply, budget - Encoding.UTF8.GetByteCount(summary)) + suffix;
    }

    /// <summary>
    /// 缺失内容使用文档占位符。
    /// </summary>
    private static string Value(string? text) => string.IsNullOrWhiteSpace(text) ? "[—]" : text;

    /// <summary>
    /// 按 Unicode 码点截断，避免破坏中文或 emoji；不截断完整 URL。
    /// </summary>
    private static string Shorten(string value, int budget)
    {
        if (Encoding.UTF8.GetByteCount(value) <= budget) return value;
        var text = new StringBuilder();
        var remaining = budget - Encoding.UTF8.GetByteCount("…");
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Utf8SequenceLength > remaining) break;
            text.Append(rune.ToString());
            remaining -= rune.Utf8SequenceLength;
        }
        return text.Append('…').ToString();
    }
}
