using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Domain.Shared;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Examinations;

/// <summary>Kesinleştirme hata kodlarının HTTP eşlemesi (sözleşme: 409).</summary>
public sealed class ExaminationFinalizeErrorMappingTests
{
    private sealed class TestController : ControllerBase { }

    private static int StatusOf(string code)
    {
        var controller = new TestController
        {
            ControllerContext = new ControllerContext
            {
                // Hata gövdesindeki timestampUtc DI saatinden okunur.
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = new ServiceCollection().AddSingleton(TimeProvider.System).BuildServiceProvider()
                }
            },
        };

        var result = Result.Failure(code, "x").ToActionResult(controller);

        return ((ObjectResult)result).StatusCode!.Value;
    }

    [Theory]
    [InlineData("Examinations.Finalized")]
    [InlineData("Examinations.AlreadyFinalized")]
    [InlineData("Examinations.NotFinalized")]
    [InlineData("Examinations.ConcurrencyConflict")]
    public void Finalize_Related_Conflicts_Should_Map_To_409(string code)
        => StatusOf(code).Should().Be(StatusCodes.Status409Conflict);

    [Fact]
    public void Unrelated_Codes_Keep_Their_Mapping()
    {
        StatusOf("Examinations.NotFound").Should().Be(StatusCodes.Status404NotFound);
        StatusOf("Examinations.Validation").Should().Be(StatusCodes.Status400BadRequest);
        StatusOf("Clinics.AccessDenied").Should().Be(StatusCodes.Status403Forbidden);
    }
}
