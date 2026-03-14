using System;

namespace NotepadSharp.App.Services;

public sealed record AiProviderSettings(
    bool Enabled,
    string Endpoint,
    string Model,
    string ApiKeyEnvironmentVariable);

public enum AiProviderAvailability
{
    Disabled,
    MissingApiKey,
    Ready,
}

public sealed record AiProviderAvailabilityState(
    AiProviderAvailability Availability,
    string Description);

public static class AiProviderConfigLogic
{
    public const string DefaultEndpoint = "https://api.openai.com/v1/responses";
    public const string DefaultModel = "gpt-5-mini";
    public const string DefaultApiKeyEnvironmentVariable = "OPENAI_API_KEY";

    public static AiProviderSettings Normalize(AiProviderSettings settings)
        => new(
            settings.Enabled,
            NormalizeEndpoint(settings.Endpoint),
            NormalizeModel(settings.Model),
            NormalizeApiKeyEnvironmentVariable(settings.ApiKeyEnvironmentVariable));

    public static string NormalizeEndpoint(string? endpoint)
        => string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint.Trim();

    public static string NormalizeModel(string? model)
        => string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();

    public static string NormalizeApiKeyEnvironmentVariable(string? name)
        => string.IsNullOrWhiteSpace(name) ? DefaultApiKeyEnvironmentVariable : name.Trim();

    public static AiProviderAvailabilityState GetAvailabilityState(
        AiProviderSettings settings,
        Func<string, string?>? environmentVariableReader = null)
    {
        var normalized = Normalize(settings);
        if (!normalized.Enabled)
        {
            return new AiProviderAvailabilityState(
                AiProviderAvailability.Disabled,
                "Deterministic local smart actions only. Enable OpenAI below to use a real provider-backed assistant.");
        }

        environmentVariableReader ??= Environment.GetEnvironmentVariable;
        var apiKey = environmentVariableReader(normalized.ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiProviderAvailabilityState(
                AiProviderAvailability.MissingApiKey,
                $"OpenAI is enabled, but env var '{normalized.ApiKeyEnvironmentVariable}' is not set.");
        }

        return new AiProviderAvailabilityState(
            AiProviderAvailability.Ready,
            $"OpenAI is enabled using model '{normalized.Model}'. Ask, Explain, Refactor, Fix, Tests, and Commit actions are provider-backed. Edit actions still require preview/apply.");
    }

    public static bool CanUseProviderForAction(SmartActionKind action)
        => action is SmartActionKind.Ask
            or SmartActionKind.Explain
            or SmartActionKind.Refactor
            or SmartActionKind.FixDiagnostics
            or SmartActionKind.GenerateTests
            or SmartActionKind.CommitMessage;
}
