using System.Text.RegularExpressions;

namespace AI.Customer.Service.Domain.Policies;

/// <summary>根据本轮原文识别明确人工授权，引用、否定、假设和能力询问不能由模型升级为同意。</summary>
public static class ExplicitHandoffIntent
{
    /// <summary>保守匹配完整请求；复杂上下文应展示建议卡供用户确认。</summary>
    private static readonly Regex Request = new(
        @"^(?:(?:请|麻烦|帮我|给我|我要|我想|我需要|需要|想要|现在|立即|直接|可以|能否|能|我想要)\s*)*(?:转人工(?:客服)?|转接人工(?:客服)?|找人工(?:客服)?|找真人(?:客服)?|人工客服|人工服务|联系人工(?:客服)?|接通人工(?:客服)?)(?:一下|吧|吗|呢|谢谢|服务|人员|好吗|可以吗|[!！?？。,.，\s])*$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>只读取服务端收到的用户原文，支持先说明问题再明确发令。</summary>
    public static bool Matches(string? input)
    {
        if (input is null || input.Length > 500)
        {
            return false;
        }
        var text = input.Trim();
        if (Request.IsMatch(text))
        {
            return true;
        }
        if (Regex.IsMatch(text, "[\"“”‘’「」『』]|如果|假如|假设|翻译|举例|他说|她说|写着|显示|功能|按钮|提示词"))
        {
            return false;
        }
        var clauses = Regex.Split(text, "[，,。！!；;\\n]");
        return clauses.Length > 1 && Request.IsMatch(clauses[^1].Trim());
    }
}
