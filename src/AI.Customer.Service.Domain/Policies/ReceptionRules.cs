using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Models;

namespace AI.Customer.Service.Domain.Policies;

/// <summary>集中声明接待状态转换及重复事件行为，应用服务不能任意赋值推进。</summary>
public static class ReceptionRules
{
    /// <summary>判断是否仍占有人工作答权。</summary>
    public static bool IsOpen(this EpisodeStatus status) =>
        status is EpisodeStatus.REQUESTED or EpisodeStatus.CLAIMED or EpisodeStatus.ACTIVE;

    /// <summary>按事件校验前置状态并取得唯一合法目标；重复命令由持久回执处理。</summary>
    public static EpisodeStatus Transition(EpisodeStatus status, string action) => (status, action) switch
    {
        (EpisodeStatus.REQUESTED, "CLAIM") => EpisodeStatus.CLAIMED,
        (EpisodeStatus.CLAIMED, "FIRST_MESSAGE") => EpisodeStatus.ACTIVE,
        (EpisodeStatus.ACTIVE, "FIRST_MESSAGE") => EpisodeStatus.ACTIVE,
        (EpisodeStatus.REQUESTED, "CANCEL") => EpisodeStatus.CANCELLED,
        (EpisodeStatus.CLAIMED or EpisodeStatus.ACTIVE, "RESOLVE") => EpisodeStatus.RESOLVED,
        (EpisodeStatus.CLAIMED or EpisodeStatus.ACTIVE, "TRANSFER") => EpisodeStatus.CLAIMED,
        _ => throw BusinessException.Conflict($"当前接待状态 {status} 不允许 {action}")
    };

    /// <summary>按冻结组及当前认领者检查敏感正文权限。</summary>
    public static void RequireAssignee(Episode episode, string actor)
    {
        if (episode.AgentId != actor)
        {
            throw BusinessException.Forbidden("当前客服未认领该接待");
        }
    }

    /// <summary>检查等待状态合法转换，终态不能恢复在线等待。</summary>
    public static void ChangeWaiting(Waiting waiting, WaitingState next)
    {
        var allowed = waiting.State switch
        {
            WaitingState.WAITING => next is WaitingState.BUSY or WaitingState.NEED_EMAIL or WaitingState.FOLLOW_UP or WaitingState.ACCEPTED or WaitingState.CANCELLED,
            WaitingState.BUSY => next is WaitingState.CONTINUING or WaitingState.NEED_EMAIL or WaitingState.FOLLOW_UP or WaitingState.ACCEPTED or WaitingState.CANCELLED,
            WaitingState.CONTINUING => next is WaitingState.NEED_EMAIL or WaitingState.FOLLOW_UP or WaitingState.ACCEPTED or WaitingState.CANCELLED,
            WaitingState.NEED_EMAIL => next == WaitingState.FOLLOW_UP,
            _ => false
        };
        if (!allowed)
        {
            throw BusinessException.Conflict($"等待状态 {waiting.State} 不允许进入 {next}");
        }
        waiting.State = next;
        waiting.Version++;
    }
}
