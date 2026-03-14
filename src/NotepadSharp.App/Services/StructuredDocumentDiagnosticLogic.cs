using System;
using System.Xml;
using YamlDotNet.Core;

namespace NotepadSharp.App.Services;

public sealed record StructuredDocumentDiagnosticInfo(int Line, int Column, string Message);

public static class StructuredDocumentDiagnosticLogic
{
    public static StructuredDocumentDiagnosticInfo FromXmlException(XmlException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new StructuredDocumentDiagnosticInfo(
            Math.Max(1, exception.LineNumber),
            Math.Max(1, exception.LinePosition),
            exception.Message);
    }

    public static StructuredDocumentDiagnosticInfo FromYamlException(YamlException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new StructuredDocumentDiagnosticInfo(
            checked((int)Math.Max(1L, exception.Start.Line)),
            checked((int)Math.Max(1L, exception.Start.Column)),
            exception.Message);
    }
}
