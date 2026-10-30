using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class AppointmentRequestConfiguration : IEntityTypeConfiguration<AppointmentRequest>
{
    public void Configure(EntityTypeBuilder<AppointmentRequest> b)
    {
        b.ToTable("AppointmentRequests");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.Source).IsRequired().HasConversion<int>();
        b.Property(x => x.Status).IsRequired().HasConversion<int>();
        b.Property(x => x.ReasonId).IsRequired();
        b.Property(x => x.ReasonName).IsRequired().HasMaxLength(AppointmentReason.NameMaxLength);
        b.Property(x => x.RequestedStartUtc).IsRequired();
        b.Property(x => x.DurationMinutes).IsRequired();
        b.Property(x => x.ContactName).IsRequired().HasMaxLength(AppointmentRequest.ContactNameMaxLength);
        b.Property(x => x.ContactEmail).IsRequired().HasMaxLength(AppointmentRequest.EmailMaxLength);
        b.Property(x => x.ContactPhone).HasMaxLength(AppointmentRequest.PhoneMaxLength);
        b.Property(x => x.PetName).HasMaxLength(AppointmentRequest.PetNameMaxLength);
        b.Property(x => x.PetSpecies).HasMaxLength(AppointmentRequest.PetSpeciesMaxLength);
        b.Property(x => x.PetBreed).HasMaxLength(AppointmentRequest.PetBreedMaxLength);
        b.Property(x => x.OwnerNote).HasMaxLength(AppointmentRequest.NoteMaxLength);
        b.Property(x => x.RejectionReason).HasMaxLength(AppointmentRequest.RejectionReasonMaxLength);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();

        // Kuyruk (durum + klinik + saat), bekleyen talep sınırı (e-posta) ve süresi dolan tarama.
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.Status, x.RequestedStartUtc });
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.ContactEmail, x.Status });
        b.HasIndex(x => new { x.Status, x.RequestedStartUtc });
        b.HasIndex(x => new { x.TenantId, x.OwnerAccountId });

        b.OwnsMany(x => x.History, h =>
        {
            h.ToTable("AppointmentRequestStatusChanges");
            h.WithOwner().HasForeignKey("AppointmentRequestId");
            // Gölge kimlik anahtarı: geçmişe eklenen satır her zaman INSERT olur.
            h.Property<long>("Id").ValueGeneratedOnAdd();
            h.HasKey("Id");
            h.Property(x => x.FromStatus).HasConversion<int?>();
            h.Property(x => x.ToStatus).IsRequired().HasConversion<int>();
            h.Property(x => x.ActorKind).IsRequired().HasConversion<int>();
            h.Property(x => x.Note).HasMaxLength(AppointmentRequest.RejectionReasonMaxLength);
            h.Property(x => x.AtUtc).IsRequired();
        });
        b.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<AppointmentReason>()
            .WithMany()
            .HasForeignKey(x => x.ReasonId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AppointmentRequestEmailVerificationConfiguration : IEntityTypeConfiguration<AppointmentRequestEmailVerification>
{
    public void Configure(EntityTypeBuilder<AppointmentRequestEmailVerification> b)
    {
        b.ToTable("AppointmentRequestEmailVerifications");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.Email).IsRequired().HasMaxLength(AppointmentRequest.EmailMaxLength);
        b.Property(x => x.CodeHash).IsRequired().HasMaxLength(64).IsUnicode(false);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.ExpiresAtUtc).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.Email, x.CreatedAtUtc });

        b.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ClinicAppointmentRequestEmailTemplateConfiguration : IEntityTypeConfiguration<ClinicAppointmentRequestEmailTemplate>
{
    public void Configure(EntityTypeBuilder<ClinicAppointmentRequestEmailTemplate> b)
    {
        b.ToTable("ClinicAppointmentRequestEmailTemplates");

        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.Kind).IsRequired().HasConversion<int>();
        b.Property(x => x.Subject).IsRequired().HasMaxLength(ClinicAppointmentRequestEmailTemplate.SubjectMaxLength);
        b.Property(x => x.Body).IsRequired().HasMaxLength(ClinicAppointmentRequestEmailTemplate.BodyMaxLength);
        b.Property(x => x.CreatedAtUtc).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.Kind }).IsUnique();

        b.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
