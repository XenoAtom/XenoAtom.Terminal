// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Reflection;
using System.Runtime.InteropServices;
using XenoAtom.Terminal.Internal;

namespace XenoAtom.Terminal.Internal.Unix;

internal static unsafe class LibC
{
    private const string LibraryName = "libc";
    private static nint _libcHandle;

    public const int STDIN_FILENO = 0;
    public const int STDOUT_FILENO = 1;
    public const int STDERR_FILENO = 2;

    public const int TCSANOW = 0;

    public const short POLLIN = 0x0001;

    static LibC()
    {
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(LibC).Assembly, Resolve);
        }
        catch
        {
            // Best-effort; if the runtime does not allow installing a resolver,
            // default native library resolution will be used.
        }
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            return 0;
        }

        var handle = Volatile.Read(ref _libcHandle);
        if (handle != 0)
        {
            return handle;
        }

        handle = TryLoadLibc();
        if (handle != 0)
        {
            Volatile.Write(ref _libcHandle, handle);
        }

        return handle;
    }

    private static nint TryLoadLibc()
    {
        // .NET runs on both glibc and musl Linux distributions.
        // - glibc typically exposes libc.so.6
        // - musl exposes libc.musl-<arch>.so.1 and ld-musl-<arch>.so.1 (often the same file)
        // Avoid assuming that a plain "libc" exists at runtime.
        var rid = RuntimeInformation.RuntimeIdentifier ?? string.Empty;
        var isMusl = rid.Contains("musl", StringComparison.OrdinalIgnoreCase);

        if (OperatingSystem.IsMacOS())
        {
            // macOS: libc symbols are in libSystem.
            return TryLoadFirst(
                "libSystem.B.dylib",
                "libc.dylib",
                "libc");
        }

        if (OperatingSystem.IsLinux())
        {
            var arch = RuntimeInformation.ProcessArchitecture;
            var muslCandidates = GetMuslCandidates(arch);

            if (isMusl)
            {
                var handle = TryLoadFirst(muslCandidates);
                if (handle != 0) return handle;
            }

            // glibc first.
            var glibc = TryLoadFirst("libc.so.6", "libc.so");
            if (glibc != 0) return glibc;

            // Fallback to musl names even when the RID is not explicit.
            var musl = TryLoadFirst(muslCandidates);
            if (musl != 0) return musl;

            // Last resort.
            return TryLoadFirst("libc");
        }

        return 0;
    }

    private static nint TryLoadFirst(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }

            // Some minimal environments only have /lib in the search path.
            if (OperatingSystem.IsLinux() && candidate.IndexOf('/') < 0)
            {
                if (NativeLibrary.TryLoad("/lib/" + candidate, out handle))
                {
                    return handle;
                }
            }
        }

        return 0;
    }

    private static string[] GetMuslCandidates(Architecture arch)
    {
        return arch switch
        {
            Architecture.X64 =>
            [
                "libc.musl-x86_64.so.1",
                "ld-musl-x86_64.so.1",
                "/lib/libc.musl-x86_64.so.1",
                "/lib/ld-musl-x86_64.so.1",
            ],
            Architecture.X86 =>
            [
                "libc.musl-i386.so.1",
                "ld-musl-i386.so.1",
                "/lib/libc.musl-i386.so.1",
                "/lib/ld-musl-i386.so.1",
            ],
            Architecture.Arm64 =>
            [
                "libc.musl-aarch64.so.1",
                "ld-musl-aarch64.so.1",
                "/lib/libc.musl-aarch64.so.1",
                "/lib/ld-musl-aarch64.so.1",
            ],
            Architecture.Arm =>
            [
                "libc.musl-armhf.so.1",
                "libc.musl-armv7.so.1",
                "libc.musl-arm.so.1",
                "ld-musl-armhf.so.1",
                "ld-musl-armv7.so.1",
                "ld-musl-arm.so.1",
                "/lib/libc.musl-armhf.so.1",
                "/lib/libc.musl-armv7.so.1",
                "/lib/libc.musl-arm.so.1",
                "/lib/ld-musl-armhf.so.1",
                "/lib/ld-musl-armv7.so.1",
                "/lib/ld-musl-arm.so.1",
            ],
            _ =>
            [
                "libc.musl-" + arch.ToString().ToLowerInvariant() + ".so.1",
                "ld-musl-" + arch.ToString().ToLowerInvariant() + ".so.1",
            ],
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int fd;
        public short events;
        public short revents;
    }

    [DllImport(LibraryName, SetLastError = true, EntryPoint = "isatty")]
    private static extern int isatty_native(int fd);

    [DllImport(LibraryName, SetLastError = true, EntryPoint = "poll")]
    private static extern int poll_native(PollFd* fds, nuint nfds, int timeout);

    [DllImport(LibraryName, SetLastError = true, EntryPoint = "read")]
    private static extern nint read_native(int fd, void* buf, nuint count);

    public static int isatty(int fd)
    {
        NativeInteropTrace.Mark("libc.isatty");
        return isatty_native(fd);
    }

    public static int poll(PollFd* fds, nuint nfds, int timeout)
    {
        NativeInteropTrace.Mark("libc.poll");
        return poll_native(fds, nfds, timeout);
    }

    public static nint read(int fd, void* buf, nuint count)
    {
        NativeInteropTrace.Mark("libc.read");
        return read_native(fd, buf, count);
    }

    // Linux (glibc/musl): termios uses 32-bit flags and includes c_line + 32 cc bytes.
    //
    // NOTE: We intentionally only define the small subset of flags we need for terminal input mode configuration.
    // See `man termios` for detailed semantics.
    public const uint LINUX_ISIG = 0x00000001;   // Enable signal generation (Ctrl+C, Ctrl+Z, ...).
    public const uint LINUX_ICANON = 0x00000002; // Canonical mode (line buffering, special line editing keys).
    public const uint LINUX_ECHO = 0x00000008;   // Echo input characters.
    public const uint LINUX_IEXTEN = 0x00008000; // Implementation-defined input processing extensions.

    public const uint LINUX_ICRNL = 0x00000100;  // Map CR to NL on input (affects Enter in cbreak/raw-like modes).
    public const uint LINUX_IXON = 0x00000400;   // XON/XOFF software flow control (Ctrl+S / Ctrl+Q).

    public const int LINUX_VTIME = 5;
    public const int LINUX_VMIN = 6;

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct termios_linux
    {
        public uint c_iflag;
        public uint c_oflag;
        public uint c_cflag;
        public uint c_lflag;
        public byte c_line;
        public fixed byte c_cc[32];
        public uint c_ispeed;
        public uint c_ospeed;
    }

    [DllImport(LibraryName, SetLastError = true, EntryPoint = "tcgetattr")]
    private static extern int tcgetattr_linux_native(int fd, out termios_linux termios);

    [DllImport(LibraryName, SetLastError = true, EntryPoint = "tcsetattr")]
    private static extern int tcsetattr_linux_native(int fd, int optionalActions, in termios_linux termios);

    [DllImport(LibraryName, EntryPoint = "cfmakeraw")]
    private static extern void cfmakeraw_linux_native(ref termios_linux termios);

    public static int tcgetattr_linux(int fd, out termios_linux termios)
    {
        NativeInteropTrace.Mark("libc.tcgetattr (linux)");
        return tcgetattr_linux_native(fd, out termios);
    }

    public static int tcsetattr_linux(int fd, int optionalActions, in termios_linux termios)
    {
        NativeInteropTrace.Mark("libc.tcsetattr (linux)");
        return tcsetattr_linux_native(fd, optionalActions, in termios);
    }

    public static void cfmakeraw_linux(ref termios_linux termios)
    {
        NativeInteropTrace.Mark("libc.cfmakeraw (linux)");
        cfmakeraw_linux_native(ref termios);
    }

    // macOS: termios uses unsigned long (64-bit) flags and includes 20 cc bytes (no c_line field).
    //
    // NOTE: We intentionally only define the small subset of flags we need for terminal input mode configuration.
    // See `man termios` for detailed semantics.
    public const nuint MACOS_ISIG = 0x00000080;   // Enable signal generation (Ctrl+C, Ctrl+Z, ...).
    public const nuint MACOS_ICANON = 0x00000100; // Canonical mode (line buffering, special line editing keys).
    public const nuint MACOS_ECHO = 0x00000008;   // Echo input characters.
    public const nuint MACOS_IEXTEN = 0x00000400; // Implementation-defined input processing extensions.

    public const nuint MACOS_ICRNL = 0x00000100;  // Map CR to NL on input (affects Enter in cbreak/raw-like modes).
    public const nuint MACOS_IXON = 0x00000200;   // XON/XOFF software flow control (Ctrl+S / Ctrl+Q).

    public const int MACOS_VTIME = 17;
    public const int MACOS_VMIN = 16;

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct termios_macos
    {
        public nuint c_iflag;
        public nuint c_oflag;
        public nuint c_cflag;
        public nuint c_lflag;
        public fixed byte c_cc[20];
        public nuint c_ispeed;
        public nuint c_ospeed;
    }

    [DllImport(LibraryName, SetLastError = true, EntryPoint = "tcgetattr")]
    private static extern int tcgetattr_macos_native(int fd, out termios_macos termios);

    [DllImport(LibraryName, SetLastError = true, EntryPoint = "tcsetattr")]
    private static extern int tcsetattr_macos_native(int fd, int optionalActions, in termios_macos termios);

    [DllImport(LibraryName, EntryPoint = "cfmakeraw")]
    private static extern void cfmakeraw_macos_native(ref termios_macos termios);

    public static int tcgetattr_macos(int fd, out termios_macos termios)
    {
        NativeInteropTrace.Mark("libc.tcgetattr (macos)");
        return tcgetattr_macos_native(fd, out termios);
    }

    public static int tcsetattr_macos(int fd, int optionalActions, in termios_macos termios)
    {
        NativeInteropTrace.Mark("libc.tcsetattr (macos)");
        return tcsetattr_macos_native(fd, optionalActions, in termios);
    }

    public static void cfmakeraw_macos(ref termios_macos termios)
    {
        NativeInteropTrace.Mark("libc.cfmakeraw (macos)");
        cfmakeraw_macos_native(ref termios);
    }
}
