using Backend.Veteriner.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> b)
    {
        b.ToTable("UserNotifications");

        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.Type).IsRequired().HasConversion<int>();
        b.Property(x => x.Title).IsRequired().HasMaxLength(UserNotification.TitleMaxLength);
        b.Property(x => x.Body).HasMaxLength(UserNotification.BodyMaxLength);
        b.Property(x => x.TargetType).HasMaxLength(UserNotification.TargetTypeMaxLength).IsUnicode(false);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Ignore(x => x.IsRead);

        // Kullanıcının listesi (yeniden eskiye) ve okunmamış rozeti.
        b.HasIndex(x => new { x.TenantId, x.UserId, x.CreatedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.UserId, x.ReadAtUtc });
    }
}
