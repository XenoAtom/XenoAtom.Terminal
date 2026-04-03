// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace XenoAtom.Terminal.Internal.Unix;

internal static class UnixClipboard
{
    internal enum ProviderKind
    {
        None,
        MacOsAppKit,
        MacOsPbCopy,
        Wayland,
        Xclip,
        Xsel,
    }

    internal readonly struct Provider
    {
        public readonly ProviderKind Kind;
        public readonly string ReadExe;
        public readonly string WriteExe;

        public Provider(ProviderKind kind, string readExe, string writeExe)
        {
            Kind = kind;
            ReadExe = readExe;
            WriteExe = writeExe;
        }

        public bool IsAvailable => Kind != ProviderKind.None;

        public bool SupportsText => Kind != ProviderKind.None;

        public bool SupportsFormats => Kind is ProviderKind.MacOsAppKit or ProviderKind.Wayland or ProviderKind.Xclip;
    }

    private static readonly string[] TextMimeCandidates =
    [
        "text/plain;charset=utf-8",
        "text/plain",
        "UTF8_STRING",
        "STRING",
        "TEXT",
    ];

    public static bool TryDetectProvider(out Provider provider)
    {
        provider = default;

        if (OperatingSystem.IsMacOS())
        {
            if (TryFindInPath("osascript", out var osascript))
            {
                provider = new Provider(ProviderKind.MacOsAppKit, osascript, osascript);
                return true;
            }

            if (TryFindInPath("pbcopy", out var pbcopy) && TryFindInPath("pbpaste", out var pbpaste))
            {
                provider = new Provider(ProviderKind.MacOsPbCopy, pbpaste, pbcopy);
                return true;
            }

            if (File.Exists("/usr/bin/osascript"))
            {
                provider = new Provider(ProviderKind.MacOsAppKit, "/usr/bin/osascript", "/usr/bin/osascript");
                return true;
            }

            if (File.Exists("/usr/bin/pbcopy") && File.Exists("/usr/bin/pbpaste"))
            {
                provider = new Provider(ProviderKind.MacOsPbCopy, "/usr/bin/pbpaste", "/usr/bin/pbcopy");
                return true;
            }

            return false;
        }

        var hasWayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        if (hasWayland && TryFindInPath("wl-copy", out var wlCopy) && TryFindInPath("wl-paste", out var wlPaste))
        {
            provider = new Provider(ProviderKind.Wayland, wlPaste, wlCopy);
            return true;
        }

        var hasX11 = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));
        if (hasX11 && TryFindInPath("xclip", out var xclip))
        {
            provider = new Provider(ProviderKind.Xclip, xclip, xclip);
            return true;
        }

        if (hasX11 && TryFindInPath("xsel", out var xsel))
        {
            provider = new Provider(ProviderKind.Xsel, xsel, xsel);
            return true;
        }

        return false;
    }

    public static bool TryGetText(in Provider provider, out string? text)
    {
        text = null;
        if (!provider.IsAvailable || !provider.SupportsText)
        {
            return false;
        }

        return provider.Kind switch
        {
            ProviderKind.MacOsPbCopy => TryRunForText(provider.ReadExe, [], out text),
            ProviderKind.Xsel => TryRunForText(provider.ReadExe, ["--clipboard", "--output"], out text),
            _ => TryGetData(provider, TerminalClipboardFormats.Text, out var data) && TryDecodeUtf8(data, out text),
        };
    }

    public static bool TrySetText(in Provider provider, ReadOnlySpan<char> text)
    {
        if (!provider.IsAvailable || !provider.SupportsText)
        {
            return false;
        }

        return provider.Kind switch
        {
            ProviderKind.MacOsPbCopy => TryRunWithUtf8Input(provider.WriteExe, [], text),
            ProviderKind.Xsel => TryRunWithUtf8Input(provider.WriteExe, ["--clipboard", "--input"], text),
            _ => TrySetData(provider, TerminalClipboardFormats.Text, Encoding.UTF8.GetBytes(text.ToString())),
        };
    }

    public static bool TryGetFormats(in Provider provider, [NotNullWhen(true)] out IReadOnlyList<string>? formats)
    {
        formats = null;
        if (!provider.SupportsFormats)
        {
            return false;
        }

        return provider.Kind switch
        {
            ProviderKind.MacOsAppKit => TryGetFormatsFromAppKit(provider, out formats),
            ProviderKind.Wayland => TryGetFormatsFromCommand(provider.ReadExe, ["--list-types"], out formats),
            ProviderKind.Xclip => TryGetFormatsFromCommand(provider.ReadExe, ["-selection", "clipboard", "-t", "TARGETS", "-o"], out formats),
            _ => false,
        };
    }

    public static bool TryGetData(in Provider provider, string format, [NotNullWhen(true)] out byte[]? data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        data = null;

        if (!provider.IsAvailable)
        {
            return false;
        }

        if (!provider.SupportsFormats)
        {
            if (!TerminalClipboardFormatHelper.IsText(format))
            {
                return false;
            }

            return TryGetText(provider, out var text) && TryEncodeUtf8(text, out data);
        }

        var normalized = TerminalClipboardFormatHelper.Normalize(format);

        return provider.Kind switch
        {
            ProviderKind.MacOsAppKit => TryGetDataFromAppKit(provider, normalized, out data),
            ProviderKind.Wayland => TryGetDataFromCandidates(provider.ReadExe, GetUnixFormatCandidates(normalized), out data, "--type"),
            ProviderKind.Xclip => TryGetDataFromCandidates(provider.ReadExe, GetUnixFormatCandidates(normalized), out data, "-selection", "clipboard", "-t"),
            _ => false,
        };
    }

    public static bool TrySetData(in Provider provider, string format, ReadOnlySpan<byte> data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        if (!provider.IsAvailable)
        {
            return false;
        }

        if (!provider.SupportsFormats)
        {
            if (!TerminalClipboardFormatHelper.IsText(format))
            {
                return false;
            }

            return TrySetText(provider, Encoding.UTF8.GetString(data).AsSpan());
        }

        var normalized = TerminalClipboardFormatHelper.Normalize(format);
        return provider.Kind switch
        {
            ProviderKind.MacOsAppKit => TrySetDataWithAppKit(provider, normalized, data),
            ProviderKind.Wayland => TryRunWithBinaryInput(provider.WriteExe, ["--type", MapUnixWriteFormat(normalized)], data),
            ProviderKind.Xclip => TryRunWithBinaryInput(provider.WriteExe, ["-selection", "clipboard", "-t", MapUnixWriteFormat(normalized), "-i"], data),
            _ => false,
        };
    }

    private static bool TryGetFormatsFromAppKit(in Provider provider, [NotNullWhen(true)] out IReadOnlyList<string>? formats)
    {
        formats = null;
        if (!TryRunForText(provider.ReadExe, CreateAppleScriptArguments(AppKitListTypesScript), out var output))
        {
            return false;
        }

        return TryNormalizeFormats(output, out formats);
    }

    private static bool TryGetDataFromAppKit(in Provider provider, string normalizedFormat, [NotNullWhen(true)] out byte[]? data)
    {
        foreach (var candidate in GetMacOsFormatCandidates(normalizedFormat))
        {
            if (!TryRunForText(provider.ReadExe, CreateAppleScriptArguments(AppKitReadDataScript, candidate), out var base64)
                || string.IsNullOrEmpty(base64))
            {
                continue;
            }

            try
            {
                data = Convert.FromBase64String(base64);
                return true;
            }
            catch
            {
                // Try next candidate.
            }
        }

        data = null;
        return false;
    }

    private static bool TrySetDataWithAppKit(in Provider provider, string normalizedFormat, ReadOnlySpan<byte> data)
    {
        var encoded = Convert.ToBase64String(data);
        foreach (var candidate in GetMacOsFormatCandidates(normalizedFormat))
        {
            if (TryRunWithUtf8Input(provider.WriteExe, CreateAppleScriptArguments(AppKitWriteDataScript, candidate), encoded.AsSpan()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetFormatsFromCommand(string exe, IReadOnlyList<string> arguments, [NotNullWhen(true)] out IReadOnlyList<string>? formats)
    {
        formats = null;
        if (!TryRunForText(exe, arguments, out var output))
        {
            return false;
        }

        return TryNormalizeFormats(output, out formats);
    }

    private static bool TryNormalizeFormats(string? output, [NotNullWhen(true)] out IReadOnlyList<string>? formats)
    {
        formats = null;
        if (output is null)
        {
            return false;
        }

        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var normalized = TerminalClipboardFormatHelper.Normalize(line);
            if (seen.Add(normalized))
            {
                list.Add(normalized);
            }
        }

        formats = list;
        return true;
    }

    private static bool TryGetDataFromCandidates(string exe, IReadOnlyList<string> candidates, [NotNullWhen(true)] out byte[]? data, params string[] prefixArguments)
    {
        data = null;
        foreach (var candidate in candidates)
        {
            var args = new List<string>(prefixArguments.Length + 2);
            args.AddRange(prefixArguments);
            args.Add(candidate);

            if (prefixArguments.Length >= 2 && string.Equals(prefixArguments[0], "-selection", StringComparison.Ordinal))
            {
                args.Add("-o");
            }

            if (prefixArguments.Length == 1 && string.Equals(prefixArguments[0], "--type", StringComparison.Ordinal))
            {
                // Nothing else required for wl-paste.
            }

            if (TryRunForBytes(exe, args, out data))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> GetUnixFormatCandidates(string normalizedFormat)
    {
        if (string.Equals(normalizedFormat, TerminalClipboardFormats.Text, StringComparison.Ordinal))
        {
            return TextMimeCandidates;
        }

        return normalizedFormat switch
        {
            var value when string.Equals(value, TerminalClipboardFormats.Html, StringComparison.Ordinal) => ["text/html"],
            var value when string.Equals(value, TerminalClipboardFormats.RichText, StringComparison.Ordinal) => ["text/rtf", "text/richtext"],
            var value when string.Equals(value, TerminalClipboardFormats.Png, StringComparison.Ordinal) => ["image/png"],
            var value when string.Equals(value, TerminalClipboardFormats.Tiff, StringComparison.Ordinal) => ["image/tiff"],
            _ => [normalizedFormat],
        };
    }

    private static string MapUnixWriteFormat(string normalizedFormat) => normalizedFormat switch
    {
        var value when string.Equals(value, TerminalClipboardFormats.Text, StringComparison.Ordinal) => "text/plain;charset=utf-8",
        _ => normalizedFormat,
    };

    private static IReadOnlyList<string> GetMacOsFormatCandidates(string normalizedFormat)
    {
        if (string.Equals(normalizedFormat, TerminalClipboardFormats.Text, StringComparison.Ordinal))
        {
            return ["public.utf8-plain-text", "public.plain-text", "public.utf16-external-plain-text"];
        }

        return normalizedFormat switch
        {
            var value when string.Equals(value, TerminalClipboardFormats.Html, StringComparison.Ordinal) => ["public.html"],
            var value when string.Equals(value, TerminalClipboardFormats.RichText, StringComparison.Ordinal) => ["public.rtf"],
            var value when string.Equals(value, TerminalClipboardFormats.Png, StringComparison.Ordinal) => ["public.png"],
            var value when string.Equals(value, TerminalClipboardFormats.Tiff, StringComparison.Ordinal) => ["public.tiff"],
            _ => [normalizedFormat],
        };
    }

    private static IReadOnlyList<string> CreateAppleScriptArguments(string script, params string[] userArgs)
    {
        var args = new List<string> { "-e", script };
        if (userArgs.Length > 0)
        {
            args.Add("--");
            args.AddRange(userArgs);
        }

        return args;
    }

    private static bool TryEncodeUtf8(string? text, [NotNullWhen(true)] out byte[]? data)
    {
        if (text is null)
        {
            data = null;
            return false;
        }

        data = Encoding.UTF8.GetBytes(text);
        return true;
    }

    private static bool TryDecodeUtf8(byte[]? data, [NotNullWhen(true)] out string? text)
    {
        if (data is null)
        {
            text = null;
            return false;
        }

        text = Encoding.UTF8.GetString(data);
        return true;
    }

    private static bool TryRunForText(string fileName, IReadOnlyList<string> arguments, [NotNullWhen(true)] out string? output)
    {
        output = null;

        try
        {
            var psi = CreateProcessStartInfo(fileName, arguments);
            psi.RedirectStandardOutput = true;
            psi.StandardOutputEncoding = Encoding.UTF8;

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return false;
            }

            output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(milliseconds: 1000))
            {
                TryKill(proc);
                return false;
            }

            return proc.ExitCode == 0;
        }
        catch
        {
            output = null;
            return false;
        }
    }

    private static bool TryRunForBytes(string fileName, IReadOnlyList<string> arguments, [NotNullWhen(true)] out byte[]? output)
    {
        output = null;

        try
        {
            var psi = CreateProcessStartInfo(fileName, arguments);
            psi.RedirectStandardOutput = true;

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return false;
            }

            using var stream = new MemoryStream();
            proc.StandardOutput.BaseStream.CopyTo(stream);
            if (!proc.WaitForExit(milliseconds: 1000))
            {
                TryKill(proc);
                return false;
            }

            if (proc.ExitCode != 0)
            {
                return false;
            }

            output = stream.ToArray();
            return true;
        }
        catch
        {
            output = null;
            return false;
        }
    }

    private static bool TryRunWithUtf8Input(string fileName, IReadOnlyList<string> arguments, ReadOnlySpan<char> input)
    {
        try
        {
            var psi = CreateProcessStartInfo(fileName, arguments);
            psi.RedirectStandardInput = true;
            psi.StandardInputEncoding = Encoding.UTF8;

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return false;
            }

            proc.StandardInput.Write(input);
            proc.StandardInput.Close();

            if (!proc.WaitForExit(milliseconds: 1000))
            {
                TryKill(proc);
                return false;
            }

            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryRunWithBinaryInput(string fileName, IReadOnlyList<string> arguments, ReadOnlySpan<byte> input)
    {
        try
        {
            var psi = CreateProcessStartInfo(fileName, arguments);
            psi.RedirectStandardInput = true;

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return false;
            }

            proc.StandardInput.BaseStream.Write(input);
            proc.StandardInput.Close();

            if (!proc.WaitForExit(milliseconds: 1000))
            {
                TryKill(proc);
                return false;
            }

            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static ProcessStartInfo CreateProcessStartInfo(string fileName, IReadOnlyList<string> arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        return psi;
    }

    private static void TryKill(Process proc)
    {
        try { proc.Kill(entireProcessTree: true); } catch { }
    }

    private static bool TryFindInPath(string fileName, out string fullPath)
    {
        fullPath = string.Empty;

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var parts = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var candidate = Path.Combine(parts[i], fileName);
            if (File.Exists(candidate))
            {
                fullPath = candidate;
                return true;
            }
        }

        return false;
    }

    private const string AppKitListTypesScript =
        """
        use framework "AppKit"
        use scripting additions
        set pb to current application's NSPasteboard's generalPasteboard()
        set pbTypes to pb's types()
        if pbTypes is missing value then
            return ""
        end if
        set previousDelimiters to AppleScript's text item delimiters
        set AppleScript's text item delimiters to linefeed
        set joinedTypes to (pbTypes as list) as text
        set AppleScript's text item delimiters to previousDelimiters
        return joinedTypes
        """;

    private const string AppKitReadDataScript =
        """
        use framework "AppKit"
        use scripting additions
        on run argv
            set requestedType to item 1 of argv
            set pb to current application's NSPasteboard's generalPasteboard()
            set payload to pb's dataForType:requestedType
            if payload is missing value then
                return ""
            end if
            return (payload's base64EncodedStringWithOptions:0) as text
        end run
        """;

    private const string AppKitWriteDataScript =
        """
        use framework "AppKit"
        use scripting additions
        on run argv
            set requestedType to item 1 of argv
            set payloadText to read stdin
            set payloadData to current application's NSData's alloc()'s initWithBase64EncodedString:payloadText options:0
            if payloadData is missing value then
                error "Invalid clipboard payload."
            end if
            set pb to current application's NSPasteboard's generalPasteboard()
            pb's clearContents()
            set didWrite to pb's setData:payloadData forType:requestedType
            if (didWrite as boolean) is false then
                error "Failed to update clipboard."
            end if
            return ""
        end run
        """;
}
