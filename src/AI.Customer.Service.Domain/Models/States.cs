namespace AI.Customer.Service.Domain.Models;

/// <summary>一次人工接待的生命周期，人工首条回复才进入 ACTIVE。</summary>
public enum EpisodeStatus
{
    /// <summary>申请已记录，尚无客服认领。</summary>
    REQUESTED,
    /// <summary>客服原子认领成功，尚未首响。</summary>
    CLAIMED,
    /// <summary>客服已发送首条有效消息。</summary>
    ACTIVE,
    /// <summary>认领客服已明确结束接待。</summary>
    RESOLVED,
    /// <summary>未接单申请已取消，回复权恢复 AI。</summary>
    CANCELLED
}

/// <summary>人工申请的确定性来源，与业务原因分别保存。</summary>
public enum TriggerSource
{
    /// <summary>访客主动按钮或明确原话。</summary>
    VISITOR_DIRECT_REQUEST,
    /// <summary>访客确认模型建议卡。</summary>
    AI_RECOMMENDATION_CONFIRMED,
    /// <summary>不满意评价后明确选择人工。</summary>
    SURVEY_DISSATISFIED_OPT_IN,
    /// <summary>可信系统故障任务。</summary>
    SYSTEM_POLICY
}

/// <summary>客服业务原因白名单，模型不能创建新原因或替换系统事实。</summary>
public enum ReasonCode
{
    /// <summary>知识缺失。</summary>
    KNOWLEDGE_GAP,
    /// <summary>回答置信不足。</summary>
    LOW_CONFIDENCE,
    /// <summary>知识冲突或过期。</summary>
    CONTRADICTORY_OR_STALE_KNOWLEDGE,
    /// <summary>用户纠正内容。</summary>
    USER_CORRECTION,
    /// <summary>投诉或不满意。</summary>
    COMPLAINT_OR_DISSATISFACTION,
    /// <summary>多轮仍未解决。</summary>
    REPEATED_UNRESOLVED,
    /// <summary>复杂人工处理事项。</summary>
    COMPLEX_CASE,
    /// <summary>展位报价或合同事项。</summary>
    EXHIBIT_PRICE_OR_CONTRACT,
    /// <summary>媒体或法律事项。</summary>
    MEDIA_OR_LEGAL,
    /// <summary>用户主动申请。</summary>
    USER_REQUEST,
    /// <summary>可信系统故障。</summary>
    SYSTEM_FAILURE,
    /// <summary>安全或紧急事项。</summary>
    SAFETY_OR_URGENT,
    /// <summary>调查不满意并明确选择人工。</summary>
    SURVEY_DISSATISFIED
}

/// <summary>主题分类结果，不代表访客身份或客服权限。</summary>
public enum Category
{
    /// <summary>参展及展商咨询。</summary>
    EXHIBITOR,
    /// <summary>观众及一般参观咨询。</summary>
    VISITOR,
    /// <summary>媒体咨询。</summary>
    MEDIA,
    /// <summary>未知主题或模型降级。</summary>
    OTHER
}

/// <summary>等待流程独立于人工接待状态，邮箱只在明确提交时保存。</summary>
public enum WaitingState
{
    /// <summary>在线等待。</summary>
    WAITING,
    /// <summary>已达到十分钟繁忙窗口。</summary>
    BUSY,
    /// <summary>访客选择继续等待。</summary>
    CONTINUING,
    /// <summary>已闭班，等待邮箱留言。</summary>
    NEED_EMAIL,
    /// <summary>已创建跟进工单，尚不表示通知受理。</summary>
    FOLLOW_UP,
    /// <summary>客服已接单。</summary>
    ACCEPTED,
    /// <summary>在线申请已取消。</summary>
    CANCELLED
}

/// <summary>在线租约仅控制展示及改派，不限制手动接单。</summary>
public enum AvailabilityStatus
{
    /// <summary>主动在线且心跳有效时可接改派。</summary>
    ONLINE,
    /// <summary>暂离，保留当前接待。</summary>
    AWAY,
    /// <summary>离线或心跳失效。</summary>
    OFFLINE
}

/// <summary>建议卡的单次访客决策。</summary>
public enum ProposalStatus
{
    /// <summary>有效期内等待访客选择。</summary>
    PENDING,
    /// <summary>已确认并创建人工周期。</summary>
    CONFIRMED,
    /// <summary>访客明确拒绝。</summary>
    DISMISSED,
    /// <summary>有效期已结束。</summary>
    EXPIRED
}

/// <summary>AI 调查的单次回答状态。</summary>
public enum SurveyStatus
{
    /// <summary>尚未评价。</summary>
    OPEN,
    /// <summary>访客明确满意。</summary>
    SATISFIED,
    /// <summary>访客明确不满意，尚需单独选择人工。</summary>
    DISSATISFIED,
    /// <summary>超时且未回答，不计为满意。</summary>
    EXPIRED
}

/// <summary>不满意后独立的人工选择，不可由模型代选。</summary>
public enum HandoffChoice
{
    /// <summary>满意或尚未评价时不适用。</summary>
    NOT_APPLICABLE,
    /// <summary>不满意后等待选择。</summary>
    PENDING,
    /// <summary>访客明确同意人工。</summary>
    OPTED_IN,
    /// <summary>访客明确不需要人工。</summary>
    DECLINED
}

/// <summary>可靠任务的租约和有限重试状态。</summary>
public enum DeliveryState
{
    /// <summary>等待首次或下一次投递。</summary>
    PENDING,
    /// <summary>Worker 已持有有期限的租约。</summary>
    CLAIMED,
    /// <summary>网关明确受理，不表示收件人已读。</summary>
    SENT,
    /// <summary>自动重试耗尽，等待人工恢复。</summary>
    DEAD,
    /// <summary>客服对已受理的最终回复显式结案。</summary>
    CLOSED
}
