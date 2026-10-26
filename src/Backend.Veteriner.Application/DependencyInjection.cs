using System.Reflection;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Files;
using Backend.Veteriner.Application.Files.Access;
using Backend.Veteriner.Application.Hospitalizations.Wards.Access;
using Backend.Veteriner.Application.Imaging.Access;
using Backend.Veteriner.Application.Imports;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Reading;
using Backend.Veteriner.Application.Imports.Rollback;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Application.LabResults.Access;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Invites;
using Backend.Veteriner.Application.OwnerPortal.Sessions;
using Backend.Veteriner.Application.Pets.Timeline;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Sales;
using Backend.Veteriner.Application.SalesPartners;
using Backend.Veteriner.Application.StockBatches;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tenants.Billing;
using Backend.Veteriner.Application.Tenants.Invites;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Veteriner.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // Mediator kaydı ve pipeline sırası Api bileşim kökünde (MediatorConfiguration, ADR-038): kaynak üretici yalnız orada çalışır.

        services.AddScoped<TenantSubscriptionEffectiveWriteEvaluator>();
        services.AddScoped<ITrialLimitPolicy, TrialLimitPolicy>();
        services.AddScoped<TenantSubscriptionSeatEvaluator>();
        services.AddScoped<TenantInviteAcceptanceService>();
        services.AddScoped<ISubscriptionCheckoutActivationService, SubscriptionCheckoutActivationService>();

        // Satış ortağı indirim kodu (ADR-037): kod kullanımı, checkout indirimi ve tahsilat komisyonu.
        services.AddScoped<DiscountCodeRedemptionService>();
        services.AddScoped<ISalesPartnerDiscountBilling, SalesPartnerDiscountBilling>();
        services.AddScoped<IClinicAssignmentAccessGuard, ClinicAssignmentAccessGuard>();
        services.AddScoped<IClinicReadScopeResolver, ClinicReadScopeResolver>();
        services.AddScoped<IHospitalizationWardClinicAccess, HospitalizationWardClinicAccess>();
        services.AddScoped<StockBatchResolver>();
        services.AddScoped<ExpiredStockReader>();
        services.AddScoped<SaleDtoAssembler>();
        services.AddScoped<RegisterReportRunner>();

        // Hasta sahibi portalı (ADR-031): sahiplik kontrolü, davet çözümü, oturum ve kod.
        services.AddScoped<OwnerPortalAccess>();
        services.AddScoped<OwnerPortalStaffAccess>();
        services.AddScoped<OwnerPortalInviteResolver>();
        services.AddScoped<OwnerPortalSessionIssuer>();
        services.AddScoped<OwnerPortalCodeService>();

        // Veri içe aktarma (ADR-019): dosya okuma, katalog çözümü, parti yanıtı sütun bilgisi.
        services.AddOptions<ImportFileLimits>();
        services.AddScoped<ImportUploadReader>();
        services.AddScoped<ImportCatalogLoader>();
        services.AddScoped<ImportTableInfoLoader>();
        services.AddScoped<ImportExistingDataLoader>();
        services.AddScoped<ImportValidationRunner>();
        services.AddScoped<ImportReminderImpactReader>();
        services.AddScoped<ImportRollbackPlanLoader>();

        // Pet timeline: her kayıt türü kendi okuyucusuyla bağlanır (ödeme okuyucusu Aşama 3 sonrası eklenecek).
        services.AddScoped<IPetTimelineSource, VisitTimelineSource>();
        services.AddScoped<IPetTimelineSource, ExaminationTimelineSource>();
        services.AddScoped<IPetTimelineSource, TreatmentTimelineSource>();
        services.AddScoped<IPetTimelineSource, PrescriptionTimelineSource>();
        services.AddScoped<IPetTimelineSource, LabResultTimelineSource>();
        services.AddScoped<IPetTimelineSource, VaccinationTimelineSource>();
        services.AddScoped<IPetTimelineSource, HospitalizationTimelineSource>();
        services.AddScoped<IPetTimelineSource, AdministrationTimelineSource>();
        services.AddScoped<IPetTimelineSource, ImagingTimelineSource>();

        // Dosya katmanı (IMAGING_API_CONTRACT.md): sahip türü başına erişim kuralı, ortak okuma ve iptalde erişim kapatma.
        services.AddScoped<IStoredFileOwnerAccess, ImagingRecordFileOwnerAccess>();
        services.AddScoped<IStoredFileOwnerAccess, LabResultFileOwnerAccess>();
        services.AddScoped<StoredFileContentReader>();
        services.AddScoped<StoredFileAccessCloser>();

        // =========================================================
        // FluentValidation
        // - T�m validator'lar? bu assembly �zerinden otomatik kaydeder.
        // - ValidationBehavior pipeline'? bu validator'lar? �al??t?r?r.
        // =========================================================
        services.AddValidatorsFromAssembly(assembly);

        return services;
    }
}
