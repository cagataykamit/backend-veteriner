using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public const string WalkInIndexName = "UX_Clients_TenantId_WalkIn";

    public void Configure(EntityTypeBuilder<Client> b)
    {
        b.ToTable("Clients");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId)
            .IsRequired();

        b.Property(x => x.CreatedAtUtc)
            .IsRequired();

        b.Property(x => x.UpdatedAtUtc)
            .IsRequired(false);

        b.Property(x => x.FullName)
            .IsRequired()
            .HasMaxLength(300);

        b.Property(x => x.Email)
            .HasMaxLength(320);

        b.Property(x => x.Phone)
            .HasMaxLength(50);

        b.Property(x => x.Address)
            .HasMaxLength(500);

        b.Property(x => x.PhoneNormalized)
            .HasMaxLength(50);

        b.Property(x => x.IsWalkIn)
            .IsRequired()
            .HasDefaultValue(false);

        // Mevcut satırlar tercihsiz ve hatırlatmaya açık kalır (ADR-020 S5).
        b.Property(x => x.PreferredContactChannel)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(ContactChannel.None);

        b.Property(x => x.RemindersOptedOut)
            .IsRequired()
            .HasDefaultValue(false);

        // Mevcut satırlar "Yok" (form alınmadı) olarak kalır (ADR-021).
        b.Property(x => x.KvkkConsentStatus)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(KvkkConsentStatus.None);

        b.Property(x => x.KvkkConsentRecordedOn);

        b.Property(x => x.KvkkConsentRecordedByUserId);

        // ADR-032: mevcut satırlar null kalır ("sürüm bilinmiyor"); geriye dönük doldurulmaz.
        b.Property(x => x.KvkkConsentTemplateVersion);

        b.Property(x => x.KvkkConsentTemplateHash)
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(ClinicKvkkFormTemplate.TextHashLength);

        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.FullName });

        // Peşin müşteri (doğrudan satış): kiracıda en fazla bir kayıt.
        b.HasIndex(x => new { x.TenantId, x.IsWalkIn })
            .IsUnique()
            .HasFilter("[IsWalkIn] = 1")
            .HasDatabaseName(WalkInIndexName);

        b.HasIndex(x => new { x.TenantId, x.Email, x.PhoneNormalized })
            .IsUnique()
            .HasFilter("[Email] IS NOT NULL AND [PhoneNormalized] IS NOT NULL");
    }
}
