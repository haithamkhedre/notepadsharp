using System;
using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class SyntaxToolDiagnosticLogicRegressionTests
{
    [Fact]
    public void ParsePython_ExtractsLineColumnAndMessage()
    {
        var output = string.Join(
            "\n",
            "  File \"/tmp/notepadsharp_sample.py\", line 3",
            "    return (",
            "           ^",
            "SyntaxError: '(' was never closed");

        var diagnostic = Assert.Single(SyntaxToolDiagnosticLogic.ParsePython(output));

        Assert.Equal("Error", diagnostic.Severity);
        Assert.Equal(3, diagnostic.Line);
        Assert.Equal(12, diagnostic.Column);
        Assert.Equal("SyntaxError: '(' was never closed", diagnostic.Message);
    }

    [Fact]
    public void ParsePython_ExtractsInlineLineForIndentationErrors()
    {
        const string output = "Sorry: IndentationError: expected an indented block after function definition on line 1 (/tmp/notepadsharp_sample.py, line 2)";

        var diagnostic = Assert.Single(SyntaxToolDiagnosticLogic.ParsePython(output));

        Assert.Equal(2, diagnostic.Line);
        Assert.Equal(1, diagnostic.Column);
        Assert.Equal("IndentationError: expected an indented block after function definition on line 1 (/tmp/notepadsharp_sample.py, line 2)", diagnostic.Message);
    }

    [Fact]
    public void ParseJavaScript_ExtractsLineColumnAndMessage()
    {
        var output = string.Join(
            "\n",
            "/tmp/notepadsharp_sample.js:2",
            "const value =",
            "            ^",
            string.Empty,
            "SyntaxError: Unexpected end of input",
            "    at wrapSafe (node:internal/modules/cjs/loader:1620:18)");

        var diagnostic = Assert.Single(SyntaxToolDiagnosticLogic.ParseJavaScript(output));

        Assert.Equal("Error", diagnostic.Severity);
        Assert.Equal(2, diagnostic.Line);
        Assert.Equal(13, diagnostic.Column);
        Assert.Equal("SyntaxError: Unexpected end of input", diagnostic.Message);
    }

    [Fact]
    public void ParseTypeScript_ExtractsMultipleDiagnostics()
    {
        const string output = """
            /tmp/notepadsharp_sample.ts(3,10): error TS1005: ';' expected.
            /tmp/notepadsharp_sample.ts(5,1): warning TS6133: 'value' is declared but its value is never read.
            """;

        var diagnostics = SyntaxToolDiagnosticLogic.ParseTypeScript(output);

        Assert.Collection(
            diagnostics,
            item =>
            {
                Assert.Equal("Error", item.Severity);
                Assert.Equal(3, item.Line);
                Assert.Equal(10, item.Column);
                Assert.Equal("TS1005: ';' expected.", item.Message);
            },
            item =>
            {
                Assert.Equal("Warning", item.Severity);
                Assert.Equal(5, item.Line);
                Assert.Equal(1, item.Column);
                Assert.Equal("TS6133: 'value' is declared but its value is never read.", item.Message);
            });
    }

    [Fact]
    public void SummarizeOutput_UsesFirstNonEmptyLineAcrossStreams()
    {
        var summary = SyntaxToolDiagnosticLogic.SummarizeOutput(
            $"{Environment.NewLine}error TS1005: ';' expected.",
            $"{Environment.NewLine}Process failed.");

        Assert.Equal("error TS1005: ';' expected.", summary);
    }
}
