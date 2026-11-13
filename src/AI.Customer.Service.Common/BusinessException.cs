namespace AI.Customer.Service.Common;

/// <summary>可安全返回给调用方的业务错误，禁止携带聊天正文、邮箱和认证凭据。</summary>
/// <param name="code">稳定的前端错误码。</param>
/// <param name="message">不含敏感信息的中文定位说明。</param>
/// <param name="statusCode">对应的 HTTP 状态码。</param>
public sealed class BusinessException(string code, string message, int statusCode = 409)
    : Exception(message)
{
    /// <summary>前端用于恢复状态或提示重试的错误码。</summary>
    public string Code { get; } = code;

    /// <summary>业务错误对应的 HTTP 状态码。</summary>
    public int StatusCode { get; } = statusCode;

    /// <summary>创建状态或版本冲突错误。</summary>
    public static BusinessException Conflict(string message) => new("STATE_CONFLICT", message);

    /// <summary>创建归属、成员或认领权限不足错误。</summary>
    public static BusinessException Forbidden(string message = "无权访问当前业务范围") => new("FORBIDDEN", message, 403);

    /// <summary>创建参数非法错误。</summary>
    public static BusinessException Invalid(string message) => new("INVALID_ARGUMENT", message, 400);

    /// <summary>创建依赖不可用错误，不把失败伪装成空结果。</summary>
    public static BusinessException Unavailable(string message) => new("DEPENDENCY_UNAVAILABLE", message, 503);
}
