// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Terminal.Backends;

namespace XenoAtom.Terminal.Tests;

[TestClass]
public class TerminalInstanceStandaloneTests
{
    [TestInitialize]
    public void Initialize()
    {
        Terminal.ResetForTests();
    }

    [TestCleanup]
    public void Cleanup()
    {
        Terminal.ResetForTests();
    }

    [TestMethod]
    public void Constructor_CreatesInitializedInstance_WithoutInitializingGlobal()
    {
        var backend = new InMemoryTerminalBackend();

        using var terminal = new TerminalInstance(backend);

        Assert.IsTrue(terminal.IsInitialized);
        Assert.AreSame(backend, terminal.Backend);
        Assert.IsNotNull(terminal.Options);
        Assert.IsFalse(Terminal.IsInitialized);
    }

    [TestMethod]
    public void Constructor_WithOptions_UsesOptions_WithoutInitializingGlobal()
    {
        var backend = new InMemoryTerminalBackend();
        var options = new TerminalOptions { ForceAnsi = true };

        using var terminal = new TerminalInstance(backend, options);
        terminal.WriteMarkup("[red]Hi[/]");

        Assert.IsTrue(terminal.IsInitialized);
        Assert.AreSame(options, terminal.Options);
        Assert.IsTrue(backend.GetOutText().Contains("\x1b[", StringComparison.Ordinal));
        Assert.IsFalse(Terminal.IsInitialized);
    }

    [TestMethod]
    public void Constructor_DoesNotReplaceInitializedGlobal()
    {
        var globalBackend = new InMemoryTerminalBackend();
        var global = Terminal.Initialize(globalBackend);

        var terminal = new TerminalInstance(new InMemoryTerminalBackend());
        Assert.AreSame(global, Terminal.Instance);

        terminal.Dispose();

        Assert.IsTrue(Terminal.IsInitialized);
        Assert.AreSame(global, Terminal.Instance);
        Terminal.Write("global");
        Assert.AreEqual("global", globalBackend.GetOutText());
    }

    [TestMethod]
    public void TwoStandaloneInstances_WriteIndependently()
    {
        var backend1 = new InMemoryTerminalBackend();
        var backend2 = new InMemoryTerminalBackend();
        using var terminal1 = new TerminalInstance(backend1);
        using var terminal2 = new TerminalInstance(backend2);

        terminal1.Write("alpha");
        terminal2.Write("beta");

        Assert.AreEqual("alpha", backend1.GetOutText());
        Assert.AreEqual("beta", backend2.GetOutText());
        Assert.IsFalse(Terminal.IsInitialized);
    }

    [TestMethod]
    public void Dispose_DisposesBackend()
    {
        var backend = new InMemoryTerminalBackend();
        var terminal = new TerminalInstance(backend);

        terminal.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => backend.PushEvent(new TerminalResizeEvent { Size = new TerminalSize(10, 10) }));
        Assert.ThrowsExactly<ObjectDisposedException>(() => backend.StartInput(new TerminalInputOptions()));
        Assert.IsFalse(Terminal.IsInitialized);
    }

    [TestMethod]
    public void Constructor_NullArguments_Throw()
    {
        var ex1 = Assert.ThrowsExactly<ArgumentNullException>(() => new TerminalInstance(null!));
        Assert.AreEqual("backend", ex1.ParamName);

        var ex2 = Assert.ThrowsExactly<ArgumentNullException>(() => new TerminalInstance(null!, new TerminalOptions()));
        Assert.AreEqual("backend", ex2.ParamName);

        var ex3 = Assert.ThrowsExactly<ArgumentNullException>(() => new TerminalInstance(new InMemoryTerminalBackend(), null!));
        Assert.AreEqual("options", ex3.ParamName);

        Assert.IsFalse(Terminal.IsInitialized);
    }
}
