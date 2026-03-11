using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NotepadSharp.Core;

namespace NotepadSharp.Perf;

public static class Program
{
    private const int DefaultLineCount = 20_000;
    private const int DefaultIterations = 14;
    private const int DefaultWarmups = 3;
    private const int QuickIterations = 6;
    private const int QuickWarmups = 1;

    public static async Task<int> Main(string[] args)
    {
        var options = ParseOptions(args);
        var fixture = await BuildFixtureAsync(options).ConfigureAwait(false);

        Console.WriteLine("NotepadSharp performance runner");
        Console.WriteLine($"Fixture lines: {fixture.LineCount:N0}");
        Console.WriteLine($"Fixture size:  {fixture.Text.Length / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine($"Mode:          {(options.Quick ? "quick" : "full")}");
        Console.WriteLine();

        var fileService = new TextDocumentFileService();
        var reusableDocument = TextDocument.CreateNew();
        var searchRangeEnd = fixture.Text.Length;

        var results = new List<BenchmarkResult>
        {
            await RunBenchmarkAsync(
                name: "Load document from file",
                iterations: options.Iterations,
                warmups: options.Warmups,
                action: async () =>
                {
                    await using var stream = OpenSequentialRead(fixture.FilePath);
                    var doc = await fileService.LoadAsync(stream, fixture.FilePath).ConfigureAwait(false);
                    return doc.Text.Length;
                }).ConfigureAwait(false),
            await RunBenchmarkAsync(
                name: "Reload existing document",
                iterations: options.Iterations,
                warmups: options.Warmups,
                action: async () =>
                {
                    await using var stream = OpenSequentialRead(fixture.FilePath);
                    await fileService.ReloadAsync(reusableDocument, stream, fixture.FilePath).ConfigureAwait(false);
                    return reusableDocument.Text.Length;
                }).ConfigureAwait(false),
            await RunBenchmarkAsync(
                name: "Count plain-text matches",
                iterations: options.Iterations,
                warmups: options.Warmups,
                action: () =>
                {
                    var count = TextSearchEngine.CountMatchesInRange(
                        fixture.Text,
                        query: "apple",
                        matchCase: false,
                        wholeWord: false,
                        useRegex: false,
                        rangeStart: 0,
                        rangeEnd: searchRangeEnd);
                    return Task.FromResult((long)count);
                }).ConfigureAwait(false),
            await RunBenchmarkAsync(
                name: "Count regex matches",
                iterations: options.Iterations,
                warmups: options.Warmups,
                action: () =>
                {
                    var count = TextSearchEngine.CountMatchesInRange(
                        fixture.Text,
                        query: "[A-Z][a-z]+Buzz",
                        matchCase: false,
                        wholeWord: false,
                        useRegex: true,
                        rangeStart: 0,
                        rangeEnd: searchRangeEnd);
                    return Task.FromResult((long)count);
                }).ConfigureAwait(false),
            await RunBenchmarkAsync(
                name: "Save normalized document",
                iterations: options.Iterations,
                warmups: options.Warmups,
                action: async () =>
                {
                    await using var stream = new MemoryStream(capacity: fixture.Text.Length);
                    reusableDocument.Text = fixture.Text;
                    await fileService.SaveAsync(reusableDocument, stream).ConfigureAwait(false);
                    return stream.Length;
                }).ConfigureAwait(false),
        };

        PrintSummary(results);
        Console.WriteLine();
        Console.WriteLine($"Fixture file: {fixture.FilePath}");
        Console.WriteLine("Tip: run with '--lines 50000' for bigger fixtures.");

        return 0;
    }

    private static FileStream OpenSequentialRead(string filePath)
        => new(filePath, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite,
            BufferSize = 128 * 1024,
            Options = FileOptions.SequentialScan,
        });

    private static BenchmarkOptions ParseOptions(string[] args)
    {
        var lines = DefaultLineCount;
        var quick = false;

        for (var i = 0; i < args.Length; i++)
        {
            var current = args[i];
            if (string.Equals(current, "--quick", StringComparison.OrdinalIgnoreCase))
            {
                quick = true;
                continue;
            }

            if (string.Equals(current, "--lines", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
                && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLines)
                && parsedLines > 200)
            {
                lines = parsedLines;
                i++;
            }
        }

        return quick
            ? new BenchmarkOptions(lines, true, QuickIterations, QuickWarmups)
            : new BenchmarkOptions(lines, false, DefaultIterations, DefaultWarmups);
    }

    private static async Task<PerfFixture> BuildFixtureAsync(BenchmarkOptions options)
    {
        var fixtureRoot = Path.Combine(Path.GetTempPath(), "notepadsharp-perf");
        Directory.CreateDirectory(fixtureRoot);

        var fileName = $"sample-{options.LineCount:N0}".Replace(",", string.Empty, StringComparison.Ordinal) + ".cs";
        var filePath = Path.Combine(fixtureRoot, fileName);
        var text = BuildLargeText(options.LineCount);
        await File.WriteAllTextAsync(filePath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).ConfigureAwait(false);

        return new PerfFixture(filePath, text, options.LineCount);
    }

    private static string BuildLargeText(int lineCount)
    {
        var builder = new StringBuilder(capacity: lineCount * 90);
        builder.AppendLine("// NotepadSharp perf fixture");
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine();
        builder.AppendLine("public static class PerfFixture");
        builder.AppendLine("{");
        builder.AppendLine("    public static IEnumerable<string> Run()");
        builder.AppendLine("    {");
        builder.AppendLine("        for (var i = 0; i < 1000; i++)");
        builder.AppendLine("        {");
        builder.AppendLine("            yield return $\"apple APPLE Apple banana BANAna DemoBuzz {i}\";");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");

        for (var i = 0; i < lineCount; i++)
        {
            builder.Append("var value");
            builder.Append(i);
            builder.Append(" = \"Line ");
            builder.Append(i);
            builder.Append(" -> apple APPLE DemoBuzz value");
            builder.Append(i % 41);
            builder.AppendLine("\";");
        }

        return builder.ToString();
    }

    private static async Task<BenchmarkResult> RunBenchmarkAsync(
        string name,
        int iterations,
        int warmups,
        Func<Task<long>> action)
    {
        if (iterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations));
        }

        if (warmups < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warmups));
        }

        for (var i = 0; i < warmups; i++)
        {
            _ = await action().ConfigureAwait(false);
        }

        ForceGc();

        var durationsMs = new double[iterations];
        long checksum = 0;

        for (var i = 0; i < iterations; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            checksum ^= await action().ConfigureAwait(false);
            stopwatch.Stop();
            durationsMs[i] = stopwatch.Elapsed.TotalMilliseconds;
        }

        var ordered = durationsMs.OrderBy(v => v).ToArray();
        var avg = durationsMs.Average();
        var min = ordered[0];
        var max = ordered[^1];
        var p95Index = (int)Math.Ceiling(ordered.Length * 0.95) - 1;
        p95Index = Math.Clamp(p95Index, 0, ordered.Length - 1);
        var p95 = ordered[p95Index];

        return new BenchmarkResult(name, iterations, avg, p95, min, max, checksum);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void PrintSummary(IReadOnlyList<BenchmarkResult> results)
    {
        const int nameWidth = 28;
        const int metricWidth = 10;

        Console.WriteLine("Summary (milliseconds):");
        Console.WriteLine(
            $"{Pad("Benchmark", nameWidth)} {Pad("Avg", metricWidth)} {Pad("P95", metricWidth)} {Pad("Min", metricWidth)} {Pad("Max", metricWidth)} Checksum");
        Console.WriteLine(new string('-', nameWidth + (metricWidth + 1) * 4 + 12));

        foreach (var result in results)
        {
            Console.WriteLine(
                $"{Pad(result.Name, nameWidth)} {Pad(result.AverageMs.ToString("F2", CultureInfo.InvariantCulture), metricWidth)} {Pad(result.P95Ms.ToString("F2", CultureInfo.InvariantCulture), metricWidth)} {Pad(result.MinMs.ToString("F2", CultureInfo.InvariantCulture), metricWidth)} {Pad(result.MaxMs.ToString("F2", CultureInfo.InvariantCulture), metricWidth)} {result.Checksum}");
        }
    }

    private static string Pad(string value, int width)
        => value.Length >= width ? value : value.PadRight(width, ' ');

    private sealed record BenchmarkOptions(int LineCount, bool Quick, int Iterations, int Warmups);

    private sealed record PerfFixture(string FilePath, string Text, int LineCount);

    private sealed record BenchmarkResult(
        string Name,
        int Iterations,
        double AverageMs,
        double P95Ms,
        double MinMs,
        double MaxMs,
        long Checksum);
}
