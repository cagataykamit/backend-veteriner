using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants;
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
    private readonly TimeProvider _timeProvider;

    public ScheduledPlanChangeProcessorHostedService(
        IServiceProvider sp,
        IOptions<ScheduledPlanChangeProcessorOptions> opt,
        TimeProvider timeProvider)
    {
        _sp = sp;
        _opt = opt.Value;
        _timeProvider = timeProvider;
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
                await ProcessDueOnceAsync(stoppingToken);
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

    /// <summary>Vadesi gelen planlı değişiklikleri bir kez işler; zamanlayıcının her turu ve entegrasyon testleri buradan geçer.</summary>
    public async Task ProcessDueOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = _sp.CreateScope();
        var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantScope>();
        var changesRead = scope.ServiceProvider.GetRequiredService<IReadRepository<ScheduledSubscriptionPlanChange>>();
        var changesWrite = scope.ServiceProvider.GetRequiredService<IRepository<ScheduledSubscriptionPlanChange>>();
        var subsRead = scope.ServiceProvider.GetRequiredService<IReadRepository<TenantSubscription>>();
        var subsWrite = scope.ServiceProvider.GetRequiredService<IRepository<TenantSubscription>>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var planFeatures = scope.ServiceProvider.GetRequiredService<TenantPlanFeatureEvaluator>();

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Vadesi gelen değişiklikleri bulmak kiracılar arasıdır (ADR-040); her değişiklik kendi kiracısının
        // kapsamında uygulanır ve kaydedilir.
        IReadOnlyList<ScheduledSubscriptionPlanChange> due;
        using (tenantScope.AsSystem())
            due = await changesRead.ListAsync(new DueScheduledPlanChangesSpec(now, Math.Max(1, _opt.BatchSize)), stoppingToken);

        foreach (var item in due)
        {
            if (item.Status != SubscriptionPlanChangeStatus.Scheduled || item.EffectiveAtUtc > now)
                continue;

            using var tenant = tenantScope.UseTenant(item.TenantId);
            var sub = await subsRead.FirstOrDefaultAsync(new TenantSubscriptionByTenantIdSpec(item.TenantId), stoppingToken);
            if (sub is null)
            {
                item.Cancel(now);
                await changesWrite.UpdateAsync(item, stoppingToken);
                await uow.SaveChangesAsync(stoppingToken);
                continue;
            }

            // Seçenek A (ADR-035 2026-11-08): planlandıktan sonra sınır aşıldıysa düşürme uygulanmaz, kiracı mevcut pakette
            // kalır; panel nedeni abonelik özetinde (rejectedPlanChange) gösterir. Kayıt silinmez.
            var fit = await planFeatures.GetPlanFitAsync(item.TenantId, item.TargetPlanCode, stoppingToken);
            if (!fit.Fits)
            {
                item.Reject();
                await changesWrite.UpdateAsync(item, stoppingToken);
                await uow.SaveChangesAsync(stoppingToken);
                Log.Warning(
                    "Scheduled downgrade rejected at period end: Tenant {TenantId}, target {TargetPlan}, excess clinics {ExcessClinics}, excess users {ExcessUsers}.",
                    item.TenantId, item.TargetPlanCode, fit.ExcessClinicCount, fit.ExcessUserCount);
                continue;
            }

            // Yalnız paket değişir; dönem ödemeyle yenilenir (TenantSubscription.ApplyScheduledDowngrade).
            sub.ApplyScheduledDowngrade(item.TargetPlanCode, now);
            item.MarkApplied(now);

            await subsWrite.UpdateAsync(sub, stoppingToken);
            await changesWrite.UpdateAsync(item, stoppingToken);
            await uow.SaveChangesAsync(stoppingToken);
        }
    }
}
