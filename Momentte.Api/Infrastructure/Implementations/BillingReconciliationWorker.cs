using Dapper;
using Momentte.Api.Application.Interfaces;
using Momentte.Api.Infrastructure.Interfaces;
namespace Momentte.Api.Infrastructure.Implementations;
public sealed class BillingReconciliationWorker(IServiceScopeFactory scopes, ILogger<BillingReconciliationWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (!scope.ServiceProvider.GetRequiredService<IMercadoPagoGateway>().Available) continue;
                using var db = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnection();
                var owners = await db.QueryAsync<Guid>("""
                    SELECT a.owner_id FROM billing_accounts a
                    WHERE (a.last_synced_at IS NULL OR a.last_synced_at<CURRENT_TIMESTAMP-INTERVAL '5 minutes')
                      AND (EXISTS(SELECT 1 FROM billing_contracts c WHERE c.owner_id=a.owner_id AND c.provider_id IS NOT NULL)
                        OR EXISTS(SELECT 1 FROM billing_orders o WHERE o.owner_id=a.owner_id AND o.status='pending'))
                    ORDER BY a.last_synced_at NULLS FIRST LIMIT 50
                    """);
                foreach (var owner in owners)
                {
                    try { await scope.ServiceProvider.GetRequiredService<IBillingService>().Reconcile(owner, stoppingToken); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Falha na conciliação da conta {Owner}", owner); }
                    await db.ExecuteAsync("UPDATE billing_accounts SET last_synced_at=CURRENT_TIMESTAMP WHERE owner_id=@owner", new { owner });
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Falha no ciclo de conciliação de pagamentos."); }
        }
    }
}
