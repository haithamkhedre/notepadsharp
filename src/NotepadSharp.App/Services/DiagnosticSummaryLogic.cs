using System;
using System.Collections.Generic;
using System.Linq;

namespace NotepadSharp.App.Services;

public static class DiagnosticSummaryLogic
{
    public static string FormatStatusText(IEnumerable<string> severities)
    {
        var (errors, warnings, total) = Count(severities);
        if (total == 0)
        {
            return "Diagnostics: 0";
        }

        return $"Diagnostics: {total} ({errors}E/{warnings}W)";
    }

    public static string FormatSummaryText(IEnumerable<string> severities)
    {
        var (errors, warnings, total) = Count(severities);
        if (total == 0)
        {
            return "No diagnostics.";
        }

        if (errors == 0)
        {
            return warnings == 1 ? "1 warning" : $"{warnings} warnings";
        }

        if (warnings == 0)
        {
            return errors == 1 ? "1 error" : $"{errors} errors";
        }

        return $"{errors} error{(errors == 1 ? string.Empty : "s")}, {warnings} warning{(warnings == 1 ? string.Empty : "s")}";
    }

    private static (int Errors, int Warnings, int Total) Count(IEnumerable<string> severities)
    {
        ArgumentNullException.ThrowIfNull(severities);

        var severityList = severities
            .Where(severity => !string.IsNullOrWhiteSpace(severity))
            .ToList();
        var errors = severityList.Count(severity => string.Equals(severity, "Error", StringComparison.OrdinalIgnoreCase));
        var warnings = severityList.Count(severity => string.Equals(severity, "Warning", StringComparison.OrdinalIgnoreCase));
        return (errors, warnings, severityList.Count);
    }
}
