using System.Net;
using System.Net.Sockets;
using AwesomeAssertions;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Mailing;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Mailing;

/// <summary>
/// Canlı sunucu başka bir uygulamayla ortak ve onun adını taşıyor: posta başlıklarına (EHLO → Received, Message-Id) makine adı
/// değil <see cref="SmtpOptions.LocalDomain"/> yazılmalı.
/// </summary>
public sealed class MailKitEmailSenderLocalDomainTests
{
    [Fact]
    public async Task SendAsync_Should_Use_LocalDomain_In_Ehlo_And_MessageId()
    {
        using var server = new FakeSmtpServer();
        var sender = new MailKitEmailSender(Options.Create(new SmtpOptions
        {
            Host = "127.0.0.1",
            Port = server.Port,
            From = "gonderen@example.com",
            LocalDomain = "vetinity.com",
        }));

        await sender.SendAsync("alici@example.com", "Konu", "Gövde", CancellationToken.None);
        var lines = await server.ReceivedLinesAsync();

        lines.Should().Contain("EHLO vetinity.com");
        lines.Should().ContainSingle(l => l.StartsWith("Message-Id:", StringComparison.OrdinalIgnoreCase))
            .Which.Should().EndWith("@vetinity.com>");
        lines.Should().NotContain(l => l.Contains(Dns.GetHostName(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_Should_Reject_Empty_LocalDomain()
    {
        var result = new SmtpOptionsValidator().Validate(null, new SmtpOptions
        {
            Host = "smtp.example.com",
            Port = 587,
            From = "gonderen@example.com",
            LocalDomain = " ",
        });

        result.Failed.Should().BeTrue();
    }

    /// <summary>Tek bağlantı kabul eden, şifresiz, en yalın SMTP sunucusu; gelen tüm satırları toplar.</summary>
    private sealed class FakeSmtpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task<List<string>> _session;

        public FakeSmtpServer()
        {
            _listener.Start();
            _session = Task.Run(RunAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public async Task<List<string>> ReceivedLinesAsync() => await _session.WaitAsync(TimeSpan.FromSeconds(30));

        private async Task<List<string>> RunAsync()
        {
            var lines = new List<string>();
            using var client = await _listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream);
            await using var writer = new StreamWriter(stream) { NewLine = "\r\n", AutoFlush = true };

            await writer.WriteLineAsync("220 test ESMTP");
            var inData = false;
            while (await reader.ReadLineAsync() is { } line)
            {
                lines.Add(line);
                if (inData)
                {
                    if (line == ".")
                    {
                        inData = false;
                        await writer.WriteLineAsync("250 ok");
                    }
                    continue;
                }

                var command = line.ToUpperInvariant();
                if (command.StartsWith("DATA", StringComparison.Ordinal))
                {
                    inData = true;
                    await writer.WriteLineAsync("354 devam");
                }
                else if (command.StartsWith("QUIT", StringComparison.Ordinal))
                {
                    await writer.WriteLineAsync("221 hosca kal");
                    break;
                }
                else
                {
                    await writer.WriteLineAsync("250 ok");
                }
            }

            return lines;
        }

        public void Dispose() => _listener.Stop();
    }
}
