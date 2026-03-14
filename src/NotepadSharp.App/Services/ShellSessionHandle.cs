using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NotepadSharp.App.Services;

public sealed class ShellSessionHandle : IDisposable
{
    private readonly IDisposable? _ownedResources;
    private int _disposed;

    public ShellSessionHandle(
        Process process,
        string displayName,
        bool usesPty,
        TextWriter inputWriter,
        IReadOnlyList<TextReader> outputReaders,
        string? startupNote = null,
        IDisposable? ownedResources = null)
    {
        Process = process ?? throw new ArgumentNullException(nameof(process));
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? throw new ArgumentException("Display name is required.", nameof(displayName))
            : displayName;
        InputWriter = inputWriter ?? throw new ArgumentNullException(nameof(inputWriter));
        OutputReaders = outputReaders ?? throw new ArgumentNullException(nameof(outputReaders));
        UsesPty = usesPty;
        StartupNote = startupNote;
        _ownedResources = ownedResources;
    }

    public Process Process { get; }

    public string DisplayName { get; }

    public bool UsesPty { get; }

    public TextWriter InputWriter { get; }

    public IReadOnlyList<TextReader> OutputReaders { get; }

    public string? StartupNote { get; }

    public bool IsAlive
    {
        get
        {
            try
            {
                return !Process.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }

    public Task WaitForExitAsync()
        => Process.WaitForExitAsync();

    public bool WaitForExit(int milliseconds)
    {
        try
        {
            return Process.WaitForExit(milliseconds);
        }
        catch
        {
            return true;
        }
    }

    public bool TryGetExitCode(out int exitCode)
    {
        try
        {
            exitCode = Process.ExitCode;
            return true;
        }
        catch
        {
            exitCode = -1;
            return false;
        }
    }

    public void CloseInput()
    {
        try
        {
            InputWriter.Close();
        }
        catch
        {
            // Ignore process teardown races.
        }
    }

    public void Kill(bool entireProcessTree)
    {
        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree);
            }
        }
        catch
        {
            // Ignore teardown races.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _ownedResources?.Dispose();
        }
        catch
        {
            // Ignore teardown races.
        }

        try
        {
            Process.Dispose();
        }
        catch
        {
            // Ignore teardown races.
        }
    }
}
