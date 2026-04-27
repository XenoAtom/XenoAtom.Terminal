// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Ansi;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.Internal;

namespace XenoAtom.Terminal.Tests;

[TestClass]
public class TerminalGraphicsDetectionTests
{
    [TestCleanup]
    public void Cleanup()
    {
        Terminal.ResetForTests();
    }

    [TestMethod]
    public void Detect_KittyEnvironment_SelectsKittyRetainedCapabilities()
    {
        var capabilities = Detect(
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["TERM"] = "xterm-kitty",
                ["KITTY_WINDOW_ID"] = "1",
            });

        Assert.AreEqual(TerminalGraphicsProtocol.Kitty, capabilities.PreferredProtocol);
        Assert.AreEqual(TerminalGraphicsSupportState.Heuristic, capabilities.SupportState);
        Assert.AreEqual(TerminalGraphicsPresentationModel.Retained, capabilities.PresentationModel);
        Assert.IsTrue(capabilities.SupportsRetainedImages);
        Assert.IsTrue(capabilities.SupportsDelete);
        CollectionAssert.Contains(capabilities.SupportedProtocols.ToArray(), TerminalGraphicsProtocol.Kitty);
    }

    [TestMethod]
    public void Detect_WindowsTerminalEnvironment_SelectsSixelAndCellMetrics()
    {
        var capabilities = Detect(
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["WT_SESSION"] = "session",
                ["XENOATOM_TERMINAL_CELL_SIZE"] = "9x18",
            },
            size: new TerminalSize(80, 25));

        Assert.AreEqual(TerminalGraphicsProtocol.Sixel, capabilities.PreferredProtocol);
        Assert.AreEqual(TerminalGraphicsPresentationModel.Streamed, capabilities.PresentationModel);
        Assert.IsTrue(capabilities.RequiresCellReservation);
        Assert.AreEqual(new TerminalPixelMetrics(720, 450, 9, 18, 80, 25), capabilities.PixelMetrics);
    }

    [TestMethod]
    public void Detect_WindowsTerminalName_SelectsSixelWhenEnvironmentVariablesAreMissing()
    {
        var capabilities = Detect(
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
            terminalName: "WindowsTerminal");

        Assert.AreEqual(TerminalGraphicsProtocol.Sixel, capabilities.PreferredProtocol);
        Assert.AreEqual(TerminalGraphicsSupportState.Heuristic, capabilities.SupportState);
        Assert.AreEqual(TerminalGraphicsPresentationModel.Streamed, capabilities.PresentationModel);
    }

    [TestMethod]
    public void Detect_WindowsTerminalProfileId_SelectsSixelWhenSessionIdIsMissing()
    {
        var capabilities = Detect(
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["WT_PROFILE_ID"] = "profile",
            });

        Assert.AreEqual(TerminalGraphicsProtocol.Sixel, capabilities.PreferredProtocol);
        Assert.AreEqual(TerminalGraphicsSupportState.Heuristic, capabilities.SupportState);
        StringAssert.Contains(string.Join(' ', capabilities.Diagnostics), "Sixel");
    }

    [TestMethod]
    public void Detect_VisualStudioHostWithoutTerminalSignal_SelectsSixelWithDiagnostic()
    {
        var capabilities = Detect(
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["VSAPPIDNAME"] = "devenv.exe",
            },
            terminalName: "VisualStudio");

        Assert.AreEqual(TerminalGraphicsProtocol.Sixel, capabilities.PreferredProtocol);
        Assert.AreEqual(TerminalGraphicsSupportState.Heuristic, capabilities.SupportState);
        StringAssert.Contains(string.Join(' ', capabilities.Diagnostics), "Visual Studio debug host detected");
        StringAssert.Contains(string.Join(' ', capabilities.Diagnostics), "XENOATOM_TERMINAL_GRAPHICS=none");
    }

    [TestMethod]
    public void Detect_ExplicitOption_ForcesProtocolEvenWhenRedirected()
    {
        var options = new TerminalGraphicsOptions { PreferredProtocol = TerminalGraphicsProtocol.ITerm2 };

        var capabilities = Detect(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase), options, isOutputRedirected: true);

        Assert.AreEqual(TerminalGraphicsProtocol.ITerm2, capabilities.PreferredProtocol);
        Assert.AreEqual(TerminalGraphicsSupportState.Forced, capabilities.SupportState);
        Assert.AreEqual("options", capabilities.DetectionSource);
    }

    [TestMethod]
    public void Detect_EnvironmentNone_DisablesGraphics()
    {
        var capabilities = Detect(
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["XENOATOM_TERMINAL_GRAPHICS"] = "none",
            });

        Assert.AreEqual(TerminalGraphicsProtocol.None, capabilities.PreferredProtocol);
        Assert.AreEqual(TerminalGraphicsSupportState.Disabled, capabilities.SupportState);
    }

    [TestMethod]
    public void Detect_MultiplexerWithPassthroughDisabled_DisablesGraphics()
    {
        var options = new TerminalGraphicsOptions { AllowMultiplexerPassthrough = false };
        var capabilities = Detect(
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["TMUX"] = "1",
                ["KITTY_WINDOW_ID"] = "1",
            },
            options);

        Assert.AreEqual(TerminalGraphicsProtocol.None, capabilities.PreferredProtocol);
        Assert.AreEqual(TerminalGraphicsSupportState.Disabled, capabilities.SupportState);
        Assert.IsTrue(capabilities.IsMultiplexer);
    }

    [TestMethod]
    public void Detect_MultiplexerWithPassthroughAllowed_KeepsProtocolAndReportsDiagnostic()
    {
        var capabilities = Detect(
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["TMUX"] = "1",
                ["KITTY_WINDOW_ID"] = "1",
            });

        Assert.AreEqual(TerminalGraphicsProtocol.Kitty, capabilities.PreferredProtocol);
        Assert.IsTrue(capabilities.IsMultiplexer);
        StringAssert.Contains(string.Join(' ', capabilities.Diagnostics), "multiplexer appears active");
    }

    [TestMethod]
    public void Detect_RemoteSession_KeepsHeuristicProtocolAndReportsDiagnostic()
    {
        var capabilities = Detect(
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["SSH_TTY"] = "/dev/pts/1",
                ["WT_PROFILE_ID"] = "profile",
            });

        Assert.AreEqual(TerminalGraphicsProtocol.Sixel, capabilities.PreferredProtocol);
        Assert.IsTrue(capabilities.IsRemoteSession);
        StringAssert.Contains(string.Join(' ', capabilities.Diagnostics), "remote session appears active");
    }

    [TestMethod]
    public void Detect_ProtocolOrder_OverridesDefaultPreference()
    {
        var options = new TerminalGraphicsOptions
        {
            ProtocolOrder = [TerminalGraphicsProtocol.Sixel, TerminalGraphicsProtocol.Kitty],
        };

        var capabilities = Detect(
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["TERM"] = "xterm-kitty-sixel",
                ["KITTY_WINDOW_ID"] = "1",
            },
            options);

        Assert.AreEqual(TerminalGraphicsProtocol.Sixel, capabilities.PreferredProtocol);
    }

    [TestMethod]
    public void TerminalInstance_GraphicsService_ExposesDetectedCapabilities()
    {
        var backend = new InMemoryTerminalBackend(capabilities: CreateCapabilities("xterm-kitty"));
        Terminal.Initialize(backend);

        Assert.AreEqual(TerminalGraphicsProtocol.Kitty, Terminal.Graphics.Capabilities.PreferredProtocol);
        Assert.AreSame(Terminal.Graphics, Terminal.Instance.Graphics);
    }

    [TestMethod]
    public async Task TerminalGraphicsService_ReturnsKnownCapabilitiesAndMetrics()
    {
        var metrics = new TerminalPixelMetrics(800, 600, 10, 20, 80, 30);
        var options = new TerminalOptions();
        options.Graphics.ForcedPixelMetrics = metrics;
        options.Graphics.PreferredProtocol = TerminalGraphicsProtocol.Kitty;

        Terminal.Initialize(new InMemoryTerminalBackend(), options);

        var capabilities = await Terminal.Graphics.RefreshCapabilitiesAsync();
        var queriedMetrics = await Terminal.Graphics.QueryPixelMetricsAsync();

        Assert.AreEqual(TerminalGraphicsProtocol.Kitty, capabilities.PreferredProtocol);
        Assert.AreEqual(metrics, queriedMetrics);
    }

    [TestMethod]
    public async Task TerminalGraphicsService_QueryPixelMetricsAsync_WritesProbeSequence()
    {
        var output = new StringWriter();
        var options = new TerminalOptions();
        options.Graphics.ProbeTimeout = TimeSpan.FromMilliseconds(1);
        Terminal.Initialize(new VirtualTerminalBackend(outWriter: output, errorWriter: TextWriter.Null), options);

        var metrics = await Terminal.Graphics.QueryPixelMetricsAsync();

        Assert.IsNull(metrics);
        Assert.AreEqual("\u001b[14t\u001b[16t\u001b[18t", output.ToString());
    }

    [TestMethod]
    public async Task TerminalGraphicsService_QueryPixelMetricsAsync_DoesNotProbeWhenImplicitInputDisabled()
    {
        var output = new StringWriter();
        Terminal.Initialize(
            new VirtualTerminalBackend(outWriter: output, errorWriter: TextWriter.Null),
            new TerminalOptions { ImplicitStartInput = false });

        var metrics = await Terminal.Graphics.QueryPixelMetricsAsync();

        Assert.IsNull(metrics);
        Assert.AreEqual(string.Empty, output.ToString());
    }

    [TestMethod]
    public void ProbeCoordinator_ConsumesKittyReplyAndPixelMetrics()
    {
        using var tokenizer = new AnsiTokenizer();
        var coordinator = new TerminalGraphicsProbeCoordinator();
        var tokens = tokenizer.Tokenize("\u001b_Gi=42,p=7;OK\u001b\\\u001b[4;600;800t\u001b[6;18;9t\u001b[8;30;80t".AsSpan());

        foreach (var token in tokens)
        {
            Assert.IsTrue(coordinator.TryConsume(token));
        }

        Assert.AreEqual(42, coordinator.LastKittyReply?.ImageId);
        Assert.AreEqual(7, coordinator.LastKittyReply?.PlacementId);
        Assert.AreEqual("OK", coordinator.LastKittyReply?.Status);
        Assert.AreEqual(new TerminalPixelMetrics(800, 600, 9, 18, 80, 30), coordinator.LastPixelMetrics);
    }

    [TestMethod]
    public void VtInputDecoder_ConsumesProbeRepliesBeforePublishingInputEvents()
    {
        using var decoder = new VtInputDecoder();
        using var events = new TerminalEventBroadcaster();
        var coordinator = new TerminalGraphicsProbeCoordinator();

        decoder.Decode("\u001b_Gi=1;OK\u001b\\x".AsSpan(), isFinalChunk: true, options: null, events, graphicsProbeCoordinator: coordinator);

        Assert.AreEqual(1, coordinator.LastKittyReply?.ImageId);
        Assert.IsTrue(events.TryReadEvent(out var ev));
        Assert.AreEqual('x', ((TerminalKeyEvent)ev).Char);
    }

    private static TerminalGraphicsCapabilities Detect(
        IReadOnlyDictionary<string, string?> environment,
        TerminalGraphicsOptions? options = null,
        bool ansiEnabled = true,
        bool isOutputRedirected = false,
        bool isInputRedirected = false,
        TerminalSize size = default,
        string? terminalName = null) =>
        TerminalGraphicsDetector.Detect(
            ansiEnabled,
            isOutputRedirected,
            isInputRedirected,
            terminalName,
            size,
            options ?? new TerminalGraphicsOptions(),
            environment);

    private static TerminalCapabilities CreateCapabilities(string terminalName) => new()
    {
        AnsiEnabled = true,
        ColorLevel = TerminalColorLevel.TrueColor,
        IsOutputRedirected = false,
        IsInputRedirected = false,
        TerminalName = terminalName,
        SupportsOsc8Links = true,
        SupportsPrivateModes = true,
        SupportsCursorPositionGet = true,
        SupportsCursorPositionSet = true,
    };
}