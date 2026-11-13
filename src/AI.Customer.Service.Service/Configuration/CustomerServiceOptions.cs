using AI.Customer.Service.Domain.Models;

namespace AI.Customer.Service.Service.Configuration;

/// <summary>客服部署级规则，不能被访客 HTTP 或模型参数覆盖。</summary>
public sealed class CustomerServiceOptions
{
    /// <summary>同租户组织客服并发接待上限，默认 5 个，合法范围 1 至 100。</summary>
    public int Capacity { get; set; } = 5;

    /// <summary>建议有效期，单位分钟，默认 10，合法范围 1 至 1440。</summary>
    public int ProposalTtlMinutes { get; set; } = 10;

    /// <summary>AI 调查有效期，单位分钟，默认 30，合法范围 1 至 1440。</summary>
    public int SurveyTtlMinutes { get; set; } = 30;

    /// <summary>未保存组织日历时的部署默认，仅影响新等待周期。</summary>
    public CalendarPolicy Calendar { get; set; } = new();
}
