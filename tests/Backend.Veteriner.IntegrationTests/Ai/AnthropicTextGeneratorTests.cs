using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Backend.Veteriner.Application.Ai;
using Backend.Veteriner.Infrastructure.Ai.Anthropic;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Ai;

/// <summary>Anthropic Messages API istemcisi: sahte HTTP sunucusuyla istek biçimi, yanıt ve hata eşlemesi (gerçek API çağrısı yok).</summary>
public sealed class AnthropicTextGeneratorTests
{
    private const string Key = "test-anahtar";
    private readonly FakeAnthropic _server = new();

    private AnthropicTextGenerator Generator(string? apiKey = Key)
        => new(
            new StubHttpClientFactory(_server),
            Options.Create(new AnthropicOptions { ApiKey = apiKey, ApiBaseUrl = "https://anthropic.test/" }),
            NullLogger<AnthropicTextGenerator>.Instance);

    private static AiGenerationRequest Request() => new(
        "claude-haiku-5-5",
        "sistem istemi",
        """{"records":[]}""",
        new JsonObject { ["type"] = "object", ["additionalProperties"] = false },
        1_500);

    [Fact]
    public async Task Sends_Messages_Request_With_Json_Schema_Output_And_Reads_Text_And_Usage()
    {
        _server.Reply(HttpStatusCode.OK, """
            {"content":[{"type":"thinking","thinking":""},{"type":"text","text":"{\"sections\":"},{"type":"text","text":"[]}"}],
             "stop_reason":"end_turn",
             "usage":{"input_tokens":1000,"cache_creation_input_tokens":20,"cache_read_input_tokens":5,"output_tokens":150}}
            """);

        var result = await Generator().GenerateJsonAsync(Request(), CancellationToken.None);

        result.Status.Should().Be(AiGenerationStatus.Completed);
        result.Text.Should().Be("""{"sections":[]}""");
        (result.InputTokens, result.OutputTokens).Should().Be((1_025, 150));

        var sent = _server.Requests.Should().ContainSingle().Which;
        sent.Uri.Should().Be("https://anthropic.test/v1/messages");
        sent.Headers["x-api-key"].Should().Be(Key);
        sent.Headers["anthropic-version"].Should().Be("2026-10-21");
        var body = JsonNode.Parse(sent.Body)!;
        body["model"]!.GetValue<string>().Should().Be("claude-haiku-5-5");
        body["max_tokens"]!.GetValue<int>().Should().Be(1_500);
        body["system"]!.GetValue<string>().Should().Be("sistem istemi");
        body["messages"]![0]!["role"]!.GetValue<string>().Should().Be("user");
        body["messages"]![0]!["content"]!.GetValue<string>().Should().Be("""{"records":[]}""");
        body["output_config"]!["effort"]!.GetValue<string>().Should().Be("low");
        body["output_config"]!["format"]!["type"]!.GetValue<string>().Should().Be("json_schema");
        body["output_config"]!["format"]!["schema"]!["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
        body["thinking"].Should().BeNull();
        body["temperature"].Should().BeNull();
    }

    [Theory]
    [InlineData("refusal", AiGenerationStatus.Refused)]
    [InlineData("max_tokens", AiGenerationStatus.Truncated)]
    public async Task Maps_Stop_Reasons_Without_Text_But_Keeps_Usage(string stopReason, AiGenerationStatus expected)
    {
        _server.Reply(HttpStatusCode.OK,
            $$$"""{"content":[{"type":"text","text":"{\"sec"}],"stop_reason":"{{{stopReason}}}","usage":{"input_tokens":800,"output_tokens":1500}}""");

        var result = await Generator().GenerateJsonAsync(Request(), CancellationToken.None);

        result.Status.Should().Be(expected);
        result.Text.Should().BeNull();
        (result.InputTokens, result.OutputTokens).Should().Be((800, 1_500));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, AiFailureKind.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, AiFailureKind.Unavailable)]
    [InlineData((HttpStatusCode)529, AiFailureKind.Unavailable)]
    [InlineData(HttpStatusCode.Unauthorized, AiFailureKind.Rejected)]
    [InlineData(HttpStatusCode.BadRequest, AiFailureKind.Rejected)]
    [InlineData(HttpStatusCode.PaymentRequired, AiFailureKind.Rejected)]
    public async Task Maps_Error_Statuses(HttpStatusCode status, AiFailureKind expected)
    {
        _server.Reply(status, """{"type":"error","error":{"type":"some_error","message":"x"}}""");

        var result = await Generator().GenerateJsonAsync(Request(), CancellationToken.None);

        result.Status.Should().Be(AiGenerationStatus.Failed);
        result.FailureKind.Should().Be(expected);
        (result.InputTokens, result.OutputTokens).Should().Be((0, 0));
    }

    [Fact]
    public async Task Network_Error_Is_Transient()
    {
        _server.Throw = true;

        var result = await Generator().GenerateJsonAsync(Request(), CancellationToken.None);

        result.Status.Should().Be(AiGenerationStatus.Failed);
        result.FailureKind.Should().Be(AiFailureKind.Unavailable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Without_Key_Is_Not_Configured_And_Makes_No_Call(string? key)
    {
        var generator = Generator(key);

        generator.IsConfigured.Should().BeFalse();
        (await generator.GenerateJsonAsync(Request(), CancellationToken.None)).Status.Should().Be(AiGenerationStatus.Failed);
        _server.Requests.Should().BeEmpty();
    }

    private sealed record CapturedRequest(string Uri, Dictionary<string, string> Headers, string Body);

    private sealed class FakeAnthropic : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _body = "{}";

        public bool Throw { get; set; }

        public List<CapturedRequest> Requests { get; } = [];

        public void Reply(HttpStatusCode status, string body) => (_status, _body) = (status, body);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Throw)
                throw new HttpRequestException("bağlantı yok");

            Requests.Add(new CapturedRequest(
                request.RequestUri!.ToString(),
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase),
                await request.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
