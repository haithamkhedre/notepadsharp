using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace NotepadSharp.App.Services;

public sealed class WindowsPseudoConsoleSession : IDisposable
{
    private readonly SafePseudoConsoleHandle _pseudoConsoleHandle;
    private readonly StreamWriter _inputWriter;
    private readonly StreamReader _outputReader;
    private readonly Process _process;
    private int _disposed;

    private WindowsPseudoConsoleSession(
        SafePseudoConsoleHandle pseudoConsoleHandle,
        StreamWriter inputWriter,
        StreamReader outputReader,
        Process process)
    {
        _pseudoConsoleHandle = pseudoConsoleHandle;
        _inputWriter = inputWriter;
        _outputReader = outputReader;
        _process = process;
    }

    public Process Process => _process;

    public TextWriter InputWriter => _inputWriter;

    public TextReader OutputReader => _outputReader;

    public static ShellSessionHandle Start(ShellCommandInvocation invocation, string workingDirectory, short columns = 120, short rows = 40)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows pseudo console sessions are only available on Windows.");
        }

        SafeFileHandle? pseudoConsoleInputRead = null;
        SafeFileHandle? appInputWrite = null;
        SafeFileHandle? appOutputRead = null;
        SafeFileHandle? pseudoConsoleOutputWrite = null;
        SafePseudoConsoleHandle? pseudoConsoleHandle = null;
        IntPtr attributeList = IntPtr.Zero;
        IntPtr threadHandle = IntPtr.Zero;
        IntPtr processHandle = IntPtr.Zero;
        Process? process = null;

        try
        {
            CreatePseudoConsolePipes(
                out pseudoConsoleInputRead,
                out appInputWrite,
                out appOutputRead,
                out pseudoConsoleOutputWrite);

            var hResult = CreatePseudoConsole(
                new Coord(columns, rows),
                pseudoConsoleInputRead.DangerousGetHandle(),
                pseudoConsoleOutputWrite.DangerousGetHandle(),
                0,
                out var rawPseudoConsoleHandle);
            if (hResult != 0)
            {
                Marshal.ThrowExceptionForHR(hResult);
            }

            pseudoConsoleHandle = new SafePseudoConsoleHandle(rawPseudoConsoleHandle);

            pseudoConsoleInputRead.Dispose();
            pseudoConsoleInputRead = null;
            pseudoConsoleOutputWrite.Dispose();
            pseudoConsoleOutputWrite = null;

            attributeList = BuildPseudoConsoleAttributeList(pseudoConsoleHandle.DangerousGetHandle());

            var startupInfo = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    cb = Marshal.SizeOf<StartupInfoEx>(),
                },
                lpAttributeList = attributeList,
            };

            var commandLine = new StringBuilder(ShellCommandLogic.BuildWindowsCommandLine(invocation.FileName, invocation.Arguments));
            var creationFlags = ExtendedStartupInfoPresent | CreateUnicodeEnvironment;
            if (!CreateProcessW(
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    creationFlags,
                    IntPtr.Zero,
                    workingDirectory,
                    ref startupInfo,
                    out var processInformation))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            threadHandle = processInformation.hThread;
            processHandle = processInformation.hProcess;
            process = Process.GetProcessById(unchecked((int)processInformation.dwProcessId));

            CloseHandle(threadHandle);
            threadHandle = IntPtr.Zero;
            CloseHandle(processHandle);
            processHandle = IntPtr.Zero;

            var inputWriter = new StreamWriter(
                new FileStream(appInputWrite, FileAccess.Write, bufferSize: 4096, isAsync: true),
                Encoding.UTF8)
            {
                AutoFlush = false,
                NewLine = "\r\n",
            };
            appInputWrite = null;

            var outputReader = new StreamReader(
                new FileStream(appOutputRead, FileAccess.Read, bufferSize: 4096, isAsync: true),
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false);
            appOutputRead = null;

            var transport = new WindowsPseudoConsoleSession(
                pseudoConsoleHandle,
                inputWriter,
                outputReader,
                process);
            pseudoConsoleHandle = null;

            return new ShellSessionHandle(
                process,
                invocation.DisplayName,
                usesPty: true,
                inputWriter,
                new[] { outputReader },
                ownedResources: transport);
        }
        catch
        {
            process?.Dispose();
            pseudoConsoleHandle?.Dispose();
            pseudoConsoleInputRead?.Dispose();
            appInputWrite?.Dispose();
            appOutputRead?.Dispose();
            pseudoConsoleOutputWrite?.Dispose();

            if (threadHandle != IntPtr.Zero)
            {
                CloseHandle(threadHandle);
            }

            if (processHandle != IntPtr.Zero)
            {
                CloseHandle(processHandle);
            }

            throw;
        }
        finally
        {
            if (attributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }
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
            _inputWriter.Dispose();
        }
        catch
        {
            // Ignore teardown races.
        }

        try
        {
            _outputReader.Dispose();
        }
        catch
        {
            // Ignore teardown races.
        }

        _pseudoConsoleHandle.Dispose();
    }

    private static IntPtr BuildPseudoConsoleAttributeList(IntPtr pseudoConsoleHandle)
    {
        nuint attributeListSize = 0;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);
        var attributeList = Marshal.AllocHGlobal(unchecked((int)attributeListSize));

        if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize))
        {
            Marshal.FreeHGlobal(attributeList);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (!UpdateProcThreadAttribute(
                attributeList,
                0,
                new IntPtr(ProcThreadAttributePseudoConsole),
                pseudoConsoleHandle,
                new IntPtr(IntPtr.Size),
                IntPtr.Zero,
                IntPtr.Zero))
        {
            DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return attributeList;
    }

    private static void CreatePseudoConsolePipes(
        out SafeFileHandle pseudoConsoleInputRead,
        out SafeFileHandle appInputWrite,
        out SafeFileHandle appOutputRead,
        out SafeFileHandle pseudoConsoleOutputWrite)
    {
        var securityAttributes = new SecurityAttributes
        {
            nLength = Marshal.SizeOf<SecurityAttributes>(),
            bInheritHandle = true,
        };

        if (!CreatePipe(out pseudoConsoleInputRead, out appInputWrite, ref securityAttributes, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (!CreatePipe(out appOutputRead, out pseudoConsoleOutputWrite, ref securityAttributes, 0))
        {
            pseudoConsoleInputRead.Dispose();
            appInputWrite.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (!SetHandleInformation(appInputWrite, HandleFlagInherit, 0))
        {
            pseudoConsoleInputRead.Dispose();
            appInputWrite.Dispose();
            appOutputRead.Dispose();
            pseudoConsoleOutputWrite.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (!SetHandleInformation(appOutputRead, HandleFlagInherit, 0))
        {
            pseudoConsoleInputRead.Dispose();
            appInputWrite.Dispose();
            appOutputRead.Dispose();
            pseudoConsoleOutputWrite.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private const int ProcThreadAttributePseudoConsole = 0x00020016;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint HandleFlagInherit = 0x00000001;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        ref SecurityAttributes lpPipeAttributes,
        int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(
        SafeFileHandle hObject,
        uint dwMask,
        uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(
        Coord size,
        IntPtr hInput,
        IntPtr hOutput,
        uint dwFlags,
        out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        uint dwFlags,
        ref nuint lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfoEx lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(IntPtr pseudoConsoleHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord(short x, short y)
    {
        public short X = x;
        public short Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    private sealed class SafePseudoConsoleHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafePseudoConsoleHandle()
            : base(ownsHandle: true)
        {
        }

        public SafePseudoConsoleHandle(IntPtr handle)
            : base(ownsHandle: true)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle()
        {
            ClosePseudoConsole(handle);
            return true;
        }
    }
}
