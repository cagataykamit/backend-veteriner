using Backend.Veteriner.Domain.Tenants;
using FluentValidation;

namespace Backend.Veteriner.Application.Common.Validation;

public static class TenantNameValidationExtensions
{
    /// <summary>Platform kiracısına ayrılmış ad müşteri kiracısı için kullanılamaz.</summary>
    public static IRuleBuilderOptions<T, string> NotReservedTenantName<T>(this IRuleBuilder<T, string> ruleBuilder)
        => ruleBuilder
            .Must(name => !Tenant.IsReservedName(name))
            .WithMessage("Bu kiracı adı sistem tarafından ayrılmıştır.");
}
