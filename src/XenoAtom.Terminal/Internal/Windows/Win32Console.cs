// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Runtime.InteropServices;

namespace XenoAtom.Terminal.Internal.Windows;

internal static unsafe class Win32Console
{
    public const int STD_INPUT_HANDLE = -10;
    public const int STD_OUTPUT_HANDLE = -11;
    public const int STD_ERROR_HANDLE = -12;

    public const uint ENABLE_PROCESSED_INPUT = 0x0001;
    public const uint ENABLE_LINE_INPUT = 0x0002;
    public const uint ENABLE_ECHO_INPUT = 0x0004;
    public const uint ENABLE_WINDOW_INPUT = 0x0008;
    public const uint ENABLE_MOUSE_INPUT = 0x0010;
    public const uint ENABLE_INSERT_MODE = 0x0020;
    public const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
    public const uint ENABLE_EXTENDED_FLAGS = 0x0080;
    public const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200;

    public const uint ENABLE_PROCESSED_OUTPUT = 0x0001;
    public const uint ENABLE_WRAP_AT_EOL_OUTPUT = 0x0002;
    public const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    public const uint DISABLE_NEWLINE_AUTO_RETURN = 0x0008;
    public const uint ENABLE_LVB_GRID_WORLDWIDE = 0x0010;

    public const ushort KEY_EVENT = 0x0001;
    public const ushort MOUSE_EVENT = 0x0002;
    public const ushort WINDOW_BUFFER_SIZE_EVENT = 0x0004;

    public const uint FROM_LEFT_1ST_BUTTON_PRESSED = 0x0001;
    public const uint RIGHTMOST_BUTTON_PRESSED = 0x0002;
    public const uint FROM_LEFT_2ND_BUTTON_PRESSED = 0x0004;
    public const uint FROM_LEFT_3RD_BUTTON_PRESSED = 0x0008;
    public const uint FROM_LEFT_4TH_BUTTON_PRESSED = 0x0010;

    public const uint MOUSE_MOVED = 0x0001;
    public const uint DOUBLE_CLICK = 0x0002;
    public const uint MOUSE_WHEELED = 0x0004;
    public const uint MOUSE_HWHEELED = 0x0008;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadConsoleInputW(nint hConsoleInput, INPUT_RECORD* lpBuffer, uint nLength, out uint lpNumberOfEventsRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetConsoleScreenBufferInfo(nint hConsoleOutput, out CONSOLE_SCREEN_BUFFER_INFO lpConsoleScreenBufferInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetConsoleCursorPosition(nint hConsoleOutput, COORD dwCursorPosition);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetConsoleCursorInfo(nint hConsoleOutput, out CONSOLE_CURSOR_INFO lpConsoleCursorInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetConsoleCursorInfo(nint hConsoleOutput, in CONSOLE_CURSOR_INFO lpConsoleCursorInfo);

    public const uint WAIT_OBJECT_0 = 0x00000000;
    public const uint WAIT_TIMEOUT = 0x00000102;
    public const uint WAIT_FAILED = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [StructLayout(LayoutKind.Sequential)]
    public struct COORD
    {
        public short X;
        public short Y;

        public COORD(short x, short y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct SMALL_RECT
    {
        public readonly short Left;
        public readonly short Top;
        public readonly short Right;
        public readonly short Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct CONSOLE_SCREEN_BUFFER_INFO
    {
        public readonly COORD dwSize;
        public readonly COORD dwCursorPosition;
        public readonly ushort wAttributes;
        public readonly SMALL_RECT srWindow;
        public readonly COORD dwMaximumWindowSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CONSOLE_CURSOR_INFO
    {
        public uint dwSize;
        private uint _bVisible;
        public bool bVisible
        {
            get => _bVisible != 0;
            set => _bVisible = value ? 1u : 0u;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct WINDOW_BUFFER_SIZE_RECORD
    {
        public readonly COORD dwSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct KEY_EVENT_RECORD
    {
        private readonly uint _bKeyDown;
        public bool bKeyDown => _bKeyDown != 0;
        public readonly ushort wRepeatCount;
        public readonly ushort wVirtualKeyCode;
        public readonly ushort wVirtualScanCode;
        private readonly ushort _UnicodeChar;
        public char UnicodeChar => (char)_UnicodeChar;
        public readonly uint dwControlKeyState;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct MOUSE_EVENT_RECORD
    {
        public readonly COORD dwMousePosition;
        public readonly uint dwButtonState;
        public readonly uint dwControlKeyState;
        public readonly uint dwEventFlags;
    }

    [StructLayout(LayoutKind.Explicit)]
    public readonly struct INPUT_RECORD_UNION
    {
        [FieldOffset(0)]
        public readonly KEY_EVENT_RECORD KeyEvent;

        [FieldOffset(0)]
        public readonly MOUSE_EVENT_RECORD MouseEvent;

        [FieldOffset(0)]
        public readonly WINDOW_BUFFER_SIZE_RECORD WindowBufferSizeEvent;
    }

    [StructLayout(LayoutKind.Explicit)]
    public readonly struct INPUT_RECORD
    {
        // WORD EventType;
        [FieldOffset(0)]
        public readonly ushort EventType;

        // union starts at offset 4
        [FieldOffset(4)]
        public readonly INPUT_RECORD_UNION Event;
    }
}
