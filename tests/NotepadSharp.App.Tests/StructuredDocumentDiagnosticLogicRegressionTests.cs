using System.Xml;
using System.Xml.Linq;
using NotepadSharp.App.Services;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace NotepadSharp.App.Tests;

public class StructuredDocumentDiagnosticLogicRegressionTests
{
    [Fact]
    public void FromXmlException_UsesParserLineAndColumn()
    {
        var exception = Assert.Throws<XmlException>(() => XDocument.Parse(
            """
            <root>
              <child>
            </root>
            """));

        var diagnostic = StructuredDocumentDiagnosticLogic.FromXmlException(exception);

        Assert.Equal(exception.LineNumber, diagnostic.Line);
        Assert.Equal(exception.LinePosition, diagnostic.Column);
        Assert.Equal(exception.Message, diagnostic.Message);
    }

    [Fact]
    public void FromYamlException_UsesParserLineAndColumn()
    {
        var deserializer = new DeserializerBuilder().Build();
        var exception = Assert.IsAssignableFrom<YamlException>(Record.Exception(() => deserializer.Deserialize<object?>(
            """
            root:
              child:
             broken: true
            """)));

        var diagnostic = StructuredDocumentDiagnosticLogic.FromYamlException(exception);

        Assert.Equal(checked((int)Math.Max(1L, exception.Start.Line)), diagnostic.Line);
        Assert.Equal(checked((int)Math.Max(1L, exception.Start.Column)), diagnostic.Column);
        Assert.Equal(exception.Message, diagnostic.Message);
    }
}
