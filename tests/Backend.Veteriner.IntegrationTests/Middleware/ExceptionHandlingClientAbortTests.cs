using System.Collections.Concurrent;
using AwesomeAssertions;
using Backend.Veteriner.Api.Middleware;
using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.IntegrationTests.Middleware;

/// <summary>
/// İstemci isteği bıraktığında (sayfa değişimi) çıkan iptal hata sayılmaz: 499, hata raporlayıcıya gitmez, framework ERR yazmaz.
/// SqlClient iptali <c>SqlException</c> olarak da fırlatabildiği için tür ne olursa olsun <c>RequestAborted</c> belirleyicidir.
/// </summary>
public sealed class ExceptionHandlingClientAbortTests
{
    [Fact]
    public async Task Aborted_Request_Should_Be_499_Without_Error_Report_Or_Framework_Error_Log()
    {
        await using var host = await PipelineHost.StartAsync(ctx =>
        {
            ctx.Abort();
            throw new InvalidOperationException("İşlem kullanıcı tarafından iptal edildi.");
        });

        var status = await host.SendAndObserveStatusAsync();

        status.Should().Be(499);
        host.Reporter.Reports.Should().BeEmpty();
        host.Logs.ErrorCategories.Should().NotContain(c => c.Contains("ExceptionHandler"));
    }

    [Fact]
    public async Task Unexpected_Exception_Should_Still_Be_500_With_Error_Report_And_Log()
    {
        await using var host = await PipelineHost.StartAsync(_ => throw new InvalidOperationException("Gerçek hata"));

        var status = await host.SendAndObserveStatusAsync();

        status.Should().Be(StatusCodes.Status500InternalServerError);
        host.Reporter.Reports.Should().ContainSingle().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        host.Logs.ErrorCategories.Should().Contain(c => c.Contains("ExceptionHandler"));
    }

    private sealed class PipelineHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly TaskCompletionSource<int> _status = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private PipelineHost(WebApplication app, FakeErrorReporter reporter, CapturingLoggerProvider logs)
        {
            _app = app;
            Reporter = reporter;
            Logs = logs;
        }

        public FakeErrorReporter Reporter { get; }
        public CapturingLoggerProvider Logs { get; }

        public static async Task<PipelineHost> StartAsync(RequestDelegate terminal)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
            builder.WebHost.UseTestServer();
            var reporter = new FakeErrorReporter();
            var logs = new CapturingLoggerProvider();
            builder.Services.AddSingleton<IErrorReporter>(reporter);
            builder.Logging.ClearProviders().AddProvider(logs);

            var app = builder.Build();
            var host = new PipelineHost(app, reporter, logs);
            app.Use(async (ctx, next) =>
            {
                try { await next(ctx); }
                finally { host._status.TrySetResult(ctx.Response.StatusCode); }
            });
            app.UseGlobalExceptionHandler(app.Environment);
            app.Run(terminal);
            await app.StartAsync();
            return host;
        }

        public async Task<int> SendAndObserveStatusAsync()
        {
            using var client = _app.GetTestClient();
            try
            {
                await client.GetAsync("/");
            }
            catch (Exception)
            {
                // İstemcinin bıraktığı istekte TestServer istemci tarafında hata fırlatır; sunucu tarafı durumu aşağıda okunur.
            }

            return await _status.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }

    private sealed class FakeErrorReporter : IErrorReporter
    {
        public ConcurrentQueue<ErrorReportContext> Reports { get; } = new();

        public void Report(Exception exception, ErrorReportContext context) => Reports.Enqueue(context);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> ErrorCategories { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, ErrorCategories);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<string> errors) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Error)
                    errors.Enqueue(category);
            }
        }
    }
}
