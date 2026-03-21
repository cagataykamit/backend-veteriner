using Backend.Veteriner.Api.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.AddBackendAppConfiguration();
builder.AddBackendSerilog();
builder.AddBackendServices();

var app = builder.Build();

await app.ConfigureBackendAsync();

app.Run();

// Integration testler için WebApplicationFactory<Program> tarafından erişilebilmesi amacıyla
// public partial Program tanımı eklenir. Uygulama davranışını değiştirmez.
public partial class Program { }
