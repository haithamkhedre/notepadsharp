using System.Net.Http.Headers;
using System.Text.Json;
using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class OpenAiResponsesClientRegressionTests
{
    [Fact]
    public void BuildRequestJson_UsesResponsesInputMessageShape()
    {
        var json = OpenAiResponsesClient.BuildRequestJson(
            new AiProviderSettings(true, AiProviderConfigLogic.DefaultEndpoint, "gpt-5-mini", "OPENAI_API_KEY"),
            "You are helpful.",
            "Explain this code.");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("gpt-5-mini", root.GetProperty("model").GetString());
        var input = root.GetProperty("input");
        Assert.Equal(2, input.GetArrayLength());
        Assert.Equal("developer", input[0].GetProperty("role").GetString());
        Assert.Equal("input_text", input[0].GetProperty("content")[0].GetProperty("type").GetString());
    }

    [Fact]
    public void BuildRequest_AddsBearerAuthorization()
    {
        using var request = OpenAiResponsesClient.BuildRequest(
            new AiProviderSettings(true, AiProviderConfigLogic.DefaultEndpoint, "gpt-5-mini", "OPENAI_API_KEY"),
            "system",
            "user",
            "secret");

        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("secret", request.Headers.Authorization?.Parameter);
        Assert.Equal("application/json", request.Content?.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void ParseResponse_ExtractsOutputTextBlocks()
    {
        const string payload = """
            {
              "id": "resp_123",
              "model": "gpt-5-mini",
              "output": [
                {
                  "content": [
                    { "type": "output_text", "text": "First line." },
                    { "type": "output_text", "text": "Second line." }
                  ]
                }
              ]
            }
            """;

        var response = OpenAiResponsesClient.ParseResponse(payload);

        Assert.Equal("resp_123", response.ResponseId);
        Assert.Equal("gpt-5-mini", response.Model);
        Assert.Equal("First line.\nSecond line.", response.OutputText);
    }

    [Fact]
    public void TryExtractError_ReturnsApiErrorMessage()
    {
        const string payload = """
            {
              "error": {
                "message": "Invalid API key."
              }
            }
            """;

        var message = OpenAiResponsesClient.TryExtractError(payload);

        Assert.Equal("Invalid API key.", message);
    }
}
