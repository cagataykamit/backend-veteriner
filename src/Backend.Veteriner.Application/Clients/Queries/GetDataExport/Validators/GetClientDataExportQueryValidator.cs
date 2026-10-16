using Backend.Veteriner.Application.Clients.Queries.GetDataExport;
using FluentValidation;

namespace Backend.Veteriner.Application.Clients.Queries.GetDataExport.Validators;

public sealed class GetClientDataExportQueryValidator : AbstractValidator<GetClientDataExportQuery>
{
    public GetClientDataExportQueryValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
    }
}
