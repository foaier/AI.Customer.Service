using AI.Customer.Service.Domain.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AI.Customer.Service.Service.Workers;

/// <summary>每十秒恢复未完成资格评估的成功轮次，不创建或扫描用户沉默期限。</summary>
/// <param name="scopes">创建独立依赖和事务作用域。</param>
/// <param name="logger">仅记录业务引用及失败类型。</param>
public sealed class SurveyRecoveryWorker(IServiceScopeFactory scopes, ILogger<SurveyRecoveryWorker> logger) : BackgroundService
{
    /// <summary>按有界批次恢复，已评估轮次不会占用下一批查询。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var pending = await scope.ServiceProvider.GetRequiredService<IConversationRepository>().SurveyCandidatesAsync(100, stoppingToken);
                foreach (var conversation in pending)
                {
                    await scope.ServiceProvider.GetRequiredService<SurveyService>().EvaluateAsync(conversation.Scope, stoppingToken);
                }
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("调查恢复失败 Failure={Failure}", error.GetType().Name);
            }
        }
    }
}
