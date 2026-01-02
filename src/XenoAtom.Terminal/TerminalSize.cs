// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Represents a terminal size in columns and rows.
/// </summary>
/// <param name="Columns">The number of columns (width).</param>
/// <param name="Rows">The number of rows (height).</param>
public readonly record struct TerminalSize(int Columns, int Rows);
