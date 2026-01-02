// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal.Internal;

internal static class TerminalKeyModifierNormalization
{
    public static TerminalModifiers NormalizeModifiersForPortableTextKeys(TerminalKey key, char? ch, TerminalModifiers modifiers)
    {
        if (ch is null)
        {
            return modifiers;
        }

        // Unix terminals generally do not provide "Shift" as a reliable modifier for text input.
        // Shift is typically encoded in the resulting character (e.g. 'a' vs 'A', '1' vs '!').
        // Keep Shift for non-text keys (e.g. Shift+Tab, Shift+arrows) where some terminals do report it.
        if (key is TerminalKey.Unknown or TerminalKey.Space)
        {
            modifiers &= ~TerminalModifiers.Shift;
        }

        return modifiers;
    }
}

