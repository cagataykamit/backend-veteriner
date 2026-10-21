using FluentValidation;

namespace Backend.Veteriner.Application.Clients.Queries.GetInvoicingIdentity;

public sealed class GetClientInvoicingIdentityQueryValidator : AbstractValidator<GetClientInvoicingIdentityQuery>
{
    public GetClientInvoicingIdentityQueryValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
    }
}
