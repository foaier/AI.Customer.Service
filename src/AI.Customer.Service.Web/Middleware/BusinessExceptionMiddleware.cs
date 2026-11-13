using System.Text.Json;
using AI.Customer.Service.Common;
using Microsoft.EntityFrameworkCore;

namespace AI.Customer.Service.Web.Middleware;

/// <summary>将业务错误统一映射为 Java 客服兼容信封，响应不包含内部异常、SQL 或正文。</summary>
/// <param name="next">后续请求管线。</param>
/// <param name="logger">错误类型日志。</param>
public sealed class BusinessExceptionMiddleware(RequestDelegate next, ILogger<BusinessExceptionMiddleware> logger)
{
    /// <summary>处理业务参数、权限、并发和外部依赖失败，不覆盖已开始的 SSE。</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception error) when (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
        {
            var business = error as BusinessException ?? error switch
            {
                JsonException or BadHttpRequestException => BusinessException.Invalid("请求格式或字段无效"),
                DbUpdateException => BusinessException.Conflict("数据库并发写入冲突"),
                HttpRequestException => BusinessException.Unavailable("外部服务暂不可用"),
                _ => new BusinessException("INTERNAL_RETRYABLE", "服务暂不可用，请保留原请求键恢复状态", 503)
            };
            logger.LogWarning("请求失败 TraceId={TraceId} Code={Code} Failure={Failure}",
                context.TraceIdentifier, business.Code, error.GetType().Name);
            context.Response.StatusCode = business.StatusCode;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new { code = business.Code, message = business.Message, traceId = context.TraceIdentifier }, context.RequestAborted);
        }
    }
}
