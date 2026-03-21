using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Backend.Veteriner.Api.Swagger;

public sealed class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
{
    private readonly IApiVersionDescriptionProvider _provider;

    public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
        => _provider = provider;

    public void Configure(SwaggerGenOptions options)
    {
        // Versioned docs
        foreach (var desc in _provider.ApiVersionDescriptions)
        {
            options.SwaggerDoc(desc.GroupName, new OpenApiInfo
            {
                Title = "Backend Veteriner API",
                Version = desc.ApiVersion.ToString(),
                Description = desc.IsDeprecated ? "DEPRECATED" : null
            });
        }

        // ✅ Kritik: Endpoint dahil etmede ApiExplorer GroupName kullan
        // (Reflection ile v string format eşleştirmesi yüzünden boş spec üretme riskini kaldırır)
        options.DocInclusionPredicate((docName, apiDesc) =>
        {
            if (string.IsNullOrWhiteSpace(apiDesc.GroupName))
                return false;

            return string.Equals(apiDesc.GroupName, docName, StringComparison.OrdinalIgnoreCase);
        });

        // (Opsiyonel) Swagger’da version parametresi gösterimini toparlamak için
        options.OperationFilter<SwaggerDefaultValues>();

        // (Opsiyonel) XML comments (dosya yoksa sessiz geç)
        TryIncludeXmlComments(options);

        // ✅ JWT Bearer
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "JWT token'ınızı 'Bearer ' önekiyle girin."
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    }

    private static void TryIncludeXmlComments(SwaggerGenOptions options)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var xmlFile = $"{assembly.GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

            if (File.Exists(xmlPath))
                options.IncludeXmlComments(xmlPath);
        }
        catch
        {
            // Kurumsal: Swagger dokümantasyonu yüzünden startup'ı kırma.
        }
    }

    /// <summary>
    /// ApiExplorer’ın ürettiği versiyon parametreleri ve deprecations için Swashbuckle uyarlaması.
    /// </summary>
    private sealed class SwaggerDefaultValues : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var apiDescription = context.ApiDescription;

            operation.Deprecated |= apiDescription.IsDeprecated();

            if (operation.Parameters == null)
                return;

            foreach (var parameter in operation.Parameters)
            {
                var description = apiDescription.ParameterDescriptions
                    .FirstOrDefault(p => p.Name == parameter.Name);

                if (description is null) continue;

                parameter.Description ??= description.ModelMetadata?.Description;

                if (parameter.Schema.Default is null && description.DefaultValue is not null)
                {
                    // default değerleri swagger'a yansıtma
                    parameter.Schema.Default = OpenApiAnyFactory.CreateFromJson(
                        System.Text.Json.JsonSerializer.Serialize(description.DefaultValue)
                    );
                }

                parameter.Required |= description.IsRequired;
            }
        }
    }
}
