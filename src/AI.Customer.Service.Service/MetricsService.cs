using System.Globalization;
using System.Text;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;

namespace AI.Customer.Service.Service;

/// <summary>跨 owner 只读指标编排，固定最近 24 小时窗口且不输出高基数身份标签。</summary>
/// <param name="reception">客服只读聚合端口。</param>
/// <param name="surveys">Conversation 调查只读聚合端口。</param>
/// <param name="transactions">组织一致性快照事务。</param>
/// <param name="clock">UTC 时钟。</param>
public sealed class MetricsService(IReceptionMetricsQuery reception, ISurveyMetricsQuery surveys, IUnitOfWork transactions, TimeProvider clock)
{
    /// <summary>调用者先核验正式组织及助手权限，时间范围不接受外部输入。</summary>
    public Task<MetricsSnapshot> ReadAsync(Actor actor, string org, string assistant, CancellationToken ct) =>
        transactions.ExecuteAsync(actor.TenantId, org, async () =>
        {
            var through = clock.GetUtcNow().UtcDateTime;
            var from = through.AddHours(-24);
            return new MetricsSnapshot(from, through, await reception.ReadAsync(actor.TenantId, org, assistant, from, through, ct),
                await surveys.CountAsync(actor.TenantId, org, assistant, from, through, ct));
        }, ct);

    /// <summary>固定 metric 名称和低基数枚举标签，窗口计数统一声明 gauge。</summary>
    public static string Prometheus(MetricsSnapshot value)
    {
        var output = new StringBuilder("# 客服历史为最近24小时窗口，积压为当前存量；值为gauge。\n");
        foreach (var name in new[] { "cs_handoff_requested", "cs_handoff_state", "cs_handoff_duration_samples", "cs_handoff_duration_seconds_sum", "cs_handoff_backlog", "cs_survey_state" })
        {
            output.AppendLine("# TYPE " + name + " gauge");
        }
        foreach (var pair in value.Reception.Requested)
        {
            var tags = pair.Key.Split(':', 2);
            Line(output, "cs_handoff_requested", $"source=\"{Escape(tags[0])}\",reason=\"{Escape(tags[1])}\"", pair.Value);
        }
        foreach (var pair in value.Reception.Statuses)
        {
            Line(output, "cs_handoff_state", $"state=\"{Escape(pair.Key)}\"", pair.Value);
        }
        foreach (var pair in value.Reception.Timings)
        {
            Line(output, "cs_handoff_duration_samples", $"stage=\"{Escape(pair.Key)}\"", pair.Value.Count);
            Line(output, "cs_handoff_duration_seconds_sum", $"stage=\"{Escape(pair.Key)}\"", pair.Value.SecondsSum);
        }
        foreach (var pair in value.Reception.Backlog)
        {
            Line(output, "cs_handoff_backlog", $"kind=\"{Escape(pair.Key)}\"", pair.Value);
        }
        foreach (var pair in value.Surveys)
        {
            Line(output, "cs_survey_state", $"state=\"{Escape(pair.Key)}\"", pair.Value);
        }
        return output.ToString();
    }

    /// <summary>固定格式输出 invariant 数值，避免操作系统小数点区域设置破坏协议。</summary>
    private static void Line(StringBuilder output, string metric, string labels, double value) =>
        output.Append(metric).Append('{').Append(labels).Append("} ").Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');

    /// <summary>即使枚举来自数据库也规范转义，禁止换行或引号注入标签。</summary>
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}
