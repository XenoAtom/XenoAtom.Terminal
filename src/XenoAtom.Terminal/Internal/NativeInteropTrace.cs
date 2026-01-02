// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal.Internal;

internal static class NativeInteropTrace
{
    private static readonly bool Enabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XENOATOM_TERMINAL_TRACE_NATIVE"));
    private static string? _lastLogged;

    public static string? LastCall;

    public static void Mark(string call)
    {
        LastCall = call;

        if (!Enabled || ReferenceEquals(_lastLogged, call))
        {
            return;
        }

        _lastLogged = call;
        try
        {
            Console.Error.Write($"[XenoAtom.Terminal.Native] {call}");
            Console.Error.Flush();
            Thread.Sleep(1);
        }
        catch
        {
        }
    }
}

