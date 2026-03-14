using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class AiProviderConfigLogicRegressionTests
{
    [Fact]
    public void Normalize_FillsDefaultsForBlankValues()
    {
        var settings = AiProviderConfigLogic.Normalize(new AiProviderSettings(
            Enabled: true,
            Endpoint: " ",
            Model: "",
            ApiKeyEnvironmentVariable: null!));

        Assert.Equal(AiProviderConfigLogic.DefaultEndpoint, settings.Endpoint);
        Assert.Equal(AiProviderConfigLogic.DefaultModel, settings.Model);
        Assert.Equal(AiProviderConfigLogic.DefaultApiKeyEnvironmentVariable, settings.ApiKeyEnvironmentVariable);
    }

    [Fact]
    public void GetAvailabilityState_ReportsMissingApiKeyWhenEnabled()
    {
        var state = AiProviderConfigLogic.GetAvailabilityState(
            new AiProviderSettings(true, AiProviderConfigLogic.DefaultEndpoint, "gpt-5-mini", "OPENAI_API_KEY"),
            _ => null);

        Assert.Equal(AiProviderAvailability.MissingApiKey, state.Availability);
        Assert.Contains("OPENAI_API_KEY", state.Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SmartActionKind.Ask, true)]
    [InlineData(SmartActionKind.Explain, true)]
    [InlineData(SmartActionKind.Refactor, true)]
    [InlineData(SmartActionKind.FixDiagnostics, true)]
    [InlineData(SmartActionKind.GenerateTests, true)]
    [InlineData(SmartActionKind.CommitMessage, true)]
    public void CanUseProviderForAction_SupportsAllSmartActionKinds(SmartActionKind action, bool expected)
    {
        Assert.Equal(expected, AiProviderConfigLogic.CanUseProviderForAction(action));
    }
}
