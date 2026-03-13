using System;

namespace NotepadSharp.App;

public partial class MainWindow
{
    private double _perfOpenMs = -1;
    private double _perfStyleMs = -1;
    private double _perfFoldMs = -1;
    private double _perfDiffMs = -1;

    private void RecordOpenPerf(double elapsedMs)
        => RecordPerfMetric(ref _perfOpenMs, elapsedMs);

    private void RecordStylePerf(double elapsedMs)
        => RecordPerfMetric(ref _perfStyleMs, elapsedMs);

    private void RecordFoldingPerf(double elapsedMs)
        => RecordPerfMetric(ref _perfFoldMs, elapsedMs);

    private void RecordDiffPerf(double elapsedMs)
        => RecordPerfMetric(ref _perfDiffMs, elapsedMs);

    private void RecordPerfMetric(ref double metric, double sampleMs)
    {
        if (double.IsNaN(sampleMs) || double.IsInfinity(sampleMs) || sampleMs < 0)
        {
            return;
        }

        metric = metric < 0
            ? sampleMs
            : ((metric * 0.65) + (sampleMs * 0.35));

        UpdatePerformanceStatusBar();
    }

    private void UpdatePerformanceStatusBar()
    {
        if (StatusPerfTextBlock is null)
        {
            return;
        }

        StatusPerfTextBlock.Text =
            $"open {FormatPerfMetric(_perfOpenMs)} | style {FormatPerfMetric(_perfStyleMs)} | fold {FormatPerfMetric(_perfFoldMs)} | diff {FormatPerfMetric(_perfDiffMs)}";
    }

    private static string FormatPerfMetric(double metric)
        => metric < 0 ? "--" : $"{metric:F0}ms";
}
