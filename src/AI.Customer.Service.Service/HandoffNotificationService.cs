using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;

namespace AI.Customer.Service.Service;

/// <summary>
/// 新 REQUESTED 事务内登记即时通知；重复命令不新增任务，失败不切换供应商。
/// </summary>
public sealed class HandoffNotificationService(IConfigurationRepository configuration, IConversationRepository conversations,
    IAssistantRegistry assistants, CommandJournal journal)
{
    /// <summary>
    /// 冻结文本、通道、雅式群及站点；企微 Hook ID 留在受保护配置中，投递时读取。
    /// </summary>
    public async Task EnqueueAsync(Episode episode, CancellationToken ct)
    {
        if (episode.Status != EpisodeStatus.REQUESTED) return;
        var settings = await configuration.ChatNotificationAsync(ct);
        if (settings?.Enabled != true) return;
        var history = await conversations.TurnsAsync(episode.Scope, 12, ct);
        var category = ChatNotificationConfiguration.GroupKey(episode.Category);
        var snapshot = new HandoffNotificationSnapshot(settings.ActiveProvider, category,
            settings.ActiveProvider == "ADSALE" ? settings.ChatGroup.GetValueOrDefault(category) : null,
            settings.BaseUrl, settings.PublicBaseUrl, episode.RequestedAt,
            assistants.Require(episode.AssistantConfigId).Language,
            history.LastOrDefault()?.UserContent, history.LastOrDefault(x => x.Status == "SUCCEEDED")?.AssistantContent);
        await journal.EnqueueAsync(episode.Scope, episode.PrincipalId, episode.Id, "HANDOFF_NOTIFY", "handoff-requested:" + episode.Id,
            snapshot, episode.TraceId, ct);
    }
}
