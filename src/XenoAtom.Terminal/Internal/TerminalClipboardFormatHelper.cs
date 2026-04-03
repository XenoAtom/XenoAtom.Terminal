// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal.Internal;

internal static class TerminalClipboardFormatHelper
{
    public static string Normalize(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        var trimmed = format.Trim();
        var lower = trimmed.ToLowerInvariant();

        return lower switch
        {
            "text/plain" => TerminalClipboardFormats.Text,
            "text/plain;charset=utf-8" => TerminalClipboardFormats.Text,
            "public.utf8-plain-text" => TerminalClipboardFormats.Text,
            "utf8_string" => TerminalClipboardFormats.Text,
            "string" => TerminalClipboardFormats.Text,
            "text" => TerminalClipboardFormats.Text,
            "text/html" => TerminalClipboardFormats.Html,
            "public.html" => TerminalClipboardFormats.Html,
            "html format" => TerminalClipboardFormats.Html,
            "text/rtf" => TerminalClipboardFormats.RichText,
            "public.rtf" => TerminalClipboardFormats.RichText,
            "rich text format" => TerminalClipboardFormats.RichText,
            "image/png" => TerminalClipboardFormats.Png,
            "public.png" => TerminalClipboardFormats.Png,
            "png" => TerminalClipboardFormats.Png,
            "image/tiff" => TerminalClipboardFormats.Tiff,
            "public.tiff" => TerminalClipboardFormats.Tiff,
            "tiff" => TerminalClipboardFormats.Tiff,
            "application/x-win32-cf-dib" => TerminalClipboardFormats.WindowsDeviceIndependentBitmap,
            "application/x-win32-cf-dibv5" => TerminalClipboardFormats.WindowsDeviceIndependentBitmapV5,
            _ => trimmed,
        };
    }

    public static bool IsText(string format) => string.Equals(Normalize(format), TerminalClipboardFormats.Text, StringComparison.Ordinal);
}
