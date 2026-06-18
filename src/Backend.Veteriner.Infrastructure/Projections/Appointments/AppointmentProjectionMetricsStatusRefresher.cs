using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Projections.Appointments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Projections.Appointments;

internal static class AppointmentProjectionMetricsStatusRefresher
{
    public static async Task RefreshAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var statusReader = serviceProvider.GetRequiredService<IAppointmentProjectionStatusReader>();
        var queryOptions = serviceProvider.GetRequiredService<IOptions<QueryReadModelsOptions>>().Value;
        var holder = serviceProvider.GetRequiredService<AppointmentProjectionMetricsSnapshotHolder>();

        var status = await statusReader.GetStatusAsync(cancellationToken);
        holder.Update(AppointmentProjectionMetricsSnapshotFactory.Create(status, queryOptions));
    }
}
