using Momentte.Api.Application.Interfaces;
namespace Momentte.Api.Infrastructure.Implementations;
public sealed class EventRetentionWorker(IServiceScopeFactory scopes, ILogger<EventRetentionWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<IEventService>().Purge(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Falha ao remover mídias expiradas; será tentado novamente."); }
        }
    }
}
