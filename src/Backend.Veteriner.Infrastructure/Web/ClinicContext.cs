using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Clinic;
using Microsoft.AspNetCore.Http;

namespace Backend.Veteriner.Infrastructure.Web;

public sealed class ClinicContext : IClinicContext
{
    private readonly IHttpContextAccessor _accessor;

    public ClinicContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public Guid? ClinicId
    {
        get
        {
            var items = _accessor.HttpContext?.Items;
            if (items is null ||
                !items.TryGetValue(ClinicHttpContextKeys.ResolvedClinicId, out var raw) ||
                raw is not Guid g)
                return null;
            return g;
        }
    }
}

