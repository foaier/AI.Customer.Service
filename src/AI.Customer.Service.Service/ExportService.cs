using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;

namespace AI.Customer.Service.Service;

/// <summary>只读聊天及 AI 评价导出编排，不包含系统提示词、留言邮箱或模型请求载荷。</summary>
/// <param name="conversations">Conversation owner。</param>
/// <param name="surveys">调查 owner。</param>
/// <param name="clock">UTC 时钟。</param>
public sealed class ExportService(IConversationRepository conversations, ISurveyRepository surveys, TimeProvider clock)
{
    /// <summary>按组织、助手和页码导出安全文本，一页不是全部数据。</summary>
    public async Task<object> ExportAsync(Actor actor, string org, string assistant, int page, int size, CancellationToken ct)
    {
        if (page is < 1 or > 100000 || size is < 1 or > 100)
        {
            throw BusinessException.Invalid("导出页码或页大小无效");
        }
        var values = await conversations.PageAsync(actor.TenantId, org, assistant, page, size, ct);
        var output = new List<object>();
        foreach (var value in values)
        {
            var turns = await conversations.TurnsAsync(value.Scope, 10000, ct);
            var human = await conversations.MessagesAsync(value.Scope, 0, 10001, ct);
            var survey = await surveys.FindAsync(value.Scope, ct);
            output.Add(new
            {
                conversationId = value.Id,
                value.BventId,
                aiMessages = turns.Where(x => x.Status == "SUCCEEDED").SelectMany(x => new[]
                {
                    new { id = x.Id + ":user", role = "user", content = x.UserContent },
                    new { id = x.Id + ":assistant", role = "assistant", content = x.AssistantContent! }
                }),
                humanMessages = human.Take(10000).Select(MessagingService.View),
                humanMessagesTruncated = human.Count > 10000,
                surveyId = survey?.Id,
                surveyStatus = survey?.Status,
                handoffChoice = survey?.HandoffChoice,
                respondedAt = survey?.RespondedAt
            });
        }
        return new { exportedAt = clock.GetUtcNow(), topOrgId = org, assistantConfigId = assistant, page, size, hasMore = values.Count == size, conversations = output };
    }

}
