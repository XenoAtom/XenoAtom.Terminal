// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal.Internal;

internal static class TerminalCapabilitiesExtensions
{
    public static TerminalCapabilities WithExtendedKeys(this TerminalCapabilities capabilities, bool supportsExtendedKeys, TerminalExtendedKeyProtocol extendedKeyProtocol)
    {
        return new TerminalCapabilities
        {
            AnsiEnabled = capabilities.AnsiEnabled,
            ColorLevel = capabilities.ColorLevel,
            SupportsOsc8Links = capabilities.SupportsOsc8Links,
            SupportsAlternateScreen = capabilities.SupportsAlternateScreen,
            SupportsCursorVisibility = capabilities.SupportsCursorVisibility,
            SupportsMouse = capabilities.SupportsMouse,
            SupportsBracketedPaste = capabilities.SupportsBracketedPaste,
            SupportsPrivateModes = capabilities.SupportsPrivateModes,
            SupportsRawMode = capabilities.SupportsRawMode,
            SupportsCursorPositionGet = capabilities.SupportsCursorPositionGet,
            SupportsCursorPositionSet = capabilities.SupportsCursorPositionSet,
            SupportsClipboard = capabilities.SupportsClipboard,
            SupportsClipboardGet = capabilities.SupportsClipboardGet,
            SupportsClipboardSet = capabilities.SupportsClipboardSet,
            SupportsClipboardFormatsGet = capabilities.SupportsClipboardFormatsGet,
            SupportsClipboardFormatsSet = capabilities.SupportsClipboardFormatsSet,
            SupportsOsc52Clipboard = capabilities.SupportsOsc52Clipboard,
            SupportsTitleGet = capabilities.SupportsTitleGet,
            SupportsTitleSet = capabilities.SupportsTitleSet,
            SupportsWindowSize = capabilities.SupportsWindowSize,
            SupportsWindowSizeSet = capabilities.SupportsWindowSizeSet,
            SupportsBufferSize = capabilities.SupportsBufferSize,
            SupportsBufferSizeSet = capabilities.SupportsBufferSizeSet,
            SupportsBeep = capabilities.SupportsBeep,
            IsOutputRedirected = capabilities.IsOutputRedirected,
            IsInputRedirected = capabilities.IsInputRedirected,
            TerminalName = capabilities.TerminalName,
            Graphics = capabilities.Graphics,
            SupportsExtendedKeys = supportsExtendedKeys,
            ExtendedKeyProtocol = extendedKeyProtocol,
        };
    }
}
