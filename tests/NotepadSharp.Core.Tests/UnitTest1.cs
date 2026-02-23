using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace NotepadSharp.Core.Tests;

public class TextDocumentFileServiceTests
{
    [Fact]
    public async Task LoadAsync_Utf8Bom_PreservesEncodingAndBom()
    {
        var service = new TextDocumentFileService();

        var text = "hello";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        await using var input = new MemoryStream(bytes);

        var doc = await service.LoadAsync(input, filePath: "test.txt");

        Assert.Equal(text, doc.Text);
        Assert.True(doc.HasBom);
        Assert.Equal(Encoding.UTF8.WebName, doc.Encoding.WebName);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public async Task SaveAsync_WritesBomWhenConfigured()
    {
        var service = new TextDocumentFileService();
        var doc = TextDocument.CreateNew();
        doc.Encoding = Encoding.UTF8;
        doc.HasBom = true;
        doc.Text = "abc";

        await using var output = new MemoryStream();
        await service.SaveAsync(doc, output);

        var saved = output.ToArray();
        Assert.True(saved.Length >= 3);
        Assert.Equal(0xEF, saved[0]);
        Assert.Equal(0xBB, saved[1]);
        Assert.Equal(0xBF, saved[2]);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public async Task LoadAsync_DetectsCrLf_AndNormalizesToLfInMemory()
    {
        var service = new TextDocumentFileService();

        var raw = "a\r\nb\r\n";
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(raw));

        var doc = await service.LoadAsync(input);

        Assert.Equal(LineEnding.CrLf, doc.PreferredLineEnding);
        Assert.Equal("a\nb\n", doc.Text);
    }

    [Fact]
    public async Task SaveAsync_WritesConfiguredLineEndings()
    {
        var service = new TextDocumentFileService();
        var doc = TextDocument.CreateNew();
        doc.Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        doc.HasBom = false;
        doc.PreferredLineEnding = LineEnding.CrLf;
        doc.Text = "a\nb\n";

        await using var output = new MemoryStream();
        await service.SaveAsync(doc, output);

        var saved = Encoding.UTF8.GetString(output.ToArray());
        Assert.Equal("a\r\nb\r\n", saved);
    }

    [Fact]
    public async Task SaveAsync_Utf16Le_WritesBom()
    {
        var service = new TextDocumentFileService();
        var doc = TextDocument.CreateNew();
        doc.Encoding = Encoding.Unicode;
        doc.HasBom = true;
        doc.PreferredLineEnding = LineEnding.Lf;
        doc.Text = "hi\n";

        await using var output = new MemoryStream();
        await service.SaveAsync(doc, output);

        var bytes = output.ToArray();
        Assert.True(bytes.Length >= 2);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xFE, bytes[1]);
    }
}

public class TextDocumentTests
{
    [Fact]
    public void TextChange_MarksDirty()
    {
        var doc = TextDocument.CreateNew();
        Assert.False(doc.IsDirty);

        doc.Text = "x";
        Assert.True(doc.IsDirty);
        Assert.EndsWith("*", doc.DisplayName);
    }
}
