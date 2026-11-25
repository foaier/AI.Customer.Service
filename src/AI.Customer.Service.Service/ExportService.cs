using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;

namespace AI.Customer.Service.Service;

/// <summary>
/// 只读会话记录与 AI 评价导出编排，不包含系统提示词、留言邮箱或模型请求载荷。
/// </summary>
/// <param name="conversations"> Conversation owner </param>
/// <param name="surveys"> 调查 owner </param>
/// <param name="clock"> UTC 时钟 </param>
public sealed class ExportService(IConversationRepository conversations, ISurveyRepository surveys, TimeProvider clock)
{
    private const int MaximumAllPages = 200;
    private const int MaximumPageSize = 100;

    /// <summary>
    /// 按组织、助手和页码导出安全文本，一页不是全部数据。
    /// </summary>
    public async Task<ConversationExportPage> ExportAsync(string assistant, int page, int size, CancellationToken ct)
    {
        if (page is < 1 or > 100000 || size is < 1 or > MaximumPageSize)
        {
            throw BusinessException.Invalid("导出页码或页大小无效");
        }

        var values = await conversations.PageAsync(assistant, page, size, ct);
        var output = new List<ConversationExportItem>();
        foreach (var value in values)
        {
            var turns = await conversations.TurnsAsync(value.Scope, 10000, ct);
            var human = await conversations.MessagesAsync(value.Scope, 0, 10001, ct);
            var survey = await surveys.FindAsync(value.Scope, ct);
            var aiMessages = turns
                .Where(turn => turn.Status == "SUCCEEDED")
                .SelectMany(turn => new[]
                {
                    new ConversationExportMessage(turn.Id + ":user", "user", turn.UserContent, turn.CreatedAt),
                    new ConversationExportMessage(turn.Id + ":assistant", "assistant", turn.AssistantContent!, turn.CompletedAt)
                })
                .ToArray();
            var humanMessages = human
                .Take(10000)
                .Select(message => new ConversationExportHumanMessage(
                    message.Id,
                    message.Seq,
                    message.SenderRole,
                    message.Content,
                    message.CreatedAt))
                .ToArray();

            output.Add(new ConversationExportItem(
                value.Id,
                value.BventId,
                aiMessages,
                humanMessages,
                human.Count > 10000,
                survey?.Id,
                survey?.Status.ToString(),
                survey?.HandoffChoice.ToString(),
                survey?.RespondedAt));
        }

        return new ConversationExportPage(
            clock.GetUtcNow(),
            assistant,
            page,
            size,
            values.Count == size,
            output);
    }

    /// <summary>
    /// 汇总最多两万条会话生成单个工作簿，达到上限时通过 hasMore 明确标记截断。
    /// </summary>
    public async Task<ConversationExportPage> ExportAllAsync(string assistant, CancellationToken ct)
    {
        var output = new List<ConversationExportItem>();
        for (var page = 1; page <= MaximumAllPages; page++)
        {
            var current = await ExportAsync(assistant, page, MaximumPageSize, ct);
            output.AddRange(current.Conversations);
            if (!current.HasMore)
            {
                return new ConversationExportPage(
                    clock.GetUtcNow(), assistant, 1, output.Count, false, output);
            }

            if (page == MaximumAllPages)
            {
                return new ConversationExportPage(
                    clock.GetUtcNow(), assistant, 1, output.Count, true, output);
            }
        }

        throw new InvalidOperationException("无法完成会话记录导出");
    }
}

/// <summary>
/// 会话记录分页文件，AI 和人工消息均只包含可展示纯文本。
/// </summary>
public sealed record ConversationExportPage(
    DateTimeOffset ExportedAt,
    string AssistantConfigId,
    int Page,
    int Size,
    bool HasMore,
    IReadOnlyList<ConversationExportItem> Conversations);

/// <summary>
/// 单个会话记录，不包含访客账号、联系邮箱或模型内部消息。
/// </summary>
public sealed record ConversationExportItem(
    string ConversationId,
    string? BventId,
    IReadOnlyList<ConversationExportMessage> AiMessages,
    IReadOnlyList<ConversationExportHumanMessage> HumanMessages,
    bool HumanMessagesTruncated,
    string? SurveyId,
    string? SurveyStatus,
    string? HandoffChoice,
    DateTime? RespondedAt);

/// <summary>
/// 一条经过系统提示词和请求载荷过滤的 AI 展示消息。
/// </summary>
public sealed record ConversationExportMessage(string Id, string Role, string Content, DateTime? CreatedAt);

/// <summary>
/// 一条按会话序号排列的人工客服时间线消息。
/// </summary>
public sealed record ConversationExportHumanMessage(
    string MessageId,
    long Seq,
    string SenderRole,
    string Content,
    DateTime CreatedAt);
