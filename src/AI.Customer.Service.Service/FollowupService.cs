using System.Text.Json;
using AI.Customer.Service.Api.Contracts;
using AI.Customer.Service.Common;
using AI.Customer.Service.Core;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Policies;
using AI.Customer.Service.Domain.Ports;

namespace AI.Customer.Service.Service;

/// <summary>留言提交、原工单认领和最终回复编排，不向未认领客服开放正文或邮箱。</summary>
/// <param name="repository">工单 owner。</param>
/// <param name="reception">原接待周期 owner。</param>
/// <param name="waitingService">等待状态端口。</param>
/// <param name="transactions">共享事务。</param>
/// <param name="access">当前成员准入。</param>
/// <param name="journal">不可变回执和可靠任务。</param>
/// <param name="tasks">投递状态端口。</param>
/// <param name="clock">UTC 时钟。</param>
public sealed class FollowupService(IFollowupRepository repository, IReceptionRepository reception, WaitingService waitingService,
    IUnitOfWork transactions, AccessService access, CommandJournal journal, ITaskRepository tasks, TimeProvider clock)
{
    /// <summary>同键同内容留言重放；保存邮箱、取消在线等待和表单写入必须原子提交。</summary>
    public Task<object> SubmitAsync(Actor actor, Scope scope, FollowupFormCommand command, CancellationToken ct) =>
        transactions.ExecuteAsync(scope.TenantId, scope.TopOrgId, async () =>
        {
            Guard.Text(command.RequestId, "留言请求键");
            Guard.Text(command.Content, "留言正文", 4000);
            Guard.Email(command.Email);
            if (command.ContactName?.Length > 100)
            {
                throw BusinessException.Invalid("称呼超过 100 字符");
            }
            var episode = await reception.LatestAsync(scope, ct) ?? throw BusinessException.Forbidden();
            var fingerprint = Guard.Fingerprint(JsonSerializer.Serialize(command, JsonDefaults.Options));
            var prior = await repository.FindAsync(scope, episode.Id, ct);
            if (prior is not null)
            {
                if (prior.RequestId != command.RequestId || prior.Fingerprint != fingerprint)
                {
                    throw BusinessException.Conflict("该周期已有不同的留言提交");
                }
                return FormView(prior, true);
            }
            var waiting = await waitingService.RefreshInTransactionAsync(episode, actor.TraceId, ct);
            Guard.Version(command.ExpectedVersion, waiting.Version);
            await waitingService.EmailInTransactionAsync(episode, waiting, command.Email, actor.TraceId, ct, createWorkItem: false);
            var form = ConversationRuntime.Bind<Followup>(scope, episode.PrincipalId, Guard.Id("form"));
            form.EpisodeId = episode.Id;
            form.TicketId = waiting.TicketId!;
            form.GroupId = episode.GroupId;
            form.RequestId = command.RequestId;
            form.Fingerprint = fingerprint;
            form.ContactName = command.ContactName;
            form.Content = command.Content;
            form.SubmittedAt = clock.GetUtcNow().UtcDateTime;
            repository.Add(form);
            return FormView(form, true);
        }, ct);

    /// <summary>读取原周期表单，只有原访客或本人认领后才能读取正文。</summary>
    public async Task<object> GetFormAsync(Actor actor, Scope scope, string? episodeId, bool agent, CancellationToken ct)
    {
        var episode = episodeId is null ? await reception.LatestAsync(scope, ct) : await reception.EpisodeAsync(scope, episodeId, ct);
        if (episode is null)
        {
            return new { form = new { } };
        }
        if (agent)
        {
            await access.RequireGroupAsync(actor, episode, ct);
        }
        var form = await repository.FindAsync(scope, episode.Id, ct);
        return form is null ? new { form = new { } } : FormView(form, !agent || form.AssignedTo == actor.AccountId);
    }

    /// <summary>待办列表只显示摘要，不返回留言内容及邮箱。</summary>
    public async Task<object> QueueAsync(Actor actor, string org, string assistant, CancellationToken ct)
    {
        var groups = await access.GroupsAsync(actor, org, ct);
        var values = await repository.QueueAsync(actor.TenantId, org, assistant, groups, ct);
        return new { followups = values.Select(x => new { x.Scope, x.EpisodeId, x.TicketId, x.State, x.Version, x.AssignedTo, x.SubmittedAt }) };
    }

    /// <summary>认领原留言工单，已有人认领时其它人不能读取正文。</summary>
    public Task<object> ClaimAsync(Actor actor, Scope scope, string episodeId, StateCommand command, CancellationToken ct) =>
        transactions.ExecuteAsync(scope.TenantId, scope.TopOrgId, async () =>
        {
            var form = await RequireAsync(actor, scope, episodeId, false, ct);
            var action = "FORM_CLAIM:" + episodeId;
            var prior = await journal.ReplayAsync(scope, actor, action, command.RequestId, command, ct);
            if (prior is not null)
            {
                return prior;
            }
            Guard.Version(command.ExpectedVersion, form.Version);
            if (form.State != "OPEN")
            {
                throw BusinessException.Conflict("留言已被认领");
            }
            form.State = "CLAIMED";
            form.AssignedTo = actor.AccountId;
            form.ClaimedAt = clock.GetUtcNow().UtcDateTime;
            form.Version++;
            return journal.Save(scope, actor, form.PrincipalId, action, command.RequestId, command,
                new { form.TicketId, form.State, form.Version, form.AssignedTo, replayed = false });
        }, ct);

    /// <summary>回复投影中 OPEN 表示尚未提交，查询不创建投递任务。</summary>
    public async Task<object> GetResponseAsync(Actor actor, Scope scope, string episodeId, CancellationToken ct)
    {
        var form = await RequireAsync(actor, scope, episodeId, true, ct);
        var response = await repository.ResponseAsync(scope, episodeId, ct);
        if (response is null)
        {
            return new { form.TicketId, state = "OPEN", version = 1 };
        }
        var task = await tasks.FindAsync(scope.TenantId, scope.TopOrgId, "reply:" + form.TicketId, ct);
        return new { response.TicketId, response.State, response.Version, response.Subject, response.Content, attempts = task?.Attempts ?? 0 };
    }

    /// <summary>最终回复只能首次提交，真实收件地址由等待 owner 解析，正文和任务同事务保存。</summary>
    public Task<object> ReplyAsync(Actor actor, Scope scope, string episodeId, ReplyCommand command, CancellationToken ct) =>
        transactions.ExecuteAsync(scope.TenantId, scope.TopOrgId, async () =>
        {
            var form = await RequireAsync(actor, scope, episodeId, true, ct);
            var action = "REPLY:" + episodeId;
            var prior = await journal.ReplayAsync(scope, actor, action, command.RequestId, command, ct);
            if (prior is not null)
            {
                return prior;
            }
            Guard.Text(command.Subject, "邮件主题", 200);
            Guard.Text(command.Content, "最终回复", 10000);
            if (command.Subject.Contains('\r') || command.Subject.Contains('\n'))
            {
                throw BusinessException.Invalid("邮件主题不能换行");
            }
            if (command.ExpectedVersion != 1 || await repository.ResponseAsync(scope, episodeId, ct) is not null)
            {
                throw BusinessException.Conflict("最终回复已提交或版本无效");
            }
            var waiting = await reception.WaitingAsync(scope, episodeId, ct) ?? throw BusinessException.Conflict("原邮箱工单不存在");
            var response = ConversationRuntime.Bind<FollowupResponse>(scope, form.PrincipalId, Guard.Id("reply"));
            response.EpisodeId = episodeId;
            response.TicketId = form.TicketId;
            response.ActorId = actor.AccountId;
            response.Subject = command.Subject;
            response.Content = command.Content;
            response.CreatedAt = clock.GetUtcNow().UtcDateTime;
            response.Version = 2;
            repository.AddResponse(response);
            await journal.EnqueueAsync(scope, form.PrincipalId, episodeId, "EMAIL_REPLY", "reply:" + form.TicketId,
                new { recipient = waiting.Email, subject = command.Subject, content = command.Content, ticketId = form.TicketId }, actor.TraceId, ct);
            return journal.Save(scope, actor, form.PrincipalId, action, command.RequestId, command,
                new { response.TicketId, response.State, response.Version, replayed = false });
        }, ct);

    /// <summary>仅 DEAD 可人工恢复，仅 SENT 可显式结案，重试沿用原投递键。</summary>
    public Task<object> ChangeResponseAsync(Actor actor, Scope scope, string episodeId, StateCommand command, bool close, CancellationToken ct) =>
        transactions.ExecuteAsync(scope.TenantId, scope.TopOrgId, async () =>
        {
            var form = await RequireAsync(actor, scope, episodeId, true, ct);
            var action = (close ? "CLOSE:" : "RETRY:") + episodeId;
            var prior = await journal.ReplayAsync(scope, actor, action, command.RequestId, command, ct);
            if (prior is not null)
            {
                return prior;
            }
            var response = await repository.ResponseAsync(scope, episodeId, ct) ?? throw BusinessException.Conflict("最终回复尚未提交");
            Guard.Version(command.ExpectedVersion, response.Version);
            if (response.State != (close ? DeliveryState.SENT : DeliveryState.DEAD))
            {
                throw BusinessException.Conflict(close ? "邮件尚未被网关受理，不能结案" : "只有最终失败的回复可以重试");
            }
            var task = await tasks.FindAsync(scope.TenantId, scope.TopOrgId, "reply:" + form.TicketId, ct) ?? throw BusinessException.Conflict("原投递任务不存在");
            if (close)
            {
                task.State = DeliveryState.CLOSED;
                task.Version++;
            }
            else
            {
                DeliveryRules.Retry(task, actor, clock.GetUtcNow().UtcDateTime);
            }
            response.State = task.State;
            response.Version++;
            return journal.Save(scope, actor, form.PrincipalId, action, command.RequestId, command,
                new { response.TicketId, response.State, response.Version, replayed = false });
        }, ct);

    /// <summary>原周期及原组固定，认领要求不得被会话最新周期替换。</summary>
    private async Task<Followup> RequireAsync(Actor actor, Scope scope, string episodeId, bool assigned, CancellationToken ct)
    {
        var episode = await reception.EpisodeAsync(scope, episodeId, ct) ?? throw BusinessException.Forbidden();
        await access.RequireGroupAsync(actor, episode, ct);
        var form = await repository.FindAsync(scope, episodeId, ct) ?? throw BusinessException.Forbidden("留言不存在");
        if (assigned && form.AssignedTo != actor.AccountId)
        {
            throw BusinessException.Forbidden("仅认领者可读取或回复该留言");
        }
        return form;
    }

    /// <summary>对未认领者只返回摘要，正文和称呼保持受控。</summary>
    public static object FormView(Followup form, bool full) => full
        ? new { form.Scope, form.EpisodeId, form.TicketId, form.Origin, form.RequestId, form.ContactName, form.Content, form.State, form.Version, form.AssignedTo, form.SubmittedAt, form.ClaimedAt }
        : new { form.TicketId, form.State, form.Version, form.AssignedTo, form.SubmittedAt };
}
