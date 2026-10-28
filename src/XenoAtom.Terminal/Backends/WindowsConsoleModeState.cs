// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Terminal.Internal.Windows;

namespace XenoAtom.Terminal.Backends;

/// <summary>
/// Tracks the Windows console modes changed by the backend so they can be restored when input stops or the backend is disposed.
/// </summary>
internal sealed class WindowsConsoleModeState
{
    internal delegate bool TryGetModeDelegate(nint handle, out uint mode);
    internal delegate bool TrySetModeDelegate(nint handle, uint mode);

    private readonly Lock _sync = new();
    private readonly TryGetModeDelegate _tryGetMode;
    private readonly TrySetModeDelegate _trySetMode;

    private uint _savedInputMode;
    private bool _hasSavedInputMode;
    private bool _enabledVirtualTerminalOutput;

    internal WindowsConsoleModeState()
        : this(Win32Console.GetConsoleMode, Win32Console.SetConsoleMode)
    {
    }

    internal WindowsConsoleModeState(TryGetModeDelegate tryGetMode, TrySetModeDelegate trySetMode)
    {
        _tryGetMode = tryGetMode ?? throw new ArgumentNullException(nameof(tryGetMode));
        _trySetMode = trySetMode ?? throw new ArgumentNullException(nameof(trySetMode));
    }

    internal bool TryEnableVirtualTerminalInput(nint handle)
    {
        if (IsInvalidHandle(handle) || !_tryGetMode(handle, out var mode))
        {
            return false;
        }

        if ((mode & Win32Console.ENABLE_VIRTUAL_TERMINAL_INPUT) != 0)
        {
            return true;
        }

        if (!_trySetMode(handle, mode | Win32Console.ENABLE_VIRTUAL_TERMINAL_INPUT))
        {
            return false;
        }

        SaveInputModeIfNeeded(mode);
        return _tryGetMode(handle, out var after) && (after & Win32Console.ENABLE_VIRTUAL_TERMINAL_INPUT) != 0;
    }

    internal void SaveInputModeIfNeeded(uint mode)
    {
        lock (_sync)
        {
            if (!_hasSavedInputMode)
            {
                _savedInputMode = mode;
                _hasSavedInputMode = true;
            }
        }
    }

    internal void RestoreInputMode(nint handle)
    {
        uint? restoreMode = null;
        lock (_sync)
        {
            if (_hasSavedInputMode)
            {
                restoreMode = _savedInputMode;
                _hasSavedInputMode = false;
            }
        }

        if (restoreMode.HasValue && !IsInvalidHandle(handle))
        {
            _trySetMode(handle, restoreMode.Value);
        }
    }

    internal bool TryEnableVirtualTerminalOutput(nint handle)
    {
        if (IsInvalidHandle(handle) || !_tryGetMode(handle, out var mode))
        {
            return false;
        }

        if ((mode & Win32Console.ENABLE_VIRTUAL_TERMINAL_PROCESSING) != 0)
        {
            return true;
        }

        if (!_trySetMode(handle, mode | Win32Console.ENABLE_VIRTUAL_TERMINAL_PROCESSING))
        {
            return false;
        }

        lock (_sync)
        {
            _enabledVirtualTerminalOutput = true;
        }

        return true;
    }

    internal void RestoreVirtualTerminalOutputMode(nint handle)
    {
        lock (_sync)
        {
            if (!_enabledVirtualTerminalOutput)
            {
                return;
            }

            _enabledVirtualTerminalOutput = false;
        }

        if (!IsInvalidHandle(handle)
            && _tryGetMode(handle, out var mode)
            && (mode & Win32Console.ENABLE_VIRTUAL_TERMINAL_PROCESSING) != 0)
        {
            _trySetMode(handle, mode & ~Win32Console.ENABLE_VIRTUAL_TERMINAL_PROCESSING);
        }
    }

    private static bool IsInvalidHandle(nint handle) => handle == 0 || handle == -1;
}
