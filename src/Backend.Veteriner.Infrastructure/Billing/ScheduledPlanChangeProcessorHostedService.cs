using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Tenants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

namespace Backend.Veteriner.Infrastructure.Billing;

public sealed class ScheduledPlanChangeProcessorHostedService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ScheduledPlanChangeProcessorOptions _opt;

    public ScheduledPlanChangeProcessorHostedService(
        IServiceProvider sp,
        IOptions<ScheduledPlanChangeProcessorOptions> opt)
    {
        _sp = sp;
        _opt = opt.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opt.Enabled)
            return;

        var interval = TimeSpan.FromSeconds(Math.Max(10, _opt.IntervalSeconds));
        var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _sp.CreateScope();
                var changesRead = scope.ServiceProvider.GetRequiredService<IReadRepository<ScheduledSubscriptionPlanChange>>();
                var changesWrite = scope.ServiceProvider.GetRequiredService<IRepository<ScheduledSubscriptionPlanChange>>();
                var subsRead = scope.ServiceProvider.GetRequiredService<IReadRepository<TenantSubscription>>();
                var subsWrite = scope.ServiceProvider.GetRequiredService<IRepository<TenantSubscription>>();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                var now = DateTime.UtcNow;
                var due = await changesRead.ListAsync(new DueScheduledPlanChangesSpec(now, Math.Max(1, _opt.BatchSize)), stoppingToken);
                if (due.Count == 0)
                    continue;

                var hasChanges = false;
                foreach (var item in due)
                {
                    if (item.Status != SubscriptionPlanChangeStatus.Scheduled || item.EffectiveAtUtc > now)
                        continue;

                    var sub = await subsRead.FirstOrDefaultAsync(new TenantSubscriptionByTenantIdSpec(item.TenantId), stoppingToken);
                    if (sub is null)
                    {
                        item.Cancel(now);
                        await changesWrite.UpdateAsync(item, stoppingToken);
                        hasChanges = true;
                        continue;
                    }

                    // Idempotent apply: abonelik zaten hedefte aktifse yalnız change kaydını applied yaparız.
                    sub.ActivatePaidPlan(item.TargetPlanCode, now);
                    item.MarkApplied(now);

                    await subsWrite.UpdateAsync(sub, stoppingToken);
                    await changesWrite.UpdateAsync(item, stoppingToken);
                    hasChanges = true;
                }

                if (hasChanges)
                    await uow.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Scheduled plan change processor failed.");
            }
        }
    }
}
