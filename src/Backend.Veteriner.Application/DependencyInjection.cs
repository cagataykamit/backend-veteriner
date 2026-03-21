using System.Reflection;
using Backend.Veteriner.Application.Common.Behaviors;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Veteriner.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // =========================================================
        // MediatR
        // - T�m Command/Query handler'lar?n? bu assembly �zerinden otomatik kaydeder.
        // =========================================================
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));

        // =========================================================
        // FluentValidation
        // - T�m validator'lar? bu assembly �zerinden otomatik kaydeder.
        // - ValidationBehavior pipeline'? bu validator'lar? �al??t?r?r.
        // =========================================================
        services.AddValidatorsFromAssembly(assembly);

        // =========================================================
        // MediatR Pipeline Behaviors (Kurumsal s?ra)
        //
        // 1) UnhandledExceptionBehavior
        //    - En d?? katmanda �al???r.
        //    - ?� katmanlarda olu?an t�m exception'lar? yakalayarak standartla?t?r?r.
        //
        // 2) ValidationBehavior
        //    - Handler �al??madan �nce input do?rulamas? yapar.
        //    - Invalid request'leri erken keser (performans + g�venlik).
        //
        // 3) AuditBehavior
        //    - Yaln?zca do?rulanm?? request'ler i�in denetim izi �retir.
        //    - Admin aksiyonlar?n? merkezi ve tutarl? bi�imde loglar.
        //
        // 4) PerformanceBehavior
        //    - Handler y�r�t�m s�resini �l�er.
        //    - Slow request / slow command tespiti i�in temel sa?lar.
        //
        // 5) LoggingBehavior
        //    - ??lem bazl? log zenginle?tirme ve standart log �retimi.
        //    - (Sizdeki implementasyona g�re request/response meta bilgisi ta??r.)
        // =========================================================
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));

        return services;
    }
}
