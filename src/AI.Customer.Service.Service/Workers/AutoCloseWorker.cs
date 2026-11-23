using AI.Customer.Service.Domain.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AI.Customer.Service.Service.Workers;

/// <summary>
/// 有界恢复已首响且访客沉默的在线接待。
/// </summary>
public sealed class AutoCloseWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<AutoCloseWorker> logger) : BackgroundService
{
    /// <summary>
    /// 每五秒读取最多一百个到期接待并逐一复核结单，隔离单项失败，在宿主取消时停止。
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<IReceptionRepository>();
                var service = scope.ServiceProvider.GetRequiredService<ReceptionService>();
                var now = clock.GetUtcNow().UtcDateTime;
                var candidates = await repository.AutoCloseCandidatesAsync(now, 100, stoppingToken);
                foreach (var candidate in candidates)
                {
                    try
                    {
                        await service.AutoCloseAsync(candidate.Scope, candidate.Id, clock.GetUtcNow().UtcDateTime, stoppingToken);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        logger.LogWarning("Automatic reception close failed ({FailureType})", error.GetType().Name);
                    }
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning("Automatic reception close scan failed ({FailureType})", error.GetType().Name);
            }
        }
    }
}
