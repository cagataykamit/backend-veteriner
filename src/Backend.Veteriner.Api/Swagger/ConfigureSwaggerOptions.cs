using System.Reflection;
using Backend.Veteriner.Api.Contracts;
using Backend.Veteriner.Api.Controllers;
using Backend.Veteriner.Application.Appointments.Commands.Create;
using Backend.Veteriner.Application.Appointments.Commands.Update;
using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Application.Hospitalizations.Commands.Create;
using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Application.LabResults.Commands.Create;
using Backend.Veteriner.Application.LabResults.Contracts.Dtos;
using Backend.Veteriner.Application.Payments.Commands.Create;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Prescriptions.Commands.Create;
using Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;
using Backend.Veteriner.Application.Treatments.Commands.Create;
using Backend.Veteriner.Application.Treatments.Contracts.Dtos;
using Microsoft.AspNetCore.Mvc;
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
        options.OperationFilter<ExaminationsListOperationFilter>();
        options.OperationFilter<AuthContractCleanupOperationFilter>();
        options.SchemaFilter<PaymentsContractSchemaFilter>();
        options.SchemaFilter<AppointmentsContractSchemaFilter>();
        options.SchemaFilter<ExaminationsContractSchemaFilter>();
        options.SchemaFilter<TreatmentsContractSchemaFilter>();
        options.SchemaFilter<PrescriptionsContractSchemaFilter>();
        options.SchemaFilter<LabResultsContractSchemaFilter>();
        options.SchemaFilter<HospitalizationsContractSchemaFilter>();
        options.SchemaFilter<ClientsContractSchemaFilter>();

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

    private sealed class PaymentsContractSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type == typeof(CreatePaymentCommand))
            {
                MarkRequired(schema, "clinicId", "clientId", "amount", "currency", "method", "paidAtUtc");
                SetNullable(schema, "currency", false);
                SetNullable(schema, "petId", true);
                SetNullable(schema, "appointmentId", true);
                SetNullable(schema, "examinationId", true);
                SetNullable(schema, "notes", true);
                Describe(schema, "amount", "Tutar; sıfırdan büyük olmalıdır (FluentValidation).");
                Describe(schema, "currency", "Zorunlu. ISO 4217 alpha-3 (örn. TRY); boş/whitespace kabul edilmez.");
                Describe(schema, "method", "Zorunlu. 0=Cash, 1=Card, 2=Transfer.");
                Describe(schema, "paidAtUtc", "Ödeme anı UTC; default(DateTime) kabul edilmez.");
                Describe(schema, "petId", "Opsiyonel. Doluysa müşteriye ait olmalıdır (iş kuralı).");
                Describe(schema, "appointmentId", "Opsiyonel; doluysa klinik/müşteri/hayvan ile tutarlı olmalıdır.");
                Describe(schema, "examinationId", "Opsiyonel; doluysa klinik/müşteri/hayvan ile tutarlı olmalıdır.");
                Describe(schema, "notes", "Opsiyonel; en fazla 4000 karakter.");
                return;
            }

            if (context.Type == typeof(UpdatePaymentBody))
            {
                MarkRequired(schema, "clinicId", "clientId", "amount", "currency", "method", "paidAtUtc");
                SetNullable(schema, "currency", false);
                SetNullable(schema, "id", true);
                SetNullable(schema, "petId", true);
                SetNullable(schema, "appointmentId", true);
                SetNullable(schema, "examinationId", true);
                SetNullable(schema, "notes", true);
                Describe(schema, "id", "Opsiyonel; doluysa route id ile aynı olmalıdır.");
                Describe(schema, "currency", "Zorunlu. ISO 4217 alpha-3.");
                Describe(schema, "method", "Zorunlu. 0=Cash, 1=Card, 2=Transfer.");
                Describe(schema, "notes", "Opsiyonel.");
                return;
            }

            if (context.Type == typeof(PaymentDetailDto))
            {
                MarkRequired(schema,
                    "id", "tenantId", "clinicId", "clientId", "clientName", "petName", "amount", "currency", "method", "paidAtUtc");
                SetNullable(schema, "currency", false);
                SetNullable(schema, "petId", true);
                SetNullable(schema, "appointmentId", true);
                SetNullable(schema, "examinationId", true);
                SetNullable(schema, "notes", true);
                Describe(schema, "petId", "Hayvan yoksa null; petName o zaman genelde boş string.");
                Describe(schema, "appointmentId", "Bağlı randevu yoksa null.");
                Describe(schema, "examinationId", "Bağlı muayene yoksa null.");
                Describe(schema, "notes", "Yoksa veya boşsa null dönebilir.");
                return;
            }

            if (context.Type == typeof(PaymentListItemDto))
            {
                MarkRequired(schema,
                    "id", "clinicId", "clientId", "clientName", "petName", "amount", "currency", "method", "paidAtUtc");
                SetNullable(schema, "currency", false);
                SetNullable(schema, "petId", true);
                Describe(schema, "petId", "Hayvan yoksa null.");
            }
        }

        private static void Describe(OpenApiSchema schema, string propertyName, string description)
        {
            if (schema.Properties.TryGetValue(propertyName, out var p))
            {
                p.Description = description;
            }
        }

        private static void MarkRequired(OpenApiSchema schema, params string[] names)
        {
            schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (schema.Properties.ContainsKey(name))
                {
                    schema.Required.Add(name);
                }
            }
        }

        private static void SetNullable(OpenApiSchema schema, string propertyName, bool isNullable)
        {
            if (!schema.Properties.TryGetValue(propertyName, out var propertySchema))
            {
                return;
            }

            propertySchema.Nullable = isNullable;
        }
    }

    /// <summary>Randevu yanıtlarında <c>type</c> ile <c>status</c> anlamını Swagger’da netleştirir.</summary>
    private sealed class AppointmentsContractSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type == typeof(CreateAppointmentCommand))
            {
                MarkRequired(schema, "petId", "scheduledAtUtc", "appointmentType");
                DescribeAppointmentTypeOnCreateUpdate(schema);
                Describe(schema, "status",
                    "Opsiyonel. Verilmezse Scheduled (0). Tamamlanmış/iptal oluşturma için 1 veya 2; bu durumda zaman penceresi ve çakışma kontrolleri uygulanmaz.");
                return;
            }

            if (context.Type == typeof(UpdateAppointmentCommand))
            {
                MarkRequired(schema, "id", "petId", "scheduledAtUtc", "appointmentType", "status");
                DescribeAppointmentTypeOnCreateUpdate(schema);
                DescribeStatusOnWrite(schema);
                return;
            }

            if (context.Type == typeof(AppointmentListItemDto))
            {
                MarkRequired(schema,
                    "id", "tenantId", "clinicId", "clinicName", "petId", "petName",
                    "speciesId", "speciesName", "appointmentType", "clientId", "clientName", "scheduledAtUtc", "status");
                Describe(schema, "speciesName", "Evcil hayvan türü görünen adı (global Species kataloğu).");
                DescribeAppointmentType(schema);
                DescribeStatus(schema);
                return;
            }

            if (context.Type == typeof(AppointmentDetailDto))
            {
                MarkRequired(schema,
                    "id", "tenantId", "clinicId", "clinicName", "petId", "petName",
                    "clientName", "clientId", "speciesId", "speciesName", "appointmentType", "scheduledAtUtc", "status");
                Describe(schema, "speciesName", "Evcil hayvan türü görünen adı (global Species kataloğu).");
                DescribeAppointmentType(schema);
                DescribeStatus(schema);
            }
        }

        private static void MarkRequired(OpenApiSchema schema, params string[] names)
        {
            schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (schema.Properties.ContainsKey(name))
                {
                    schema.Required.Add(name);
                }
            }
        }

        private static void Describe(OpenApiSchema schema, string propertyName, string description)
        {
            if (schema.Properties.TryGetValue(propertyName, out var p))
            {
                p.Description = description;
            }
        }

        private static void DescribeStatus(OpenApiSchema schema)
        {
            Describe(schema, "status",
                "Randevu durumu (JSON’da sayı): 0=Scheduled, 1=Completed, 2=Cancelled.");
        }

        private static void DescribeAppointmentType(OpenApiSchema schema)
        {
            Describe(schema, "appointmentType",
                "Randevu / ziyaret türü (JSON’da sayı): 0=Examination, 1=Vaccination, 2=Checkup, 3=Surgery, 4=Grooming, 5=Consultation, 6=Other.");
        }

        private static void DescribeAppointmentTypeOnCreateUpdate(OpenApiSchema schema)
        {
            DescribeAppointmentType(schema);
        }

        private static void DescribeStatusOnWrite(OpenApiSchema schema)
        {
            Describe(schema, "status",
                "Randevu durumu (JSON sayı): 0=Scheduled, 1=Completed, 2=Cancelled. Scheduled dışındaki değerlerde çakışma kontrolü yapılmaz.");
        }
    }

    private sealed class ExaminationsContractSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type == typeof(CreateExaminationBody))
            {
                Describe(schema, "visitReason",
                    "Canonical: başvuru nedeni. Doluysa bu değer kullanılır; complaint yok sayılır.");
                Describe(schema, "complaint",
                    "DEPRECATED — geriye dönük uyumluluk. Yalnızca visitReason boş/whitespace ise kullanılır. Kaldırma hedefi: docs/BACKEND-CONTRACT-STANDARD.md §11.");
                return;
            }

            if (context.Type == typeof(UpdateExaminationBody))
            {
                Describe(schema, "visitReason",
                    "Canonical: başvuru nedeni. Doluysa bu değer kullanılır; complaint yok sayılır.");
                Describe(schema, "complaint",
                    "DEPRECATED — geriye dönük uyumluluk. Yalnızca visitReason boş/whitespace ise kullanılır.");
                return;
            }

            if (context.Type == typeof(ExaminationDetailDto))
            {
                Describe(schema, "visitReason", "Başvuru nedeni (tek kanonik alan; complaint yanıtta yok).");
                return;
            }

            if (context.Type == typeof(ExaminationListItemDto))
            {
                Describe(schema, "visitReason", "Başvuru nedeni.");
            }
        }

        private static void Describe(OpenApiSchema schema, string propertyName, string description)
        {
            if (schema.Properties.TryGetValue(propertyName, out var p))
            {
                p.Description = description;
            }
        }
    }

    private sealed class TreatmentsContractSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type == typeof(CreateTreatmentCommand))
            {
                MarkRequired(schema, "clinicId", "petId", "treatmentDateUtc", "title", "description");
                SetNullable(schema, "examinationId", true);
                SetNullable(schema, "notes", true);
                SetNullable(schema, "followUpDateUtc", true);
                return;
            }

            if (context.Type == typeof(UpdateTreatmentBody))
            {
                MarkRequired(schema, "clinicId", "petId", "treatmentDateUtc", "title", "description");
                SetNullable(schema, "id", true);
                SetNullable(schema, "examinationId", true);
                SetNullable(schema, "notes", true);
                SetNullable(schema, "followUpDateUtc", true);
                return;
            }

            if (context.Type == typeof(TreatmentDetailDto))
            {
                MarkRequired(schema,
                    "id", "tenantId", "clinicId", "petId", "petName", "clientId", "clientName",
                    "treatmentDateUtc", "title", "description", "createdAtUtc");
                SetNullable(schema, "examinationId", true);
                SetNullable(schema, "notes", true);
                SetNullable(schema, "followUpDateUtc", true);
                SetNullable(schema, "updatedAtUtc", true);
                return;
            }

            if (context.Type == typeof(TreatmentListItemDto))
            {
                MarkRequired(schema,
                    "id", "clinicId", "petId", "petName", "clientId", "clientName", "treatmentDateUtc", "title");
                SetNullable(schema, "examinationId", true);
                SetNullable(schema, "followUpDateUtc", true);
            }
        }

        private static void MarkRequired(OpenApiSchema schema, params string[] names)
        {
            schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (schema.Properties.ContainsKey(name))
                    schema.Required.Add(name);
            }
        }

        private static void SetNullable(OpenApiSchema schema, string propertyName, bool isNullable)
        {
            if (!schema.Properties.TryGetValue(propertyName, out var propertySchema))
                return;
            propertySchema.Nullable = isNullable;
        }
    }

    private sealed class PrescriptionsContractSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type == typeof(CreatePrescriptionCommand))
            {
                MarkRequired(schema, "clinicId", "petId", "prescribedAtUtc", "title", "content");
                SetNullable(schema, "examinationId", true);
                SetNullable(schema, "treatmentId", true);
                SetNullable(schema, "notes", true);
                SetNullable(schema, "followUpDateUtc", true);
                return;
            }

            if (context.Type == typeof(UpdatePrescriptionBody))
            {
                MarkRequired(schema, "clinicId", "petId", "prescribedAtUtc", "title", "content");
                SetNullable(schema, "id", true);
                SetNullable(schema, "examinationId", true);
                SetNullable(schema, "treatmentId", true);
                SetNullable(schema, "notes", true);
                SetNullable(schema, "followUpDateUtc", true);
                return;
            }

            if (context.Type == typeof(PrescriptionDetailDto))
            {
                MarkRequired(schema,
                    "id", "tenantId", "clinicId", "petId", "petName", "clientId", "clientName",
                    "prescribedAtUtc", "title", "content", "createdAtUtc");
                SetNullable(schema, "examinationId", true);
                SetNullable(schema, "treatmentId", true);
                SetNullable(schema, "notes", true);
                SetNullable(schema, "followUpDateUtc", true);
                SetNullable(schema, "updatedAtUtc", true);
                return;
            }

            if (context.Type == typeof(PrescriptionListItemDto))
            {
                MarkRequired(schema,
                    "id", "clinicId", "petId", "petName", "clientId", "clientName", "prescribedAtUtc", "title");
                SetNullable(schema, "examinationId", true);
                SetNullable(schema, "treatmentId", true);
                SetNullable(schema, "followUpDateUtc", true);
            }
        }

        private static void MarkRequired(OpenApiSchema schema, params string[] names)
        {
            schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (schema.Properties.ContainsKey(name))
                    schema.Required.Add(name);
            }
        }

        private static void SetNullable(OpenApiSchema schema, string propertyName, bool isNullable)
        {
            if (!schema.Properties.TryGetValue(propertyName, out var propertySchema))
                return;
            propertySchema.Nullable = isNullable;
        }
    }

    private sealed class LabResultsContractSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type == typeof(CreateLabResultCommand))
            {
                MarkRequired(schema, "clinicId", "petId", "resultDateUtc", "testName", "resultText");
                SetNullable(schema, "examinationId", true);
                SetNullable(schema, "interpretation", true);
                SetNullable(schema, "notes", true);
                return;
            }

            if (context.Type == typeof(UpdateLabResultBody))
            {
                MarkRequired(schema, "clinicId", "petId", "resultDateUtc", "testName", "resultText");
                SetNullable(schema, "id", true);
                SetNullable(schema, "examinationId", true);
                SetNullable(schema, "interpretation", true);
                SetNullable(schema, "notes", true);
                return;
            }

            if (context.Type == typeof(LabResultDetailDto))
            {
                MarkRequired(schema,
                    "id", "tenantId", "clinicId", "petId", "petName", "clientId", "clientName",
                    "resultDateUtc", "testName", "resultText", "createdAtUtc");
                SetNullable(schema, "examinationId", true);
                SetNullable(schema, "interpretation", true);
                SetNullable(schema, "notes", true);
                SetNullable(schema, "updatedAtUtc", true);
                return;
            }

            if (context.Type == typeof(LabResultListItemDto))
            {
                MarkRequired(schema,
                    "id", "clinicId", "petId", "petName", "clientId", "clientName", "resultDateUtc", "testName");
                SetNullable(schema, "examinationId", true);
            }
        }

        private static void MarkRequired(OpenApiSchema schema, params string[] names)
        {
            schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (schema.Properties.ContainsKey(name))
                    schema.Required.Add(name);
            }
        }

        private static void SetNullable(OpenApiSchema schema, string propertyName, bool isNullable)
        {
            if (!schema.Properties.TryGetValue(propertyName, out var propertySchema))
                return;
            propertySchema.Nullable = isNullable;
        }
    }

    private sealed class HospitalizationsContractSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type == typeof(CreateHospitalizationCommand))
            {
                MarkRequired(schema, "clinicId", "petId", "admittedAtUtc", "reason");
                SetNullable(schema, "examinationId", true);
                SetNullable(schema, "plannedDischargeAtUtc", true);
                SetNullable(schema, "notes", true);
                return;
            }

            if (context.Type == typeof(UpdateHospitalizationBody))
            {
                MarkRequired(schema, "clinicId", "petId", "admittedAtUtc", "reason");
                SetNullable(schema, "id", true);
                SetNullable(schema, "examinationId", true);
                SetNullable(schema, "plannedDischargeAtUtc", true);
                SetNullable(schema, "notes", true);
                return;
            }

            if (context.Type == typeof(DischargeHospitalizationBody))
            {
                MarkRequired(schema, "dischargedAtUtc");
                SetNullable(schema, "notes", true);
                return;
            }

            if (context.Type == typeof(HospitalizationDetailDto))
            {
                MarkRequired(schema,
                    "id", "tenantId", "clinicId", "petId", "petName", "clientId", "clientName",
                    "admittedAtUtc", "reason", "createdAtUtc", "isActive");
                SetNullable(schema, "examinationId", true);
                SetNullable(schema, "plannedDischargeAtUtc", true);
                SetNullable(schema, "dischargedAtUtc", true);
                SetNullable(schema, "notes", true);
                SetNullable(schema, "updatedAtUtc", true);
                return;
            }

            if (context.Type == typeof(HospitalizationListItemDto))
            {
                MarkRequired(schema,
                    "id", "clinicId", "petId", "petName", "clientId", "clientName",
                    "admittedAtUtc", "reason", "isActive");
                SetNullable(schema, "examinationId", true);
                SetNullable(schema, "plannedDischargeAtUtc", true);
                SetNullable(schema, "dischargedAtUtc", true);
            }
        }

        private static void MarkRequired(OpenApiSchema schema, params string[] names)
        {
            schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (schema.Properties.ContainsKey(name))
                    schema.Required.Add(name);
            }
        }

        private static void SetNullable(OpenApiSchema schema, string propertyName, bool isNullable)
        {
            if (!schema.Properties.TryGetValue(propertyName, out var propertySchema))
                return;
            propertySchema.Nullable = isNullable;
        }
    }

    private sealed class ClientsContractSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type == typeof(ClientRecentSummaryDto))
            {
                MarkRequired(schema, "clientId", "recentAppointments", "recentExaminations");
                return;
            }

            if (context.Type == typeof(ClientRecentAppointmentSummaryItemDto))
            {
                MarkRequired(schema, "id", "scheduledAtUtc", "petId", "petName", "status");
                SetNullable(schema, "notes", true);
                return;
            }

            if (context.Type == typeof(ClientRecentExaminationSummaryItemDto))
            {
                MarkRequired(schema, "id", "examinedAtUtc", "petId", "petName", "visitReason");
            }
        }

        private static void MarkRequired(OpenApiSchema schema, params string[] names)
        {
            schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (schema.Properties.ContainsKey(name))
                    schema.Required.Add(name);
            }
        }

        private static void SetNullable(OpenApiSchema schema, string propertyName, bool isNullable)
        {
            if (!schema.Properties.TryGetValue(propertyName, out var propertySchema))
                return;
            propertySchema.Nullable = isNullable;
        }
    }

    /// <summary>
    /// GET /examinations liste parametreleri için OpenAPI açıklamaları (Appointment detail gibi senaryolarda <c>appointmentId</c> sözleşmesi).
    /// </summary>
    private sealed class ExaminationsListOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var route = context.ApiDescription.ActionDescriptor.RouteValues;
            if (!route.TryGetValue("controller", out var controller)
                || !string.Equals(controller, "Examinations", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!route.TryGetValue("action", out var action)
                || !string.Equals(action, "GetList", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (operation.Parameters is null)
                return;

            static void SetDesc(OpenApiOperation op, string name, string description)
            {
                var p = op.Parameters!.FirstOrDefault(x =>
                    string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (p is null)
                    return;
                if (string.IsNullOrWhiteSpace(p.Description))
                    p.Description = description;
            }

            SetDesc(operation, "appointmentId",
                "Optional. Returns examinations where Examination.AppointmentId equals this value. Combined with clinicId, petId, date range, and search using AND.");
            SetDesc(operation, "clinicId",
                "Optional clinic filter. Must match JWT/header clinic context when both are set.");
            SetDesc(operation, "petId", "Optional pet filter.");
            SetDesc(operation, "dateFromUtc", "Optional lower bound (inclusive) on ExaminedAtUtc.");
            SetDesc(operation, "dateToUtc", "Optional upper bound (inclusive) on ExaminedAtUtc.");
            SetDesc(operation, "search",
                "Optional text search; merged with page.search when both are used (see PageRequestQuery.WithMergedSearch).");
        }
    }

    private sealed class AuthContractCleanupOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var routeValues = context.ApiDescription.ActionDescriptor.RouteValues;
            if (!routeValues.TryGetValue("controller", out var controller)
                || !string.Equals(controller, "Auth", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!routeValues.TryGetValue("action", out var action))
            {
                return;
            }

            if (string.Equals(action, "Logout", StringComparison.OrdinalIgnoreCase))
            {
                var schema = context.SchemaGenerator.GenerateSchema(typeof(AuthLogoutBodyDto), context.SchemaRepository);
                operation.RequestBody = new OpenApiRequestBody
                {
                    Required = false,
                    Content =
                    {
                        ["application/json"] = new OpenApiMediaType { Schema = schema }
                    }
                };

                operation.Responses["200"] = new OpenApiResponse
                {
                    Description = "OK",
                    Content =
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = context.SchemaGenerator.GenerateSchema(typeof(AuthActionResultDto), context.SchemaRepository)
                        }
                    }
                };

                operation.Responses["401"] = new OpenApiResponse
                {
                    Description = "Unauthorized",
                    Content =
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = context.SchemaGenerator.GenerateSchema(typeof(ProblemDetails), context.SchemaRepository)
                        }
                    }
                };

                return;
            }

            if (string.Equals(action, "LogoutAll", StringComparison.OrdinalIgnoreCase))
            {
                operation.RequestBody = null;

                operation.Responses["200"] = new OpenApiResponse
                {
                    Description = "OK",
                    Content =
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = context.SchemaGenerator.GenerateSchema(typeof(AuthActionResultDto), context.SchemaRepository)
                        }
                    }
                };

                operation.Responses["401"] = new OpenApiResponse
                {
                    Description = "Unauthorized",
                    Content =
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = context.SchemaGenerator.GenerateSchema(typeof(ProblemDetails), context.SchemaRepository)
                        }
                    }
                };
            }
        }
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
