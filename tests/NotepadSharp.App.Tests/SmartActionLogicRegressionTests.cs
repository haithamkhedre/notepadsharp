using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class SmartActionLogicRegressionTests
{
    [Fact]
    public void NormalizeSidebarSectionAlias_MapsLegacyAssistantName()
    {
        var normalized = SmartActionLogic.NormalizeSidebarSectionAlias("AI Assistant");

        Assert.Equal(SmartActionLogic.SidebarSectionName, normalized);
    }

    [Fact]
    public void ResolveAskKind_UsesExplicitQuickCommandBeforePromptKeywords()
    {
        var kind = SmartActionLogic.ResolveAskKind(
            prompt: "please fix these diagnostics",
            quickCommand: "/commit",
            cleanedPrompt: out var cleanedPrompt);

        Assert.Equal(SmartActionKind.CommitMessage, kind);
        Assert.Equal("please fix these diagnostics", cleanedPrompt);
    }

    [Fact]
    public void ResolveAskKind_UsesSlashCommandFromPromptAndStripsIt()
    {
        var kind = SmartActionLogic.ResolveAskKind(
            prompt: "/tests add regression coverage",
            quickCommand: SmartActionLogic.DefaultQuickCommand,
            cleanedPrompt: out var cleanedPrompt);

        Assert.Equal(SmartActionKind.GenerateTests, kind);
        Assert.Equal("add regression coverage", cleanedPrompt);
    }

    [Theory]
    [InlineData("", SmartActionKind.Explain)]
    [InlineData("improve readability", SmartActionKind.Refactor)]
    [InlineData("fix failing errors", SmartActionKind.FixDiagnostics)]
    [InlineData("write tests for parsing", SmartActionKind.GenerateTests)]
    [InlineData("draft commit message", SmartActionKind.CommitMessage)]
    public void ResolvePromptKind_UsesDeterministicKeywordRouting(string prompt, SmartActionKind expected)
    {
        var kind = SmartActionLogic.ResolvePromptKind(prompt);

        Assert.Equal(expected, kind);
    }
}
