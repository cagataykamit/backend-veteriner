using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: Xunit.TestFramework("Backend.IntegrationTests.Infrastructure.IntegrationTestFramework", "Backend.Veteriner.IntegrationTests")]

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Tüm testler bittiğinde, sonuç test host'a iletilmeden hemen önce bu koşunun LocalDB veritabanlarını siler
/// (<see cref="IntegrationTestDatabaseNames.CleanUp"/>). Dispose / ProcessExit kancaları test host süreci
/// sonuç bildirilir bildirilmez kesildiği için yarım kalıyor; bu yüzden temizlik assembly-bitti mesajında çalışır.
/// </summary>
public sealed class IntegrationTestFramework : XunitTestFramework
{
    public IntegrationTestFramework(IMessageSink messageSink) : base(messageSink) { }

    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName)
        => new Executor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);

    private sealed class Executor(
        AssemblyName assemblyName,
        ISourceInformationProvider sourceInformationProvider,
        IMessageSink diagnosticMessageSink)
        : XunitTestFrameworkExecutor(assemblyName, sourceInformationProvider, diagnosticMessageSink)
    {
        protected override async void RunTestCases(
            IEnumerable<IXunitTestCase> testCases,
            IMessageSink executionMessageSink,
            ITestFrameworkExecutionOptions executionOptions)
        {
            using var runner = new XunitTestAssemblyRunner(
                TestAssembly, testCases, DiagnosticMessageSink, new CleanUpOnAssemblyFinishedSink(executionMessageSink), executionOptions);
            await runner.RunAsync();
        }
    }

    private sealed class CleanUpOnAssemblyFinishedSink : LongLivedMarshalByRefObject, IMessageSink
    {
        private readonly IMessageSink _inner;

        public CleanUpOnAssemblyFinishedSink(IMessageSink inner) => _inner = inner;

        public bool OnMessage(IMessageSinkMessage message)
        {
            if (message is ITestAssemblyFinished)
                IntegrationTestDatabaseNames.CleanUp();

            return _inner.OnMessage(message);
        }
    }
}