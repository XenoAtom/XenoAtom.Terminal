// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Diagnostics;
using System.Text;

namespace XenoAtom.Terminal.Internal.Unix;

internal static class UnixClipboard
{
    internal readonly struct Provider
    {
        public readonly string? GetExe;
        public readonly string? GetArgs;
        public readonly string? SetExe;
        public readonly string? SetArgs;

        public Provider(string getExe, string getArgs, string setExe, string setArgs)
        {
            GetExe = getExe;
            GetArgs = getArgs;
            SetExe = setExe;
            SetArgs = setArgs;
        }

        public bool IsAvailable => GetExe is not null && SetExe is not null;
    }

    public static bool TryDetectProvider(out Provider provider)
    {
        provider = default;

        if (OperatingSystem.IsMacOS())
        {
            if (TryFindInPath("pbcopy", out var pbcopy) && TryFindInPath("pbpaste", out var pbpaste))
            {
                provider = new Provider(pbpaste, string.Empty, pbcopy, string.Empty);
                return true;
            }

            if (File.Exists("/usr/bin/pbcopy") && File.Exists("/usr/bin/pbpaste"))
            {
                provider = new Provider("/usr/bin/pbpaste", string.Empty, "/usr/bin/pbcopy", string.Empty);
                return true;
            }

            return false;
        }

        // Linux / other Unix: prefer Wayland tools when available, then X11.
        var hasWayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        if (hasWayland && TryFindInPath("wl-copy", out var wlCopy) && TryFindInPath("wl-paste", out var wlPaste))
        {
            provider = new Provider(wlPaste, string.Empty, wlCopy, string.Empty);
            return true;
        }

        var hasX11 = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));
        if (hasX11 && TryFindInPath("xclip", out var xclip))
        {
            provider = new Provider(xclip, "-selection clipboard -o", xclip, "-selection clipboard -i");
            return true;
        }

        if (hasX11 && TryFindInPath("xsel", out var xsel))
        {
            provider = new Provider(xsel, "--clipboard --output", xsel, "--clipboard --input");
            return true;
        }

        return false;
    }

    public static bool TryGetText(in Provider provider, out string? text)
    {
        text = null;
        if (!provider.IsAvailable)
        {
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = provider.GetExe!,
                Arguments = provider.GetArgs ?? string.Empty,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return false;
            }

            var output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(milliseconds: 1000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            if (proc.ExitCode != 0)
            {
                return false;
            }

            text = output;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TrySetText(in Provider provider, ReadOnlySpan<char> text)
    {
        if (!provider.IsAvailable)
        {
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = provider.SetExe!,
                Arguments = provider.SetArgs ?? string.Empty,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardInputEncoding = Encoding.UTF8,
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return false;
            }

            proc.StandardInput.Write(text);
            proc.StandardInput.Close();

            if (!proc.WaitForExit(milliseconds: 1000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
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
}

