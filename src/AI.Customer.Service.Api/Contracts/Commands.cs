using System.Text.Json.Serialization;
using AI.Customer.Service.Domain.Models;

namespace AI.Customer.Service.Api.Contracts;

/// <summary>客户端稳定命令键，不接受自报身份及组织。</summary>
/// <param name="RequestId">首次提交键，网络重试原样复用。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandoffCommand(string RequestId);

/// <summary>带聚合版本的状态命令。</summary>
/// <param name="RequestId">稳定首次请求键。</param>
/// <param name="ExpectedVersion">首次请求依据的版本。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StateCommand(string RequestId, long ExpectedVersion);

/// <summary>人工消息请求，仅允许消息键和纯文本。</summary>
/// <param name="ClientMsgId">发送主体下稳定的消息键。</param>
/// <param name="Content">1 至 4000 字符。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MessageCommand(string ClientMsgId, string Content);

/// <summary>当前主体的明确接收或已读回执。</summary>
/// <param name="Kind">DELIVERED 或 READ。</param>
/// <param name="Seq">不能超过已存在最大序列。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReceiptCommand(string Kind, long Seq);

/// <summary>建议卡单次决策。</summary>
/// <param name="RequestId">稳定命令键。</param>
/// <param name="ExpectedProposalVersion">首次建议版本。</param>
/// <param name="Decision">CONFIRM 或 DISMISS。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposalDecision(string RequestId, long ExpectedProposalVersion, string Decision);

/// <summary>等待选择，不能覆盖归属、路由或容量。</summary>
/// <param name="ExpectedVersion">等待聚合版本。</param>
/// <param name="Action">CONTINUE、CONTACT 或 EMAIL。</param>
/// <param name="Email">CONTACT/EMAIL 必填。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WaitingChoice(long ExpectedVersion, string Action, string? Email);

/// <summary>明确留言表单，邮箱只写等待 owner。</summary>
/// <param name="RequestId">稳定提交键。</param>
/// <param name="ExpectedVersion">原等待版本。</param>
/// <param name="Email">实际回复邮箱。</param>
/// <param name="ContactName">可空称呼，最多 100 字符。</param>
/// <param name="Content">1 至 4000 字符留言。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FollowupFormCommand(string RequestId, long ExpectedVersion, string Email, string? ContactName, string Content);

/// <summary>客服最终回复，不接受客户端指定收件地址。</summary>
/// <param name="RequestId">稳定首次提交键。</param>
/// <param name="ExpectedVersion">回复版本，尚未提交时为 1。</param>
/// <param name="Subject">1 至 200 字符且禁止换行。</param>
/// <param name="Content">1 至 10000 字符纯文本。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReplyCommand(string RequestId, long ExpectedVersion, string Subject, string Content);

/// <summary>同组改派请求，目标资格由服务器实时核验。</summary>
/// <param name="RequestId">稳定首次改派键。</param>
/// <param name="ExpectedVersion">接待版本。</param>
/// <param name="TargetAccountId">同组其它账号引用。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransferCommand(string RequestId, long ExpectedVersion, string TargetAccountId);

/// <summary>明确在线状态选择，每次选择推进代次。</summary>
/// <param name="Status">ONLINE、AWAY 或 OFFLINE。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AvailabilityCommand(AvailabilityStatus Status);

/// <summary>在线心跳只能续有效同代次 ONLINE。</summary>
/// <param name="Version">最近一次明确上线返回的版本。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HeartbeatCommand(long Version);

/// <summary>单次 AI 满意度回答。</summary>
/// <param name="ExpectedVersion">调查版本。</param>
/// <param name="Answer">SATISFIED 或 DISSATISFIED。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SurveyAnswer(long ExpectedVersion, SurveyStatus Answer);

/// <summary>不满意后单独询问的人工选择。</summary>
/// <param name="ExpectedVersion">调查版本。</param>
/// <param name="Choice">OPTED_IN 或 DECLINED。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SurveyChoice(long ExpectedVersion, HandoffChoice Choice);

/// <summary>本地业务路由整体 CAS，不接受客户端身份字段。</summary>
/// <param name="Version">未保存为 0。</param>
/// <param name="Groups">四类完整本地组。</param>
/// <param name="Routes">助手主题映射。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RoutingCommand(long Version, List<ReceptionGroup> Groups, List<AssistantRoute> Routes);

/// <summary>正式组织日历完整 CAS 保存。</summary>
/// <param name="Version">未保存为 0。</param>
/// <param name="CalendarVersion">业务日历版本。</param>
/// <param name="Zone">合法时区。</param>
/// <param name="Opens">同日开门时间。</param>
/// <param name="Closes">同日闭门时间。</param>
/// <param name="Overrides">ISO 日期覆盖。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CalendarCommand(long Version, string CalendarVersion, string Zone, string Opens, string Closes, Dictionary<string, bool> Overrides);

/// <summary>日历草稿预览，不保存且不修改服务器时间。</summary>
/// <param name="Calendar">完整草稿。</param>
/// <param name="At">带时区的指定时刻。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CalendarPreview(CalendarCommand Calendar, DateTimeOffset At);

/// <summary>语义路由预览，不创建人工申请。</summary>
/// <param name="AssistantConfigId">可信助手引用。</param>
/// <param name="Context">1 至 16000 字符上下文。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RoutingPreview(string AssistantConfigId, string Context);

/// <summary>与 Java 页面一致的 AI 聊天请求，归属只读服务端助手注册表。</summary>
/// <param name="SessionId">已有共享会话。</param>
/// <param name="UserInput">当前原始输入。</param>
/// <param name="BusinessType">页面兼容业务类型，不授予权限。</param>
/// <param name="Extensions">只读取助手引用，其余归属字段重新核验。</param>
/// <param name="IsReconnect">兼容原页面，生成请求必须为 false。</param>
/// <param name="SpeechEnabled">本期只支持纯文本，true 拒绝。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChatCommand(string SessionId, string UserInput, string? BusinessType, Dictionary<string, ChatExtension>? Extensions, bool IsReconnect = false, bool SpeechEnabled = false);

/// <summary>页面可选助手引用，来源组织和大会不能覆盖服务端事实。</summary>
/// <param name="AssistantConfigId">配置引用。</param>
/// <param name="BventId">仅用于一致性核验。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChatExtension(string AssistantConfigId, string? BventId);
