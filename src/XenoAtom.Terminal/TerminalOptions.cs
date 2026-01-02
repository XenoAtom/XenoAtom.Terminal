// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Options controlling terminal behavior.
/// </summary>
public sealed class TerminalOptions
{
    /// <summary>
    /// When enabled, unsupported operations throw instead of becoming a no-op.
    /// </summary>
    public bool StrictMode { get; set; }

    /// <summary>
    /// Forces ANSI output even if the output appears redirected (use with care).
    /// </summary>
    public bool ForceAnsi { get; set; }

    /// <summary>
    /// Respects the NO_COLOR environment variable by disabling colors (not cursor control).
    /// </summary>
    public bool RespectNoColor { get; set; } = true;

    /// <summary>
    /// When enabled, <see cref="Terminal.ReadEventsAsync(System.Threading.CancellationToken)"/> starts input implicitly.
    /// </summary>
    public bool ImplicitStartInput { get; set; } = true;

    /// <summary>
    /// When enabled, Ctrl+C / Ctrl+Break are treated as regular key input (best effort) instead of terminal signals.
    /// </summary>
    /// <remarks>
    /// This is similar to <see cref="Console.TreatControlCAsInput"/>.
    /// </remarks>
    public bool TreatControlCAsInput { get; set; }

    /// <summary>
    /// Requests setting output encoding to UTF-8 when possible.
    /// </summary>
    public bool PreferUtf8Output { get; set; } = true;

    /// <summary>
    /// Emits 7-bit escape sequences (ESC [) instead of 8-bit C1 control codes.
    /// </summary>
    public bool Prefer7BitC1 { get; set; } = true;

    /// <summary>
    /// Preferred color capability when ANSI is enabled.
    /// </summary>
    public TerminalColorLevel PreferredColorLevel { get; set; } = TerminalColorLevel.TrueColor;
}

