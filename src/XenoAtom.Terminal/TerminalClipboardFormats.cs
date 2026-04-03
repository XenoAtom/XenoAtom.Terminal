// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Well-known clipboard format identifiers used by <see cref="TerminalClipboard" />.
/// </summary>
/// <remarks>
/// <para>
/// The clipboard APIs accept arbitrary format identifiers. These constants provide the normalized cross-platform
/// names that the built-in backends understand directly. Backends may also surface additional platform-specific
/// identifiers via <see cref="TerminalClipboard.GetFormats" />.
/// </para>
/// <para>
/// Image payloads are exposed as the raw bytes stored by the operating system. No image conversion is performed.
/// </para>
/// </remarks>
public static class TerminalClipboardFormats
{
    /// <summary>
    /// UTF-8 plain text.
    /// </summary>
    public const string Text = "text/plain";

    /// <summary>
    /// HTML clipboard content.
    /// </summary>
    public const string Html = "text/html";

    /// <summary>
    /// Rich Text Format clipboard content.
    /// </summary>
    public const string RichText = "text/rtf";

    /// <summary>
    /// PNG image bytes.
    /// </summary>
    public const string Png = "image/png";

    /// <summary>
    /// TIFF image bytes.
    /// </summary>
    public const string Tiff = "image/tiff";

    /// <summary>
    /// Native Windows CF_DIB payload bytes. This is not a BMP file; it is the raw DIB clipboard payload.
    /// </summary>
    public const string WindowsDeviceIndependentBitmap = "application/x-win32-cf-dib";

    /// <summary>
    /// Native Windows CF_DIBV5 payload bytes. This is not a BMP file; it is the raw DIBV5 clipboard payload.
    /// </summary>
    public const string WindowsDeviceIndependentBitmapV5 = "application/x-win32-cf-dibv5";
}
