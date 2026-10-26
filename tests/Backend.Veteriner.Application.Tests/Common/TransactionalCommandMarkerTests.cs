using Backend.Veteriner.Application.Clinics.Commands.WorkingHours.UpdateClinicWorkingHours;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Commands.Create;
using Backend.Veteriner.Application.Public.Commands.OwnerSignup;
using Backend.Veteriner.Application.Public.Commands.SignupAndAcceptInvite;
using Backend.Veteriner.Application.Tenants.Commands.ClearExampleClinic;
using Backend.Veteriner.Application.Tenants.Commands.Create;
using Backend.Veteriner.Application.Tenants.Commands.LoadExampleClinic;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Common;

/// <summary>
/// Repository yazımları (AddAsync/UpdateAsync/DeleteAsync) her çağrıda SaveChanges çalıştırır; birden fazla
/// yazım yapan komutlar TransactionBehavior ile tek işlemde koşmalı, yoksa ortada hata yarım kayıt bırakır.
/// Bu test işaretin yanlışlıkla kaldırılmasını engeller.
/// </summary>
public sealed class TransactionalCommandMarkerTests
{
    [Theory]
    [InlineData(typeof(PublicOwnerSignupCommand))]
    [InlineData(typeof(SignupAndAcceptTenantInviteCommand))]
    [InlineData(typeof(CreateTenantCommand))]
    [InlineData(typeof(LoadExampleClinicCommand))]
    [InlineData(typeof(ClearExampleClinicCommand))]
    [InlineData(typeof(CreateExaminationCommand))]
    [InlineData(typeof(UpdateClinicWorkingHoursCommand))]
    public void MultiWriteCommand_Should_RollBackOnFailure(Type commandType)
        => typeof(ITransactionalRollbackOnFailureRequest).IsAssignableFrom(commandType)
            .Should().BeTrue($"{commandType.Name} birden fazla yazım yapar; tek işlemde koşmalı");
}
