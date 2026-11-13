namespace AI.Customer.Service.Core.Models;

/// <summary>Microsoft Agent Framework 的 OpenAI 兼容 provider 配置，框架类型不越过模型领域端口。</summary>
public sealed class ModelOptions
{
    /// <summary>兼容 API 根地址，默认空，通常以 /v1/ 结尾。</summary>
    public string BaseUrl { get; set; } = "";
    /// <summary>服务端模型密钥，默认空，只通过环境变量或 Secret 注入。</summary>
    public string ApiKey { get; set; } = "";
    /// <summary>文本生成及语义分类模型名，默认空；未配置不能伪装 AI 成功。</summary>
    public string Model { get; set; } = "";
    /// <summary>生成超时，单位秒，默认 120，合法范围 5 至 240。</summary>
    public int TimeoutSeconds { get; set; } = 120;
    /// <summary>受控客服系统提示词，不使用访客可覆盖的指令字段。</summary>
    public string SystemPrompt { get; set; } = "你是展会智能客服。只回答有依据的内容，知识不足应明确说明。需要人工协助时可建议但不能自行转人工。使用中文纯文本回答。";
}
