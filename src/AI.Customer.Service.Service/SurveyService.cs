using System.Globalization;
using AI.Customer.Service.Api.Contracts;
using AI.Customer.Service.Common;
using AI.Customer.Service.Core;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Policies;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Service.Configuration;
using Microsoft.Extensions.Options;

namespace AI.Customer.Service.Service;

/// <summary>AI 成功结束资格、单次评价和不满意后明确 opt-in 的应用编排。</summary>
/// <param name="repository">调查 owner。</param>
/// <param name="conversations">真实成功历史。</param>
/// <param name="runtime">回复权及会话范围。</param>
/// <param name="handoff">客服申请应用端口。</param>
/// <param name="transactions">共享事务。</param>
/// <param name="journal">稳定回执。</param>
/// <param name="clock">UTC 时钟。</param>
/// <param name="options">调查有效期。</param>
/// <param name="reception">客服历史覆盖核验，不以当前 AI owner 代替从未转人工的事实。</param>
public sealed class SurveyService(ISurveyRepository repository, IConversationRepository conversations, ConversationRuntime runtime,
    HandoffService handoff, IUnitOfWork transactions, CommandJournal journal, TimeProvider clock, IOptions<CustomerServiceOptions> options,
    IReceptionRepository reception)
{
    /// <summary>在成功轮次后按真实历史创建一次性调查，不根据沉默或人工结束创建。</summary>
    public Task<Survey?> EvaluateAsync(Scope scope, CancellationToken ct) =>
        transactions.ExecuteAsync<Survey?>(scope.TenantId, scope.TopOrgId, async () =>
        {
            var current = await repository.FindAsync(scope, ct);
            var conversation = await runtime.RequireAsync(scope, ct);
            conversation.SurveyCheckedTurnId = conversation.LastCompletedTurnId;
            conversation.Version++;
            if (current is not null || conversation.HumanEpisodeId is not null)
            {
                return current;
            }
            var history = await conversations.TurnsAsync(scope, 101, ct);
            var evidence = SurveyContentPolicy.Assess(history);
            if (evidence is null || await reception.LatestAsync(scope, ct) is not null)
            {
                return null;
            }
            var survey = ConversationRuntime.Bind<Survey>(scope, conversation.PrincipalId, Guard.Id("survey"));
            survey.SourceTurnId = history[^1].Id;
            survey.Evidence = evidence;
            survey.Epoch = conversation.Epoch;
            survey.OpenedAt = clock.GetUtcNow().UtcDateTime;
            survey.ExpiresAt = clock.GetUtcNow().UtcDateTime.AddMinutes(options.Value.SurveyTtlMinutes);
            await RecordEvidenceInTransactionAsync(scope, history[^1], evidence.Split(':')[0], null, "explicit-resolution-evidence-v4", ct);
            repository.Add(survey);
            return survey;
        }, ct);

    /// <summary>只恢复及过期已有调查，读取不创建或代填展示回执。</summary>
    public Task<object?> GetAsync(Scope scope, CancellationToken ct) =>
        transactions.ExecuteAsync<object?>(scope.TenantId, scope.TopOrgId, async () =>
        {
            var value = await repository.FindAsync(scope, ct);
            var conversation = await runtime.RequireAsync(scope, ct);
            if (value is not null && (value.Status == SurveyStatus.OPEN || value.HandoffChoice == HandoffChoice.PENDING) &&
                (value.Epoch != conversation.Epoch || conversation.HumanEpisodeId is not null || await reception.LatestAsync(scope, ct) is not null))
            {
                return null;
            }
            if (value is not null && value.Status == SurveyStatus.OPEN && value.ExpiresAt <= clock.GetUtcNow().UtcDateTime)
            {
                value.Status = SurveyStatus.EXPIRED;
                value.Version++;
            }
            return value is null ? null : View(value);
        }, ct);

    /// <summary>评价同参数重放安全，反向修改已提交答案冲突；不满意只打开选择，不直接转人工。</summary>
    public Task<object> AnswerAsync(Actor actor, Scope scope, string id, SurveyAnswer command, CancellationToken ct) =>
        transactions.ExecuteAsync(scope.TenantId, scope.TopOrgId, async () =>
        {
            var survey = await RequireAsync(scope, id, ct);
            var request = id + ":answer";
            var prior = await journal.ReplayAsync(scope, actor, "SURVEY_ANSWER", request, command, ct);
            if (prior is not null)
            {
                return prior;
            }
            if (command.Answer is not SurveyStatus.SATISFIED and not SurveyStatus.DISSATISFIED)
            {
                throw BusinessException.Invalid("调查回答只能为 SATISFIED 或 DISSATISFIED");
            }
            Guard.Version(command.ExpectedVersion, survey.Version);
            await RequireCurrentAsync(scope, survey, ct);
            if (survey.Status != SurveyStatus.OPEN || survey.ExpiresAt <= clock.GetUtcNow().UtcDateTime)
            {
                throw BusinessException.Conflict("调查已回答或过期");
            }
            survey.Status = command.Answer;
            survey.HandoffChoice = command.Answer == SurveyStatus.DISSATISFIED ? HandoffChoice.PENDING : HandoffChoice.NOT_APPLICABLE;
            survey.RespondedAt = clock.GetUtcNow().UtcDateTime;
            survey.Version++;
            return journal.Save(scope, actor, survey.PrincipalId, "SURVEY_ANSWER", request, command, View(survey));
        }, ct);

    /// <summary>只有评价不满意并明确 OPTED_IN 才创建同会话人工周期，选择与申请同事务。</summary>
    public async Task<object> ChooseAsync(Actor actor, Scope scope, string id, SurveyChoice command, CancellationToken ct)
    {
        var classification = await handoff.PrepareAsync(scope, ct);
        return await transactions.ExecuteAsync(scope.TenantId, scope.TopOrgId, async () =>
        {
            var survey = await RequireAsync(scope, id, ct);
            var request = id + ":choice";
            var prior = await journal.ReplayAsync(scope, actor, "SURVEY_CHOICE", request, command, ct);
            if (prior is not null)
            {
                return prior;
            }
            Guard.Version(command.ExpectedVersion, survey.Version);
            await RequireCurrentAsync(scope, survey, ct);
            if (survey.Status != SurveyStatus.DISSATISFIED || survey.HandoffChoice != HandoffChoice.PENDING ||
                command.Choice is not HandoffChoice.OPTED_IN and not HandoffChoice.DECLINED)
            {
                throw BusinessException.Conflict("当前调查不允许此人工选择");
            }
            if (command.Choice == HandoffChoice.OPTED_IN)
            {
                await handoff.CreateInTransactionAsync(scope, survey.PrincipalId, TriggerSource.SURVEY_DISSATISFIED_OPT_IN,
                    ReasonCode.SURVEY_DISSATISFIED, classification, actor.TraceId, ct);
            }
            survey.HandoffChoice = command.Choice;
            survey.Version++;
            return journal.Save(scope, actor, survey.PrincipalId, "SURVEY_CHOICE", request, command, View(survey));
        }, ct);
    }

    /// <summary>页面实际显示后显式登记，重复回执安全且不推进评价版本。</summary>
    public Task<object> ReceiptAsync(Scope scope, string id, CancellationToken ct) =>
        transactions.ExecuteAsync(scope.TenantId, scope.TopOrgId, async () =>
        {
            var value = await RequireAsync(scope, id, ct);
            value.DisplayedAt ??= clock.GetUtcNow().UtcDateTime;
            return View(value);
        }, ct);

    /// <summary>固定调查与完整会话范围，拒绝跨会话引用。</summary>
    private async Task<Survey> RequireAsync(Scope scope, string id, CancellationToken ct)
    {
        var value = await repository.FindAsync(scope, ct);
        return value?.Id == id ? value : throw BusinessException.Forbidden("调查不存在或不可访问");
    }

    /// <summary>模型候选只有置信度达到 0.85 才保存，候选本身不能产生问卷。</summary>
    public Task<ResolutionEvidence?> RecordCandidateAsync(Scope scope, AiTurn turn, double confidence, CancellationToken ct)
    {
        if (!double.IsFinite(confidence) || confidence is < .85 or > 1)
        {
            return Task.FromResult<ResolutionEvidence?>(null);
        }
        return transactions.ExecuteAsync<ResolutionEvidence?>(scope.TenantId, scope.TopOrgId, async () =>
        {
            var conversation = await runtime.RequireAsync(scope, ct);
            if (conversation.HumanEpisodeId is not null || conversation.Epoch != turn.Epoch)
            {
                return null;
            }
            var source = await conversations.TurnAsync(scope, turn.Id, ct);
            if (source?.Status != "SUCCEEDED" || source.Epoch != turn.Epoch)
            {
                return null;
            }
            return await RecordEvidenceInTransactionAsync(scope, source, "MODEL_POSSIBLE_RESOLUTION", confidence, "ai-resolution-candidate-v1", ct);
        }, ct);
    }

    /// <summary>同轮同参数恢复原证据，异参数冲突且不覆盖首次审计。</summary>
    private async Task<ResolutionEvidence> RecordEvidenceInTransactionAsync(Scope scope, AiTurn turn, string kind, double? confidence, string policy, CancellationToken ct)
    {
        var fingerprint = Guard.Fingerprint($"{turn.Id}|{turn.Epoch}|{kind}|{confidence?.ToString(CultureInfo.InvariantCulture)}|{policy}");
        var existing = await repository.EvidenceAsync(scope, turn.Id, kind, ct);
        if (existing is not null)
        {
            return existing.Fingerprint == fingerprint ? existing : throw BusinessException.Conflict("同轮结束候选参数不一致");
        }
        var evidence = ConversationRuntime.Bind<ResolutionEvidence>(scope, turn.PrincipalId, Guard.Id("resolution"));
        evidence.Kind = kind;
        evidence.TurnId = turn.Id;
        evidence.Epoch = turn.Epoch;
        evidence.Confidence = confidence;
        evidence.Policy = policy;
        evidence.Fingerprint = fingerprint;
        evidence.CreatedAt = clock.GetUtcNow().UtcDateTime;
        evidence.TraceId = turn.TraceId;
        repository.AddEvidence(evidence);
        return evidence;
    }

    /// <summary>已提交回执先重放；首次评价或选择必须仍处于原 AI 代次且没有人工历史。</summary>
    private async Task RequireCurrentAsync(Scope scope, Survey survey, CancellationToken ct)
    {
        var conversation = await runtime.RequireAsync(scope, ct);
        if (conversation.Epoch != survey.Epoch || conversation.HumanEpisodeId is not null || await reception.LatestAsync(scope, ct) is not null)
        {
            throw BusinessException.Conflict("调查来源轮次已失效");
        }
    }

    /// <summary>安全调查投影不返回内部证据和原始模型结果。</summary>
    public static object View(Survey survey) =>
        new { surveyId = survey.Id, survey.Status, survey.HandoffChoice, survey.Version, survey.ExpiresAt, survey.RespondedAt, replayed = false };
}
