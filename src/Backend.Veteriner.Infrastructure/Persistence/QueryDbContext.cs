using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Backend.Veteriner.Infrastructure.Persistence;

/// <summary>
/// CQRS query tarafı için ayrı SQL Server veritabanı context'i.
/// Projection read-model tablolarını yönetir; command entity configuration'ları yüklenmez.
/// </summary>
public sealed class QueryDbContext : DbContext
{
    public QueryDbContext(DbContextOptions<QueryDbContext> options) : base(options) { }

    public DbSet<AppointmentReadModel> AppointmentReadModels => Set<AppointmentReadModel>();
    public DbSet<ClientReadModel> ClientReadModels => Set<ClientReadModel>();
    public DbSet<PetReadModel> PetReadModels => Set<PetReadModel>();
    public DbSet<ClinicPetActivityReadModel> ClinicPetActivityReadModels => Set<ClinicPetActivityReadModel>();
    public DbSet<ClinicClientActivityReadModel> ClinicClientActivityReadModels => Set<ClinicClientActivityReadModel>();
    public DbSet<ClinicDailyAppointmentStatsReadModel> ClinicDailyAppointmentStatsReadModels =>
        Set<ClinicDailyAppointmentStatsReadModel>();
    public DbSet<ClinicDailyPaymentStatsReadModel> ClinicDailyPaymentStatsReadModels =>
        Set<ClinicDailyPaymentStatsReadModel>();
    public DbSet<ProcessedProjectionEvent> ProcessedProjectionEvents => Set<ProcessedProjectionEvent>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(QueryDbContext).Assembly,
            type => type.Namespace == "Backend.Veteriner.Infrastructure.Persistence.Query.Configurations");
    }
}
