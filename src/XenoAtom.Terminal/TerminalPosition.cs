// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Represents a 0-based cursor position in character cells.
/// </summary>
public readonly record struct TerminalPosition(int Column, int Row);

