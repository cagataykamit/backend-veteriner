using Microsoft.EntityFrameworkCore;
using Serilog.Exceptions.Destructurers;

namespace Backend.Veteriner.Api.Observability;

/// <summary>
/// <see cref="DbUpdateException"/> için yalnız ortak istisna alanları ve InnerException (SqlException numarası, mesajı).
/// <c>Entries</c> yansımayla gezilirse EntityEntry üzerinden DbContext grafiği dolaşılır; benzersiz indeks yarışında
/// tek log yüzlerce ms sürer. Filtre bunu önlemez: yansıma destructurer'ı değeri filtreden önce gezer.
/// </summary>
public sealed class DbUpdateExceptionDestructurer : ExceptionDestructurer
{
    public override Type[] TargetTypes => [typeof(DbUpdateException), typeof(DbUpdateConcurrencyException)];
}
