using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NotepadSharp.Core;

public sealed class TextDocumentFileService
{
    public async Task<TextDocument> LoadAsync(Stream input, string? filePath = null, CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var (text, encoding, hasBom) = await ReadAllTextAsync(input, cancellationToken).ConfigureAwait(false);

        var detectedLineEnding = DetectLineEnding(text);
        var normalizedText = NormalizeToLf(text);

        var document = TextDocument.CreateNew();
        document.LoadFrom(normalizedText, encoding, hasBom, filePath, detectedLineEnding);
        return document;
    }

    public async Task SaveAsync(TextDocument document, Stream output, CancellationToken cancellationToken = default)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        var encoding = document.Encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        if (document.HasBom)
        {
            var preamble = encoding.GetPreamble();
            if (preamble.Length > 0)
            {
                await output.WriteAsync(preamble, cancellationToken).ConfigureAwait(false);
            }
        }

        var text = document.Text ?? string.Empty;
        text = NormalizeToLf(text);
        text = ApplyLineEnding(text, document.PreferredLineEnding);

        var bytes = encoding.GetBytes(text);
        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);

        document.MarkSaved();
    }

    private static async Task<(string text, Encoding encoding, bool hasBom)> ReadAllTextAsync(Stream input, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        var bytes = buffer.ToArray();
        if (bytes.Length == 0)
        {
            return (string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), false);
        }

        if (HasPrefix(bytes, 0xEF, 0xBB, 0xBF))
        {
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), Encoding.UTF8, true);
        }

        if (HasPrefix(bytes, 0xFF, 0xFE, 0x00, 0x00))
        {
            return (Encoding.UTF32.GetString(bytes, 4, bytes.Length - 4), Encoding.UTF32, true);
        }

        if (HasPrefix(bytes, 0x00, 0x00, 0xFE, 0xFF))
        {
            // UTF-32 BE isn't built-in as Encoding.UTF32; use a named encoding.
            var utf32be = Encoding.GetEncoding("utf-32BE");
            return (utf32be.GetString(bytes, 4, bytes.Length - 4), utf32be, true);
        }

        if (HasPrefix(bytes, 0xFF, 0xFE))
        {
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode, true);
        }

        if (HasPrefix(bytes, 0xFE, 0xFF))
        {
            return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), Encoding.BigEndianUnicode, true);
        }

        // Default: UTF-8 without BOM.
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
        return (utf8NoBom.GetString(bytes), utf8NoBom, false);
    }

    private static bool HasPrefix(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (bytes[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeToLf(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Replace CRLF first, then remaining CR.
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static string ApplyLineEnding(string lfText, LineEnding lineEnding)
    {
        if (string.IsNullOrEmpty(lfText))
        {
            return string.Empty;
        }

        return lineEnding switch
        {
            LineEnding.Lf => lfText,
            LineEnding.CrLf => lfText.Replace("\n", "\r\n"),
            LineEnding.Cr => lfText.Replace("\n", "\r"),
            _ => lfText,
        };
    }

    private static LineEnding DetectLineEnding(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return LineEnding.Lf;
        }

        var crlf = 0;
        var lf = 0;
        var cr = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                    continue;
                }

                cr++;
                continue;
            }

            if (c == '\n')
            {
                lf++;
            }
        }

        if (crlf >= lf && crlf >= cr && crlf > 0)
        {
            return LineEnding.CrLf;
        }

        if (cr >= lf && cr > 0)
        {
            return LineEnding.Cr;
        }

        return LineEnding.Lf;
    }
}
