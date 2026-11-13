using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AI.Customer.Service.Service.Workers;

/// <summary>每五秒恢复可靠任务，每轮独立依赖作用域，不依赖线程本地请求上下文。</summary>
/// <param name="scopes">创建各轮独立事务作用域。</param>
/// <param name="logger">后台错误日志。</param>
public sealed class ReliableTaskWorker(IServiceScopeFactory scopes, ILogger<ReliableTaskWorker> logger) : BackgroundService
{
    /// <summary>可取消地运行扫描，异常不终止后续恢复。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ReliableTaskProcessor>().RunOnceAsync(stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError("可靠任务扫描失败 Failure={Failure}", error.GetType().Name);
            }
        }
    }
}
