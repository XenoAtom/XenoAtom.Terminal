// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Ansi;
using XenoAtom.Terminal.Backends;

namespace XenoAtom.Terminal.Tests;

[TestClass]
public class TerminalMarkupStylesTests
{
    [TestCleanup]
    public void Cleanup()
    {
        Terminal.ResetForTests();
    }

    [TestMethod]
    public void WriteMarkup_UsesCustomStylesFromTerminalInstance()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        Terminal.SetMarkupStyle("primary", AnsiStyle.Default with { Decorations = AnsiDecorations.Underline });
        Terminal.WriteMarkup("[primary]Hi[/]");

        var text = backend.GetOutText();
        Assert.IsTrue(text.Contains("Hi", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("\x1b[4", StringComparison.Ordinal));
    }

    [TestMethod]
    public void WriteMarkup_RebuildsMarkup_WhenStylesChange()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        Terminal.SetMarkupStyle("primary", AnsiStyle.Default with { Decorations = AnsiDecorations.Underline });
        Terminal.WriteMarkup("[primary]Hi[/]");
        var before = backend.GetOutText();

        Terminal.SetMarkupStyle("primary", AnsiStyle.Default with { Foreground = (AnsiColor)ConsoleColor.Red });
        Terminal.WriteMarkup("[primary]Hi[/]");
        var after = backend.GetOutText();

        Assert.IsGreaterThan(before.Length, after.Length);
        var appended = after.Substring(before.Length);
        Assert.IsTrue(appended.Contains("\x1b[91", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MarkupStyles_InPlaceMutation_CanBeNotified()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        var styles = new Dictionary<string, AnsiStyle>
        {
            ["primary"] = AnsiStyle.Default with { Decorations = AnsiDecorations.Underline },
        };

        Terminal.MarkupStyles = styles;
        Terminal.WriteMarkup("[primary]Hi[/]");
        var before = backend.GetOutText();
        Assert.IsTrue(before.Contains("\x1b[4", StringComparison.Ordinal));

        styles["primary"] = AnsiStyle.Default with { Foreground = (AnsiColor)ConsoleColor.Green };
        Terminal.NotifyMarkupStylesChanged();

        Terminal.WriteMarkup("[primary]Hi[/]");
        var after = backend.GetOutText();
        var appended = after.Substring(before.Length);
        Assert.IsTrue(appended.Contains("\x1b[92", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CaptureOutput_UsesCustomStyles()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        Terminal.SetMarkupStyle("primary", AnsiStyle.Default with { Decorations = AnsiDecorations.Underline });

        using var builder = new AnsiBuilder();
        using (Terminal.Instance.CaptureOutput(builder))
        {
            Terminal.WriteMarkup("[primary]Hi[/]");
        }

        Assert.IsTrue(builder.ToString().Contains("\x1b[4", StringComparison.Ordinal));
    }
}
