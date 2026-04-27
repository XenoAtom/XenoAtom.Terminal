// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Describes terminal pixel and cell metrics used to translate cell layout to image pixel sizes.
/// </summary>
/// <param name="WindowPixelWidth">The terminal window width in pixels.</param>
/// <param name="WindowPixelHeight">The terminal window height in pixels.</param>
/// <param name="CellPixelWidth">The cell width in pixels.</param>
/// <param name="CellPixelHeight">The cell height in pixels.</param>
/// <param name="Columns">The terminal width in cells.</param>
/// <param name="Rows">The terminal height in cells.</param>
public readonly record struct TerminalPixelMetrics(int WindowPixelWidth, int WindowPixelHeight, int CellPixelWidth, int CellPixelHeight, int Columns, int Rows);