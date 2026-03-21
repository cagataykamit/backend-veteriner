using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Backend.Veteriner.Application.Auth.Contracts.Dtos
{
    public sealed record OperationClaimPermissionDto(
     Guid Id,
     Guid OperationClaimId,
     Guid PermissionId,
     string OperationClaimName,
     string PermissionCode,
     string? PermissionDescription);
}
