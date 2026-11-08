using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Waitlist;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>Bekleme listesi kaydı (TIER-018). Sıra (klinik + durum + sıra anahtarı) ve hayvan başına liste.</summary>
public sealed class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> b)
    {
        b.ToTable("WaitlistEntries");
        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.ClientId).IsRequired();
        b.Property(x => x.PetId).IsRequired();
        b.Property(x => x.AppointmentType).HasConversion<int>().IsRequired();
        b.Property(x => x.PreferredDays).HasConversion<int>().IsRequired();
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.Note).HasMaxLength(WaitlistEntry.NoteMaxLength);
        b.Property(x => x.CloseReason).HasMaxLength(WaitlistEntry.CloseReasonMaxLength);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Ignore(x => x.IsWaiting);

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.Status, x.SortKey });
        b.HasIndex(x => new { x.TenantId, x.PetId });

        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Pet>().WithMany().HasForeignKey(x => x.PetId).OnDelete(DeleteBehavior.NoAction);
    }
}

/// <summary>
/// Bekleme listesi teklifi (TIER-018). Eşzamanlılık kuralı veritabanındadır: kayıt başına ve boşalan saat başına en çok bir açık teklif,
/// saat başına en çok bir kabul (süzülmüş benzersiz indeksler); durum değişimi satır sürümüyle korunur.
/// </summary>
public sealed class WaitlistOfferConfiguration : IEntityTypeConfiguration<WaitlistOffer>
{
    public void Configure(EntityTypeBuilder<WaitlistOffer> b)
    {
        b.ToTable("WaitlistOffers");
        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.WaitlistEntryId).IsRequired();
        b.Property(x => x.SourceAppointmentId).IsRequired();
        b.Property(x => x.SlotStartUtc).IsRequired();
        b.Property(x => x.DurationMinutes).IsRequired();
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.ExpiresAtUtc).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Ignore(x => x.IsOpen);

        var open = $"[Status] = {(int)WaitlistOfferStatus.Open}";
        b.HasIndex(x => x.WaitlistEntryId, "UX_WaitlistOffers_Entry_Open").IsUnique().HasFilter(open);
        b.HasIndex(x => x.SourceAppointmentId, "UX_WaitlistOffers_Slot_Open").IsUnique().HasFilter(open);
        b.HasIndex(x => x.SourceAppointmentId, "UX_WaitlistOffers_Slot_Accepted").IsUnique()
            .HasFilter($"[Status] = {(int)WaitlistOfferStatus.Accepted}");
        b.HasIndex(x => new { x.TenantId, x.Status, x.ExpiresAtUtc });
        b.HasIndex(x => new { x.TenantId, x.SourceAppointmentId });

        b.HasOne<WaitlistEntry>().WithMany().HasForeignKey(x => x.WaitlistEntryId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Appointment>().WithMany().HasForeignKey(x => x.SourceAppointmentId).OnDelete(DeleteBehavior.NoAction);
    }
}

/// <summary>Bekleme kaydının geçmişi (TIER-018); yalnız eklenir.</summary>
public sealed class WaitlistEntryEventConfiguration : IEntityTypeConfiguration<WaitlistEntryEvent>
{
    public void Configure(EntityTypeBuilder<WaitlistEntryEvent> b)
    {
        b.ToTable("WaitlistEntryEvents");
        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.WaitlistEntryId).IsRequired();
        b.Property(x => x.Type).HasConversion<int>().IsRequired();
        b.Property(x => x.OccurredAtUtc).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.WaitlistEntryId, x.OccurredAtUtc });

        b.HasOne<WaitlistEntry>().WithMany().HasForeignKey(x => x.WaitlistEntryId).OnDelete(DeleteBehavior.Cascade);
    }
}
