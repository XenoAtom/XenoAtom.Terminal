// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Base type for all terminal input events.
/// </summary>
public abstract record TerminalEvent
{
    /// <summary>
    /// Gets the timestamp associated with this event (UTC by default).
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
