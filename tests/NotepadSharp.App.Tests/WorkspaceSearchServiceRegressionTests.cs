using System.IO;
using System.Text.RegularExpressions;
using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public sealed class WorkspaceSearchServiceRegressionTests : IDisposable
{
    private readonly string _rootPath;

    public WorkspaceSearchServiceRegressionTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), $"notepadsharp-search-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootPath);
    }

    [Fact]
    public async Task SearchAsync_FindsMatchesAndSkipsIgnoredDirectories()
    {
        WriteFile("src/Program.cs", "needle\nsecond needle\n");
        WriteFile("node_modules/ignored.js", "needle\n");

        var service = CreateService();
        var regex = new Regex("needle", RegexOptions.Compiled);

        var result = await service.SearchAsync(_rootPath, regex, maxMatches: 20);

        Assert.Equal(2, result.Matches.Count);
        Assert.Equal(1, result.MatchedFileCount);
        Assert.All(result.Matches, match => Assert.Equal("src/Program.cs", match.RelativePath.Replace('\\', '/')));
    }

    [Fact]
    public async Task SearchAsync_StopsAtConfiguredMatchLimit()
    {
        WriteFile("src/Large.cs", "hit\nhit\nhit\nhit\n");

        var service = CreateService();
        var regex = new Regex("hit", RegexOptions.Compiled);

        var result = await service.SearchAsync(_rootPath, regex, maxMatches: 3);

        Assert.Equal(3, result.Matches.Count);
        Assert.True(result.HitMatchLimit);
    }

    [Fact]
    public async Task SearchAsync_ReturnsStableOrderingAcrossFiles()
    {
        WriteFile("src/Zeta.cs", "hit\n");
        WriteFile("src/Alpha.cs", "hit\nhit\n");

        var service = CreateService(maxParallelism: 2);
        var regex = new Regex("hit", RegexOptions.Compiled);

        var result = await service.SearchAsync(_rootPath, regex, maxMatches: 10);

        Assert.Collection(
            result.Matches,
            item =>
            {
                Assert.Equal("src/Alpha.cs", item.RelativePath.Replace('\\', '/'));
                Assert.Equal(1, item.Line);
            },
            item =>
            {
                Assert.Equal("src/Alpha.cs", item.RelativePath.Replace('\\', '/'));
                Assert.Equal(2, item.Line);
            },
            item =>
            {
                Assert.Equal("src/Zeta.cs", item.RelativePath.Replace('\\', '/'));
                Assert.Equal(1, item.Line);
            });
    }

    [Fact]
    public async Task SearchAsync_SkipsLargeAndBinaryFiles()
    {
        WriteFile("src/Small.cs", "needle\n");
        WriteFile("src/Large.cs", $"{new string('a', 40)}needle\n");
        WriteBinaryFile("src/Binary.txt", new byte[] { (byte)'n', 0, (byte)'e', (byte)'e', (byte)'d', (byte)'l', (byte)'e' });

        var service = CreateService(maxFileSizeBytes: 20);
        var regex = new Regex("needle", RegexOptions.Compiled);

        var result = await service.SearchAsync(_rootPath, regex, maxMatches: 10);

        var match = Assert.Single(result.Matches);
        Assert.Equal("src/Small.cs", match.RelativePath.Replace('\\', '/'));
        Assert.Equal(3, result.ScannedFileCount);
        Assert.Equal(2, result.SkippedFileCount);
        Assert.Equal(1, result.MatchedFileCount);
    }

    [Fact]
    public async Task ReplaceAsync_ReportsChangedFilesAndReplacementCount()
    {
        var first = WriteFile("src/One.cs", "foo foo\n");
        var second = WriteFile("src/Two.cs", "no change\n");

        var service = CreateService();
        var regex = new Regex("foo", RegexOptions.Compiled);

        var result = await service.ReplaceAsync(_rootPath, regex, "bar");

        Assert.Equal(1, result.FilesChanged);
        Assert.Equal(2, result.ReplacementCount);
        Assert.Contains(first, result.ChangedFiles);
        Assert.DoesNotContain(second, result.ChangedFiles);
        Assert.Equal("bar bar\n", File.ReadAllText(first));
    }

    [Fact]
    public async Task ReplaceAsync_SkipsLargeAndBinaryFiles()
    {
        var small = WriteFile("src/One.cs", "foo foo\n");
        var largeContents = $"{new string('x', 40)}foo foo\n";
        var large = WriteFile("src/Large.cs", largeContents);
        var binary = WriteBinaryFile("src/Binary.txt", new byte[] { (byte)'f', 0, (byte)'o', (byte)'o' });

        var service = CreateService(maxFileSizeBytes: 20);
        var regex = new Regex("foo", RegexOptions.Compiled);

        var result = await service.ReplaceAsync(_rootPath, regex, "bar");

        Assert.Equal(1, result.FilesChanged);
        Assert.Equal(2, result.ReplacementCount);
        Assert.Equal(2, result.SkippedFileCount);
        Assert.Contains(small, result.ChangedFiles);
        Assert.Equal("bar bar\n", File.ReadAllText(small));
        Assert.Equal(largeContents, File.ReadAllText(large));
        Assert.Equal(new byte[] { (byte)'f', 0, (byte)'o', (byte)'o' }, File.ReadAllBytes(binary));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
        catch
        {
            // Ignore temp cleanup failures.
        }
    }

    private WorkspaceSearchService CreateService(int? maxParallelism = null, long maxFileSizeBytes = 2_000_000)
        => new(
            new[] { ".git", "node_modules", "bin", "obj" },
            new[] { ".cs", ".txt", ".md", ".json" },
            maxParallelism: maxParallelism,
            maxFileSizeBytes: maxFileSizeBytes);

    private string WriteFile(string relativePath, string contents)
    {
        var fullPath = Path.Combine(_rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
        return fullPath;
    }

    private string WriteBinaryFile(string relativePath, byte[] contents)
    {
        var fullPath = Path.Combine(_rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, contents);
        return fullPath;
    }
}
