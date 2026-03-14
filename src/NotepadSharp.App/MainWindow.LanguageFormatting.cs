using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using NotepadSharp.App.Services;
using NotepadSharp.App.ViewModels;
using NotepadSharp.Core;
using YamlDotNet.Serialization;

namespace NotepadSharp.App;

public partial class MainWindow
{
    private void OnReplaceClick(object? sender, RoutedEventArgs e)
        => ReplaceOnce();

    private void OnReplaceAllClick(object? sender, RoutedEventArgs e)
        => ReplaceAll();

    private void OnSetLineEndingLfClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDocument is null)
        {
            return;
        }

        _viewModel.SelectedDocument.PreferredLineEnding = LineEnding.Lf;
    }

    private void OnSetLineEndingCrLfClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDocument is null)
        {
            return;
        }

        _viewModel.SelectedDocument.PreferredLineEnding = LineEnding.CrLf;
    }

    private void OnSetLineEndingCrClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDocument is null)
        {
            return;
        }

        _viewModel.SelectedDocument.PreferredLineEnding = LineEnding.Cr;
    }

    private void OnSetEncodingUtf8Click(object? sender, RoutedEventArgs e)
        => SetEncoding(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), hasBom: false);

    private void OnSetEncodingUtf8BomClick(object? sender, RoutedEventArgs e)
        => SetEncoding(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), hasBom: true);

    private void OnSetEncodingUtf16LeClick(object? sender, RoutedEventArgs e)
        => SetEncoding(Encoding.Unicode, hasBom: true);

    private void OnSetEncodingUtf16BeClick(object? sender, RoutedEventArgs e)
        => SetEncoding(Encoding.BigEndianUnicode, hasBom: true);

    private void OnLanguageModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel || _isUpdatingLanguageModeSelector || LanguageModeComboBox is null)
        {
            return;
        }

        if (LanguageModeComboBox.SelectedItem is not ComboBoxItem selectedItem)
        {
            return;
        }

        var selectedMode = selectedItem.Content?.ToString();
        if (string.IsNullOrWhiteSpace(selectedMode))
        {
            return;
        }

        SetLanguageMode(selectedMode);
    }

    private void OnSetLanguageAutoClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("Auto");

    private void OnSetLanguagePlainTextClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("Plain Text");

    private void OnSetLanguageCSharpClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("C#");

    private void OnSetLanguageJsonClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("JSON");

    private void OnSetLanguageXmlClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("XML");

    private void OnSetLanguageYamlClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("YAML");

    private void OnSetLanguageMarkdownClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("Markdown");

    private void OnSetLanguageJavaScriptClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("JavaScript");

    private void OnSetLanguageTypeScriptClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("TypeScript");

    private void OnSetLanguagePythonClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("Python");

    private void OnSetLanguageSqlClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("SQL");

    private void OnSetLanguageHtmlClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("HTML");

    private void OnSetLanguageCssClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("CSS");

    private void SetLanguageMode(string mode)
    {
        _languageMode = NormalizeLanguageMode(mode);
        UpdateLanguageModeSelector();
        UpdateSettingsControls();
        ApplyLanguageStyling();
    }

    private string NormalizeLanguageMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "Auto";
        }

        var candidate = mode.Trim();
        return LanguageModes.Any(item => string.Equals(item, candidate, StringComparison.Ordinal))
            ? candidate
            : "Auto";
    }

    private void UpdateLanguageModeSelector()
    {
        if (LanguageModeComboBox is null)
        {
            return;
        }

        _isUpdatingLanguageModeSelector = true;
        try
        {
            var selected = NormalizeLanguageMode(_languageMode);
            var comboItem = LanguageModeComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Content?.ToString(), selected, StringComparison.Ordinal));

            LanguageModeComboBox.SelectedItem = comboItem ?? LanguageModeComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }
        finally
        {
            _isUpdatingLanguageModeSelector = false;
        }
    }

    private void ApplyLanguageStyling()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var timer = Stopwatch.StartNew();
        try
        {
            var sourceText = string.IsNullOrWhiteSpace(EditorTextBox.Text)
                ? _viewModel.SelectedDocument?.Text
                : EditorTextBox.Text;

            var primaryLineCount = Math.Max(1, EditorTextBox.Document?.LineCount ?? 1);
            var primaryIsLarge = (sourceText?.Length ?? 0) > LargeFileTextLengthThreshold || primaryLineCount > LargeFileMiniMapLineThreshold;
            var resolved = primaryIsLarge
                ? "Plain Text"
                : (_languageMode == "Auto"
                    ? DetectLanguage(_viewModel.SelectedDocument?.FilePath, sourceText)
                    : _languageMode);

            _viewModel.StatusLanguage = resolved;
            EditorTextBox.SyntaxHighlighting = ResolveHighlightingDefinition(resolved);

            if (SplitEditorTextBox is not null)
            {
                var splitText = string.IsNullOrWhiteSpace(SplitEditorTextBox.Text)
                    ? _splitDocument?.Text
                    : SplitEditorTextBox.Text;

                var splitLineCount = Math.Max(1, SplitEditorTextBox.Document?.LineCount ?? 1);
                var splitIsLarge = (splitText?.Length ?? 0) > LargeFileTextLengthThreshold || splitLineCount > LargeFileMiniMapLineThreshold;
                var splitResolved = splitIsLarge
                    ? "Plain Text"
                    : (_languageMode == "Auto"
                        ? DetectLanguage(_splitDocument?.FilePath, splitText)
                        : _languageMode);

                SplitEditorTextBox.SyntaxHighlighting = ResolveHighlightingDefinition(splitResolved);
            }

            UpdateDiagnostics();
        }
        finally
        {
            timer.Stop();
            RecordStylePerf(timer.Elapsed.TotalMilliseconds);
        }
    }

    private static string DetectLanguage(string? filePath, string? text)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".cs" or ".csx" => "C#",
                ".json" => "JSON",
                ".xml" or ".xaml" => "XML",
                ".yaml" or ".yml" => "YAML",
                ".md" or ".markdown" => "Markdown",
                ".js" or ".mjs" or ".cjs" => "JavaScript",
                ".ts" or ".tsx" => "TypeScript",
                ".py" => "Python",
                ".sql" => "SQL",
                ".html" or ".htm" => "HTML",
                ".css" => "CSS",
                _ => "Plain Text",
            };
        }

        // Heuristic for untitled buffers so starter/sample code gets a sensible language.
        var sample = text ?? string.Empty;
        if (sample.Contains("using System;", StringComparison.Ordinal)
            || sample.Contains("public static void Main", StringComparison.Ordinal))
        {
            return "C#";
        }

        return "Plain Text";
    }

    private IHighlightingDefinition? ResolveHighlightingDefinition(string language)
    {
        var extension = language switch
        {
            "C#" => ".cs",
            "JSON" => ".json",
            "XML" => ".xml",
            "YAML" => ".yml",
            "Markdown" => ".md",
            "JavaScript" => ".js",
            "TypeScript" => ".js",
            "Python" => ".py",
            "SQL" => ".sql",
            "HTML" => ".html",
            "CSS" => ".css",
            _ => string.Empty,
        };

        if (string.IsNullOrEmpty(extension))
        {
            return null;
        }

        var definition = HighlightingManager.Instance.GetDefinitionByExtension(extension);
        if (definition is null)
        {
            return null;
        }

        if (_themedHighlightDefinitions.Add(definition.Name))
        {
            ApplyVsCodeDarkPalette(definition);
        }

        return definition;
    }

    private void ApplyVsCodeDarkPalette(IHighlightingDefinition definition)
    {
        foreach (var color in definition.NamedHighlightingColors)
        {
            var hex = ResolveThemeTokenColor(color.Name);
            color.Foreground = new SimpleHighlightingBrush(Color.Parse(hex));
            color.Background = null;
        }
    }

    private string ResolveThemeTokenColor(string? tokenName)
    {
        var n = (tokenName ?? string.Empty).ToLowerInvariant();
        var comment = _themeMode switch
        {
            "Light" => "#008000",
            "Monokai" => "#75715E",
            "One Dark" => "#5C6370",
            _ => "#6A9955",
        };
        var str = _themeMode switch
        {
            "Light" => "#A31515",
            "Monokai" => "#E6DB74",
            "One Dark" => "#98C379",
            _ => "#CE9178",
        };
        var num = _themeMode switch
        {
            "Light" => "#098658",
            "Monokai" => "#AE81FF",
            "One Dark" => "#D19A66",
            _ => "#B5CEA8",
        };
        var keyword = _themeMode switch
        {
            "Light" => "#0000FF",
            "Monokai" => "#F92672",
            "One Dark" => "#C678DD",
            _ => "#C586C0",
        };
        var type = _themeMode switch
        {
            "Light" => "#267F99",
            "Monokai" => "#66D9EF",
            "One Dark" => "#E5C07B",
            _ => "#4EC9B0",
        };
        var method = _themeMode switch
        {
            "Light" => "#795E26",
            "Monokai" => "#A6E22E",
            "One Dark" => "#61AFEF",
            _ => "#DCDCAA",
        };
        var prop = _themeMode switch
        {
            "Light" => "#001080",
            "Monokai" => "#66D9EF",
            "One Dark" => "#56B6C2",
            _ => "#9CDCFE",
        };
        var constant = _themeMode switch
        {
            "Light" => "#0000FF",
            "Monokai" => "#FD971F",
            "One Dark" => "#E06C75",
            _ => "#569CD6",
        };
        var normal = _themeMode switch
        {
            "Light" => "#1F2933",
            "Monokai" => "#F8F8F2",
            "One Dark" => "#ABB2BF",
            _ => "#D4D4D4",
        };

        if (n.Contains("comment"))
        {
            return comment;
        }

        if (n.Contains("string") || n.Contains("char") || n.Contains("regex"))
        {
            return str;
        }

        if (n.Contains("number") || n.Contains("digit") || n.Contains("hex"))
        {
            return num;
        }

        if (n.Contains("preprocessor") || n.Contains("directive") || n.Contains("keyword"))
        {
            return keyword;
        }

        if (n.Contains("class")
            || n.Contains("interface")
            || n.Contains("enum")
            || n.Contains("struct")
            || n.Contains("type"))
        {
            return type;
        }

        if (n.Contains("method") || n.Contains("function") || n.Contains("call"))
        {
            return method;
        }

        if (n.Contains("tag")
            || n.Contains("attribute")
            || n.Contains("property")
            || n.Contains("field")
            || n.Contains("xml")
            || n.Contains("html")
            || n.Contains("css"))
        {
            return prop;
        }

        if (n.Contains("constant") || n.Contains("literal") || n.Contains("bool"))
        {
            return constant;
        }

        if (n.Contains("operator") || n.Contains("punctuation"))
        {
            return normal;
        }

        return normal;
    }

    private void TryAutoFormatCurrentDocument()
    {
        if (_isApplyingAutoFormat || EditorTextBox is null || _viewModel.SelectedDocument is null)
        {
            return;
        }

        var language = _viewModel.StatusLanguage;
        if (!IsAutoFormatLanguage(language))
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 200_000)
        {
            return;
        }

        var formatted = TryFormatByLanguage(text, language);
        if (formatted is null || string.Equals(formatted, text, StringComparison.Ordinal))
        {
            return;
        }

        var caret = EditorTextBox.CaretOffset;
        var selectionStart = EditorTextBox.SelectionStart;
        var selectionLength = EditorTextBox.SelectionLength;

        _isApplyingAutoFormat = true;
        try
        {
            EditorTextBox.Text = formatted;
            EditorTextBox.CaretOffset = Math.Min(caret, formatted.Length);
            EditorTextBox.SelectionStart = Math.Min(selectionStart, formatted.Length);
            EditorTextBox.SelectionLength = Math.Min(selectionLength, Math.Max(0, formatted.Length - EditorTextBox.SelectionStart));
        }
        finally
        {
            _isApplyingAutoFormat = false;
        }
    }

    private static bool IsAutoFormatLanguage(string language)
        => language is "C#" or "JSON" or "XML" or "HTML" or "YAML" or "JavaScript" or "TypeScript" or "CSS" or "SQL";

    private void OnFormatDocumentClick(object? sender, RoutedEventArgs e)
        => FormatDocument(selectionOnly: false);

    private void OnFormatSelectionClick(object? sender, RoutedEventArgs e)
        => FormatDocument(selectionOnly: true);

    private void FormatDocument(bool selectionOnly)
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var start = 0;
        var end = text.Length;
        if (selectionOnly)
        {
            start = Math.Min(EditorTextBox.SelectionStart, GetSelectionEnd());
            end = Math.Max(EditorTextBox.SelectionStart, GetSelectionEnd());
            if (end <= start)
            {
                return;
            }
        }

        var prefix = text.Substring(0, start);
        var segment = text.Substring(start, end - start);
        var suffix = text.Substring(end);

        var language = _viewModel.StatusLanguage;
        var formatted = TryFormatByLanguage(segment, language);
        if (formatted is null || string.Equals(formatted, segment, StringComparison.Ordinal))
        {
            return;
        }

        EditorTextBox.Text = prefix + formatted + suffix;
        if (selectionOnly)
        {
            SetSelection(start, start + formatted.Length);
        }
        else
        {
            EditorTextBox.CaretOffset = 0;
            EditorTextBox.ScrollToHome();
        }
    }

    private static string? TryFormatByLanguage(string text, string language)
    {
        try
        {
            return language switch
            {
                "JSON" => JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(text), new JsonSerializerOptions { WriteIndented = true }),
                "XML" or "HTML" => XDocument.Parse(text).ToString(),
                "YAML" => FormatYaml(text),
                "C#" => FormatCSharp(text),
                "JavaScript" => TryFormatWithPrettier(text, "babel") ?? FormatBraceLanguage(text),
                "TypeScript" => TryFormatWithPrettier(text, "typescript") ?? FormatBraceLanguage(text),
                "CSS" => TryFormatWithPrettier(text, "css") ?? FormatBraceLanguage(text),
                "SQL" => TryFormatWithSqlFormatter(text) ?? FormatBraceLanguage(text),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string FormatCSharp(string text)
    {
        var tree = CSharpSyntaxTree.ParseText(text);
        var root = tree.GetRoot();
        using var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var formattedRoot = Formatter.Format(root, workspace);
        return formattedRoot.ToFullString();
    }

    private static string FormatYaml(string text)
    {
        var deserializer = new DeserializerBuilder().Build();
        var value = deserializer.Deserialize<object?>(text);
        var serializer = new SerializerBuilder()
            .DisableAliases()
            .WithIndentedSequences()
            .Build();
        return serializer.Serialize(value).TrimEnd('\r', '\n');
    }

    private static string? TryFormatWithPrettier(string text, string parser)
    {
        var args = $"--parser {parser}";
        return TryFormatWithCommand("prettier", args, text);
    }

    private static string? TryFormatWithSqlFormatter(string text)
    {
        return TryFormatWithCommand("sql-formatter", "--language sql", text);
    }

    private static SyntaxToolRunResult TryRunSyntaxTool(string tool, string argumentTemplate, string content, string extension)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"notepadsharp_{Guid.NewGuid():N}{extension}");
        try
        {
            File.WriteAllText(tempPath, content);
            var args = argumentTemplate.Replace("{file}", tempPath, StringComparison.Ordinal);
            var result = RunProcess(tool, args, Path.GetDirectoryName(tempPath) ?? Path.GetTempPath(), timeoutMs: 5000);
            return new SyntaxToolRunResult(result.exitCode == 0, result.exitCode, result.stdout, result.stderr);
        }
        catch (Exception ex)
        {
            return new SyntaxToolRunResult(false, -1, string.Empty, ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Ignore.
            }
        }
    }

    private (int exitCode, string stdout, string stderr) RunGit(
        string repoRoot,
        string arguments,
        int timeoutMs = 4000,
        CancellationToken cancellationToken = default)
        => RunProcess("git", $"-C \"{repoRoot}\" {arguments}", repoRoot, timeoutMs, cancellationToken);

    private (int exitCode, string stdout, string stderr) RunGit(
        string repoRoot,
        IReadOnlyList<string> arguments,
        int timeoutMs = 4000,
        CancellationToken cancellationToken = default)
    {
        var argumentList = new List<string>(arguments.Count + 2)
        {
            "-C",
            repoRoot,
        };

        foreach (var argument in arguments)
        {
            argumentList.Add(argument);
        }

        return RunProcess("git", repoRoot, argumentList, timeoutMs, cancellationToken);
    }

    private static (int exitCode, string stdout, string stderr) RunProcess(
        string fileName,
        string arguments,
        string workingDirectory,
        int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            if (cancellationToken.IsCancellationRequested)
            {
                return (-1, string.Empty, "Process canceled.");
            }

            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore race with process shutdown.
                }
            });

            if (!process.Start())
            {
                return (-1, string.Empty, $"Failed to start process: {fileName}");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore.
                }

                return (-1, string.Empty, "Process timed out.");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return (-1, string.Empty, "Process canceled.");
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            return (process.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }

    private static (int exitCode, string stdout, string stderr) RunProcess(
        string fileName,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return (-1, string.Empty, "Process canceled.");
            }

            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore race with process shutdown.
                }
            });

            if (!process.Start())
            {
                return (-1, string.Empty, $"Failed to start process: {fileName}");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore.
                }

                return (-1, string.Empty, "Process timed out.");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return (-1, string.Empty, "Process canceled.");
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            return (process.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }

    private static string? TryFormatWithCommand(string command, string arguments, string input, int timeoutMs = 5000)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            if (!process.Start())
            {
                return null;
            }

            process.StandardInput.Write(input);
            process.StandardInput.Close();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore.
                }

                return null;
            }

            var output = outputTask.GetAwaiter().GetResult();
            var _ = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            return output.TrimEnd('\r', '\n');
        }
        catch
        {
            return null;
        }
    }

    private static string FormatBraceLanguage(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder(text.Length + 64);
        var indent = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].Trim();
            if (raw.Length == 0)
            {
                if (i < lines.Length - 1)
                {
                    sb.Append('\n');
                }
                continue;
            }

            if (raw.StartsWith("}", StringComparison.Ordinal) || raw.StartsWith("]", StringComparison.Ordinal))
            {
                indent = Math.Max(0, indent - 1);
            }

            sb.Append(new string(' ', indent * 4));
            sb.Append(raw);
            if (i < lines.Length - 1)
            {
                sb.Append('\n');
            }

            if (raw.EndsWith("{", StringComparison.Ordinal) || raw.EndsWith("[", StringComparison.Ordinal))
            {
                indent++;
            }
        }

        return sb.ToString();
    }

    private void SetEncoding(Encoding encoding, bool hasBom)
    {
        if (_viewModel.SelectedDocument is null)
        {
            return;
        }

        _viewModel.SelectedDocument.Encoding = encoding;
        _viewModel.SelectedDocument.HasBom = hasBom;
    }

    private void ReplaceOnce()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var query = _viewModel.FindText;
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        var selection = EditorTextBox.SelectedText ?? string.Empty;
        var replacementRaw = _viewModel.ReplaceText ?? string.Empty;

        var selectionEnd = GetSelectionEnd();
        if (selectionEnd > EditorTextBox.SelectionStart && selection.Length > 0)
        {
            var start = EditorTextBox.SelectionStart;
            var end = selectionEnd;
            var text = EditorTextBox.Text ?? string.Empty;

            var replacement = GetReplacementIfSelectionMatches(text, query, replacementRaw, start, end - start);
            if (replacement is not null)
            {
                var newText = text.Substring(0, start) + replacement + text.Substring(end);
                EditorTextBox.Text = newText;
                SelectMatch(start, replacement.Length);
                FindNext(forward: true);
                return;
            }
        }

        FindNext(forward: true);
    }

    private string? GetReplacementIfSelectionMatches(string text, string query, string replacementRaw, int selectionStart, int selectionLength)
    {
        if (_viewModel.UseRegex)
        {
            var regex = TryCreateRegex(query, _viewModel.MatchCase, _viewModel.WholeWord);
            if (regex is null)
            {
                return null;
            }

            var m = regex.Match(text, Math.Clamp(selectionStart, 0, text.Length));
            if (!m.Success || m.Index != selectionStart || m.Length != selectionLength)
            {
                return null;
            }

            try
            {
                return m.Result(replacementRaw);
            }
            catch
            {
                return replacementRaw;
            }
        }

        if (selectionStart < 0 || selectionStart + selectionLength > text.Length)
        {
            return null;
        }

        var selected = text.Substring(selectionStart, selectionLength);
        var comparison = _viewModel.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (!string.Equals(selected, query, comparison))
        {
            return null;
        }

        if (_viewModel.WholeWord && !IsWholeWordAt(text, selectionStart, selectionLength))
        {
            return null;
        }

        return replacementRaw;
    }

    private void ReplaceAll()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var query = _viewModel.FindText;
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        var replacement = _viewModel.ReplaceText ?? string.Empty;
        var text = EditorTextBox.Text ?? string.Empty;

        var (rangeStart, rangeEnd) = GetSearchRange(text);
        if (rangeStart >= rangeEnd)
        {
            return;
        }

        var prefix = rangeStart == 0 ? string.Empty : text.Substring(0, rangeStart);
        var segment = text.Substring(rangeStart, rangeEnd - rangeStart);
        var suffix = rangeEnd >= text.Length ? string.Empty : text.Substring(rangeEnd);

        if (_viewModel.UseRegex)
        {
            var regex = TryCreateRegex(query, _viewModel.MatchCase, _viewModel.WholeWord);
            if (regex is null)
            {
                return;
            }

            try
            {
                segment = regex.Replace(segment, replacement);
                EditorTextBox.Text = prefix + segment + suffix;
            }
            catch
            {
                // Ignore invalid replacement.
            }

            return;
        }

        var comparison = _viewModel.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        var idx = 0;
        var result = new System.Text.StringBuilder(segment.Length);
        while (idx < segment.Length)
        {
            var next = segment.IndexOf(query, idx, comparison);
            if (next < 0)
            {
                result.Append(segment, idx, segment.Length - idx);
                break;
            }

            if (_viewModel.WholeWord && !IsWholeWordAt(segment, next, query.Length))
            {
                result.Append(segment, idx, (next - idx) + 1);
                idx = next + 1;
                continue;
            }

            result.Append(segment, idx, next - idx);
            result.Append(replacement);
            idx = next + query.Length;
        }

        EditorTextBox.Text = prefix + result + suffix;
    }
}
