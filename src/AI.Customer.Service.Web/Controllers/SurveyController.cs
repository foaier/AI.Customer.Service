using AI.Customer.Service.Api.Contracts;
using AI.Customer.Service.Service;
using AI.Customer.Service.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace AI.Customer.Service.Web.Controllers;

/// <summary>AI 单次满意度及不满意人工选择 HTTP Adapter。</summary>
/// <param name="request">原访客准入。</param>
/// <param name="surveys">调查应用端口。</param>
public sealed class SurveyController(RequestAccess request, SurveyService surveys) : CustomerServiceControllerBase(request)
{
    /// <summary>恢复已有调查，不通过查询生成问卷。</summary>
    [HttpGet("conversations/{id}/survey")]
    public async Task<object> Get(string id, CancellationToken ct) => Success(new { survey = await surveys.GetAsync(await RequestAccess.VisitorAsync(id, ct), ct) });

    /// <summary>只记录明确满意或不满意。</summary>
    [HttpPost("conversations/{id}/survey/{survey}/answer")]
    public async Task<object> Answer(string id, string survey, SurveyAnswer command, CancellationToken ct) =>
        Success(await surveys.AnswerAsync(Actor, await RequestAccess.VisitorAsync(id, ct), survey, command, ct));

    /// <summary>不满意之后再单独选择人工。</summary>
    [HttpPost("conversations/{id}/survey/{survey}/handoff-choice")]
    public async Task<object> Choice(string id, string survey, SurveyChoice command, CancellationToken ct) =>
        Success(await surveys.ChooseAsync(Actor, await RequestAccess.VisitorAsync(id, ct), survey, command, ct));

    /// <summary>原访客实际展示卡片后提交回执。</summary>
    [HttpPost("conversations/{id}/survey/{survey}/receipt")]
    public async Task<object> Receipt(string id, string survey, CancellationToken ct) =>
        Success(await surveys.ReceiptAsync(await RequestAccess.VisitorAsync(id, ct), survey, ct));
}
