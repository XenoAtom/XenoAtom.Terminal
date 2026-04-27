// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal.Internal;

internal static class TerminalGraphicsDetector
{
    private static readonly TerminalGraphicsProtocol[] DefaultProtocolOrder =
    [
        TerminalGraphicsProtocol.Kitty,
        TerminalGraphicsProtocol.Sixel,
        TerminalGraphicsProtocol.ITerm2,
    ];

    private static readonly string[] KnownEnvironmentKeys =
    [
        "TERM",
        "TERM_PROGRAM",
        "LC_TERMINAL",
        "KITTY_WINDOW_ID",
        "ITERM_SESSION_ID",
        "WT_SESSION",
        "WT_PROFILE_ID",
        "KONSOLE_VERSION",
        "TMUX",
        "STY",
        "SSH_TTY",
        "SSH_CONNECTION",
        "VSAPPIDNAME",
        "__VSAPPIDDIR",
        "XENOATOM_TERMINAL_GRAPHICS",
        "XENOATOM_TERMINAL_GRAPHICS_PROBING",
        "XENOATOM_TERMINAL_GRAPHICS_PASSTHROUGH",
        "XENOATOM_TERMINAL_CELL_SIZE",
    ];

    public static IReadOnlyDictionary<string, string?> CaptureEnvironment()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in KnownEnvironmentKeys)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrEmpty(value))
            {
                values[key] = value;
            }
        }

        return values;
    }

    public static TerminalGraphicsCapabilities Detect(
        bool ansiEnabled,
        bool isOutputRedirected,
        bool isInputRedirected,
        string? terminalName,
        TerminalSize size,
        TerminalGraphicsOptions options,
        IReadOnlyDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        var diagnostics = new List<string>(6);
        var isMultiplexer = HasValue(environment, "TMUX") || HasValue(environment, "STY");
        var isRemote = HasValue(environment, "SSH_TTY") || HasValue(environment, "SSH_CONNECTION");
        var isVisualStudioHost = HasValue(environment, "VSAPPIDNAME") || HasValue(environment, "__VSAPPIDDIR");
        var passthroughAllowed = options.AllowMultiplexerPassthrough;
        if (TryGet(environment, "XENOATOM_TERMINAL_GRAPHICS_PASSTHROUGH", out var passthroughValue) && TryParseBool(passthroughValue, out var passthroughOverride))
        {
            passthroughAllowed = passthroughOverride;
            diagnostics.Add($"Multiplexer passthrough overridden by environment: {passthroughAllowed}.");
        }

        var probingDisabled = options.DisableProbing;
        if (TryGet(environment, "XENOATOM_TERMINAL_GRAPHICS_PROBING", out var probingValue) && TryParseBool(probingValue, out var probingOverride))
        {
            probingDisabled = !probingOverride;
            diagnostics.Add($"Active probing {(probingDisabled ? "disabled" : "enabled")} by environment.");
        }

        var metrics = options.ForcedPixelMetrics ?? TryParseCellSize(environment, size, diagnostics);

        if (options.DisableGraphics)
        {
            diagnostics.Add("Graphics disabled by TerminalGraphicsOptions.");
            return CreateNone(TerminalGraphicsSupportState.Disabled, "options", terminalName, isMultiplexer, isRemote, metrics, diagnostics);
        }

        if (TryGet(environment, "XENOATOM_TERMINAL_GRAPHICS", out var graphicsOverride) && TryParseProtocolOverride(graphicsOverride, out var overrideProtocol, out var overrideIsAuto))
        {
            if (overrideProtocol == TerminalGraphicsProtocol.None && !overrideIsAuto)
            {
                diagnostics.Add("Graphics disabled by XENOATOM_TERMINAL_GRAPHICS=none.");
                return CreateNone(TerminalGraphicsSupportState.Disabled, "environment", terminalName, isMultiplexer, isRemote, metrics, diagnostics);
            }

            if (!overrideIsAuto && overrideProtocol != TerminalGraphicsProtocol.None)
            {
                diagnostics.Add($"Graphics protocol forced by environment: {overrideProtocol}.");
                return CreateSupported(overrideProtocol, [overrideProtocol], TerminalGraphicsSupportState.Forced, "environment", terminalName, isMultiplexer, isRemote, metrics, diagnostics);
            }
        }

        if (options.PreferredProtocol != TerminalGraphicsProtocol.None)
        {
            diagnostics.Add($"Graphics protocol forced by options: {options.PreferredProtocol}.");
            return CreateSupported(options.PreferredProtocol, [options.PreferredProtocol], TerminalGraphicsSupportState.Forced, "options", terminalName, isMultiplexer, isRemote, metrics, diagnostics);
        }

        if (!ansiEnabled)
        {
            diagnostics.Add("Graphics disabled because ANSI output is not enabled.");
            return CreateNone(TerminalGraphicsSupportState.Unsupported, "backend", terminalName, isMultiplexer, isRemote, metrics, diagnostics);
        }

        if (isOutputRedirected)
        {
            diagnostics.Add("Graphics disabled because output is redirected.");
            return CreateNone(TerminalGraphicsSupportState.Unsupported, "backend", terminalName, isMultiplexer, isRemote, metrics, diagnostics);
        }

        if (isMultiplexer && !passthroughAllowed)
        {
            diagnostics.Add("Graphics disabled because a multiplexer is active and passthrough is disabled.");
            return CreateNone(TerminalGraphicsSupportState.Disabled, "multiplexer", terminalName, isMultiplexer, isRemote, metrics, diagnostics);
        }

        if (!options.AllowHeuristicEnablement)
        {
            diagnostics.Add("Graphics heuristics are disabled and no explicit protocol was forced.");
            return CreateNone(TerminalGraphicsSupportState.Unsupported, "heuristic-disabled", terminalName, isMultiplexer, isRemote, metrics, diagnostics);
        }

        var supported = DetectHeuristicProtocols(environment, terminalName, diagnostics);
        if (supported.Count == 0)
        {
            if (isInputRedirected || probingDisabled)
            {
                diagnostics.Add(isInputRedirected ? "No graphics protocol detected; input is redirected so active probing is unavailable." : "No graphics protocol detected and active probing is disabled.");
            }
            else
            {
                diagnostics.Add("No graphics protocol detected by environment heuristics.");
            }

            if (isVisualStudioHost)
            {
                diagnostics.Add("Visual Studio debug host detected; the process inherits Visual Studio's environment, so WT_SESSION/WT_PROFILE_ID may be absent even when a Windows Terminal window is used. If the visible host supports Sixel, set XENOATOM_TERMINAL_GRAPHICS=sixel for this debug profile.");
            }

            return CreateNone(TerminalGraphicsSupportState.Unsupported, "heuristic", terminalName, isMultiplexer, isRemote, metrics, diagnostics);
        }

        if (isMultiplexer)
        {
            diagnostics.Add("A multiplexer appears active; graphics are enabled because passthrough is allowed.");
        }

        if (isRemote)
        {
            diagnostics.Add("A remote session appears active; graphics detection is based on exported terminal environment variables.");
        }

        var preferred = SelectPreferredProtocol(supported, options.ProtocolOrder);
        diagnostics.Add($"Graphics protocol selected by environment heuristics: {preferred}.");
        return CreateSupported(preferred, supported, TerminalGraphicsSupportState.Heuristic, "heuristic", terminalName, isMultiplexer, isRemote, metrics, diagnostics);
    }

    private static List<TerminalGraphicsProtocol> DetectHeuristicProtocols(IReadOnlyDictionary<string, string?> environment, string? terminalName, List<string> diagnostics)
    {
        var supported = new List<TerminalGraphicsProtocol>(3);
        var term = GetValue(environment, "TERM");
        var termProgram = GetValue(environment, "TERM_PROGRAM");
        var lcTerminal = GetValue(environment, "LC_TERMINAL");
        var combined = string.Join(" ", term, termProgram, lcTerminal, terminalName ?? string.Empty);

        if (HasValue(environment, "KITTY_WINDOW_ID") || ContainsAny(combined, "kitty", "ghostty"))
        {
            AddProtocol(supported, TerminalGraphicsProtocol.Kitty);
            diagnostics.Add("Kitty graphics indicated by terminal environment.");
        }

        if (HasValue(environment, "WT_SESSION") || HasValue(environment, "WT_PROFILE_ID")
            || HasValue(environment, "VSAPPIDNAME") || HasValue(environment, "__VSAPPIDDIR")
            || ContainsAny(combined, "sixel", "windowsterminal", "windows terminal"))
        {
            AddProtocol(supported, TerminalGraphicsProtocol.Sixel);
            diagnostics.Add("Sixel graphics indicated by terminal environment or terminal name.");
            if (HasValue(environment, "VSAPPIDNAME") || HasValue(environment, "__VSAPPIDDIR"))
            {
                diagnostics.Add("Visual Studio debug host detected; Visual Studio-launched terminals can omit WT_SESSION/WT_PROFILE_ID, so Sixel is enabled heuristically. Set XENOATOM_TERMINAL_GRAPHICS=none if this host does not support Sixel.");
            }
        }

        if (HasValue(environment, "ITERM_SESSION_ID") || ContainsAny(combined, "iterm", "wezterm", "mintty", "konsole", "rio"))
        {
            AddProtocol(supported, TerminalGraphicsProtocol.ITerm2);
            diagnostics.Add("iTerm2 inline image protocol indicated by terminal environment.");
        }

        if (ContainsAny(combined, "wezterm"))
        {
            AddProtocol(supported, TerminalGraphicsProtocol.Kitty);
            AddProtocol(supported, TerminalGraphicsProtocol.Sixel);
        }

        if (HasValue(environment, "KONSOLE_VERSION"))
        {
            AddProtocol(supported, TerminalGraphicsProtocol.ITerm2);
        }

        return supported;
    }

    private static TerminalGraphicsProtocol SelectPreferredProtocol(IReadOnlyList<TerminalGraphicsProtocol> supported, IReadOnlyList<TerminalGraphicsProtocol>? protocolOrder)
    {
        var order = protocolOrder is { Count: > 0 } ? protocolOrder : DefaultProtocolOrder;
        foreach (var protocol in order)
        {
            if (protocol != TerminalGraphicsProtocol.None && supported.Contains(protocol))
            {
                return protocol;
            }
        }

        return supported.Count > 0 ? supported[0] : TerminalGraphicsProtocol.None;
    }

    private static TerminalGraphicsCapabilities CreateNone(
        TerminalGraphicsSupportState state,
        string detectionSource,
        string? terminalName,
        bool isMultiplexer,
        bool isRemote,
        TerminalPixelMetrics? metrics,
        List<string> diagnostics) => new()
        {
            PreferredProtocol = TerminalGraphicsProtocol.None,
            SupportedProtocols = Array.Empty<TerminalGraphicsProtocol>(),
            SupportState = state,
            PresentationModel = TerminalGraphicsPresentationModel.None,
            PixelMetrics = metrics,
            DetectionSource = detectionSource,
            TerminalName = terminalName,
            IsMultiplexer = isMultiplexer,
            IsRemoteSession = isRemote,
            Diagnostics = diagnostics.ToArray(),
        };

    private static TerminalGraphicsCapabilities CreateSupported(
        TerminalGraphicsProtocol preferred,
        IReadOnlyList<TerminalGraphicsProtocol> supported,
        TerminalGraphicsSupportState state,
        string detectionSource,
        string? terminalName,
        bool isMultiplexer,
        bool isRemote,
        TerminalPixelMetrics? metrics,
        List<string> diagnostics)
    {
        var caps = GetProtocolShape(preferred);
        return new TerminalGraphicsCapabilities
        {
            PreferredProtocol = preferred,
            SupportedProtocols = supported.ToArray(),
            SupportState = state,
            PresentationModel = caps.PresentationModel,
            SupportsStaticImages = caps.SupportsStaticImages,
            SupportsRealTimeUpdates = caps.SupportsRealTimeUpdates,
            SupportsRetainedImages = caps.SupportsRetainedImages,
            SupportsRetainedPlacements = caps.SupportsRetainedPlacements,
            SupportsDelete = caps.SupportsDelete,
            SupportsMoveOrReplace = caps.SupportsMoveOrReplace,
            SupportsCellPlacement = caps.SupportsCellPlacement,
            SupportsPixelPlacement = caps.SupportsPixelPlacement,
            SupportsTransparency = caps.SupportsTransparency,
            RequiresCellReservation = caps.RequiresCellReservation,
            MaxChunkBytes = caps.MaxChunkBytes,
            MaxRecommendedPayloadBytes = caps.MaxRecommendedPayloadBytes,
            PixelMetrics = metrics,
            DetectionSource = detectionSource,
            TerminalName = terminalName,
            IsMultiplexer = isMultiplexer,
            IsRemoteSession = isRemote,
            Diagnostics = diagnostics.ToArray(),
        };
    }

    private static TerminalGraphicsCapabilities GetProtocolShape(TerminalGraphicsProtocol protocol) => protocol switch
    {
        TerminalGraphicsProtocol.Kitty => new TerminalGraphicsCapabilities
        {
            PresentationModel = TerminalGraphicsPresentationModel.Retained,
            SupportsStaticImages = true,
            SupportsRealTimeUpdates = true,
            SupportsRetainedImages = true,
            SupportsRetainedPlacements = true,
            SupportsDelete = true,
            SupportsMoveOrReplace = true,
            SupportsCellPlacement = true,
            SupportsPixelPlacement = true,
            SupportsTransparency = true,
            RequiresCellReservation = false,
            MaxChunkBytes = 4096,
            MaxRecommendedPayloadBytes = 4_000_000,
        },
        TerminalGraphicsProtocol.ITerm2 => new TerminalGraphicsCapabilities
        {
            PresentationModel = TerminalGraphicsPresentationModel.Streamed,
            SupportsStaticImages = true,
            SupportsRealTimeUpdates = false,
            SupportsRetainedImages = false,
            SupportsRetainedPlacements = false,
            SupportsDelete = false,
            SupportsMoveOrReplace = false,
            SupportsCellPlacement = true,
            SupportsPixelPlacement = true,
            SupportsTransparency = true,
            RequiresCellReservation = true,
            MaxChunkBytes = 0,
            MaxRecommendedPayloadBytes = 2_000_000,
        },
        TerminalGraphicsProtocol.Sixel => new TerminalGraphicsCapabilities
        {
            PresentationModel = TerminalGraphicsPresentationModel.Streamed,
            SupportsStaticImages = true,
            SupportsRealTimeUpdates = true,
            SupportsRetainedImages = false,
            SupportsRetainedPlacements = false,
            SupportsDelete = false,
            SupportsMoveOrReplace = false,
            SupportsCellPlacement = false,
            SupportsPixelPlacement = false,
            SupportsTransparency = false,
            RequiresCellReservation = true,
            MaxChunkBytes = 0,
            MaxRecommendedPayloadBytes = 1_500_000,
        },
        _ => TerminalGraphicsCapabilities.None,
    };

    private static TerminalPixelMetrics? TryParseCellSize(IReadOnlyDictionary<string, string?> environment, TerminalSize size, List<string> diagnostics)
    {
        if (!TryGet(environment, "XENOATOM_TERMINAL_CELL_SIZE", out var value))
        {
            return null;
        }

        var separator = value.IndexOf('x');
        if (separator < 0)
        {
            separator = value.IndexOf('X');
        }

        if (separator <= 0 || separator == value.Length - 1
            || !TryParsePositiveInt(value.AsSpan(0, separator), out var cellWidth)
            || !TryParsePositiveInt(value.AsSpan(separator + 1), out var cellHeight))
        {
            diagnostics.Add("XENOATOM_TERMINAL_CELL_SIZE could not be parsed. Expected format is WIDTHxHEIGHT, for example 9x18.");
            return null;
        }

        var columns = Math.Max(0, size.Columns);
        var rows = Math.Max(0, size.Rows);
        diagnostics.Add($"Cell pixel metrics forced by environment: {cellWidth}x{cellHeight}.");
        return new TerminalPixelMetrics(columns * cellWidth, rows * cellHeight, cellWidth, cellHeight, columns, rows);
    }

    private static void AddProtocol(List<TerminalGraphicsProtocol> protocols, TerminalGraphicsProtocol protocol)
    {
        if (!protocols.Contains(protocol))
        {
            protocols.Add(protocol);
        }
    }

    private static bool TryParseProtocolOverride(string value, out TerminalGraphicsProtocol protocol, out bool isAuto)
    {
        protocol = TerminalGraphicsProtocol.None;
        isAuto = false;
        if (value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            isAuto = true;
            return true;
        }

        if (value.Equals("none", StringComparison.OrdinalIgnoreCase) || value.Equals("off", StringComparison.OrdinalIgnoreCase) || value.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Equals("kitty", StringComparison.OrdinalIgnoreCase))
        {
            protocol = TerminalGraphicsProtocol.Kitty;
            return true;
        }

        if (value.Equals("iterm2", StringComparison.OrdinalIgnoreCase) || value.Equals("iterm", StringComparison.OrdinalIgnoreCase))
        {
            protocol = TerminalGraphicsProtocol.ITerm2;
            return true;
        }

        if (value.Equals("sixel", StringComparison.OrdinalIgnoreCase))
        {
            protocol = TerminalGraphicsProtocol.Sixel;
            return true;
        }

        return false;
    }

    private static bool TryParseBool(string value, out bool result)
    {
        if (value.Equals("1", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (value.Equals("0", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase) || value.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    private static bool TryParsePositiveInt(ReadOnlySpan<char> span, out int value)
    {
        value = 0;
        if (span.IsEmpty)
        {
            return false;
        }

        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            if (c is < '0' or > '9')
            {
                return false;
            }

            var digit = c - '0';
            if (value > (int.MaxValue - digit) / 10)
            {
                return false;
            }

            value = (value * 10) + digit;
        }

        return value > 0;
    }

    private static bool ContainsAny(string value, params ReadOnlySpan<string> needles)
    {
        foreach (var needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGet(IReadOnlyDictionary<string, string?> environment, string key, out string value)
    {
        if (environment.TryGetValue(key, out var nullableValue) && !string.IsNullOrWhiteSpace(nullableValue))
        {
            value = nullableValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool HasValue(IReadOnlyDictionary<string, string?> environment, string key) => TryGet(environment, key, out _);

    private static string GetValue(IReadOnlyDictionary<string, string?> environment, string key) => TryGet(environment, key, out var value) ? value : string.Empty;
}