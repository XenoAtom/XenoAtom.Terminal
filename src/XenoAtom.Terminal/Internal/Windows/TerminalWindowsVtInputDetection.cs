// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal.Internal.Windows;

internal static class TerminalWindowsVtInputDetection
{
    public static bool IsLikelyConPtyHost(Func<string, string?> getEnv)
    {
        ArgumentNullException.ThrowIfNull(getEnv);

        if (!string.IsNullOrEmpty(getEnv("WT_SESSION")))
        {
            return true;
        }

        // VS Code terminal sets TERM_PROGRAM=vscode and VSCODE_PID.
        if (!string.IsNullOrEmpty(getEnv("TERM_PROGRAM")))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(getEnv("VSCODE_PID")))
        {
            return true;
        }

        return false;
    }

    public static bool IsLikelyConPtyHost()
        => IsLikelyConPtyHost(Environment.GetEnvironmentVariable);
}

