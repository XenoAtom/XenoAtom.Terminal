// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Threading;
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

    [TestMethod]
    public void UseInstance_NullInstance_Throws()
    {
        var ex = Assert.ThrowsExactly<ArgumentNullException>(() => Terminal.UseInstance(null!));
        Assert.AreEqual("instance", ex.ParamName);
    }

    [TestMethod]
    public void UseInstance_RedirectsStaticFacadeOnCurrentThread()
    {
        var globalBackend = new InMemoryTerminalBackend();
        var global = Terminal.Initialize(globalBackend);
        var backend = new InMemoryTerminalBackend();
        using var terminal = new TerminalInstance(backend);

        using (Terminal.UseInstance(terminal))
        {
            Assert.AreSame(terminal, Terminal.Instance);
            Assert.AreSame(terminal, Terminal.T);
            Assert.AreSame(backend, Terminal.Backend);
            Terminal.Write("ambient");
        }

        Assert.AreSame(global, Terminal.Instance);
        Terminal.Write("global");

        Assert.AreEqual("ambient", backend.GetOutText());
        Assert.AreEqual("global", globalBackend.GetOutText());
    }

    [TestMethod]
    public void UseInstance_DoesNotInitializeGlobal()
    {
        var backend = new InMemoryTerminalBackend();
        using var terminal = new TerminalInstance(backend);

        using (Terminal.UseInstance(terminal))
        {
            Terminal.WriteLine("hello");
            Assert.IsFalse(Terminal.IsInitialized);
        }

        Assert.IsFalse(Terminal.IsInitialized);
        Assert.IsTrue(backend.GetOutText().Contains("hello", StringComparison.Ordinal));
    }

    [TestMethod]
    public void UseInstance_NestedScopes_RestoreInReverseOrder()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var terminal1 = new TerminalInstance(new InMemoryTerminalBackend());
        using var terminal2 = new TerminalInstance(new InMemoryTerminalBackend());

        var scope1 = Terminal.UseInstance(terminal1);
        Assert.AreSame(terminal1, Terminal.Instance);

        var scope2 = Terminal.UseInstance(terminal2);
        Assert.AreSame(terminal2, Terminal.Instance);

        scope2.Dispose();
        Assert.AreSame(terminal1, Terminal.Instance);

        scope1.Dispose();
        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    public void UseInstance_SameInstanceNested_RestoresOnlyOnOuterDispose()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var terminal = new TerminalInstance(new InMemoryTerminalBackend());

        using (Terminal.UseInstance(terminal))
        {
            using (Terminal.UseInstance(terminal))
            {
                Assert.AreSame(terminal, Terminal.Instance);
            }

            Assert.AreSame(terminal, Terminal.Instance);
        }

        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    public void UseInstance_IsNotVisibleFromOtherThreads()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var terminal = new TerminalInstance(new InMemoryTerminalBackend());

        TerminalInstance? seenFromOtherThread = null;
        var mainThreadId = Environment.CurrentManagedThreadId;
        var otherThreadId = mainThreadId;

        using (Terminal.UseInstance(terminal))
        {
            var thread = new Thread(() =>
            {
                otherThreadId = Environment.CurrentManagedThreadId;
                seenFromOtherThread = Terminal.Instance;
            });
            thread.Start();
            thread.Join();

            Assert.AreSame(terminal, Terminal.Instance);
        }

        Assert.AreNotEqual(mainThreadId, otherThreadId);
        Assert.AreSame(global, seenFromOtherThread);
    }

    [TestMethod]
    public void UseInstance_OnOtherThread_DoesNotAffectCurrentThread()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        var otherBackend = new InMemoryTerminalBackend();
        using var otherTerminal = new TerminalInstance(otherBackend);

        TerminalInstance? seenInsideScope = null;
        TerminalInstance? seenAfterScope = null;
        using var scopeEntered = new ManualResetEventSlim();
        using var releaseScope = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            using (Terminal.UseInstance(otherTerminal))
            {
                seenInsideScope = Terminal.Instance;
                Terminal.Write("other");
                scopeEntered.Set();
                releaseScope.Wait();
            }

            seenAfterScope = Terminal.Instance;
        });
        thread.Start();

        Assert.IsTrue(scopeEntered.Wait(TimeSpan.FromSeconds(10)));
        // The other thread is inside its scope: this thread still targets the global instance.
        Assert.AreSame(global, Terminal.Instance);
        releaseScope.Set();
        thread.Join();

        Assert.AreSame(otherTerminal, seenInsideScope);
        Assert.AreSame(global, seenAfterScope);
        Assert.AreEqual("other", otherBackend.GetOutText());
    }

    [TestMethod]
    public void UseInstance_DisposeRestoresPreviousAmbient()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var terminal = new TerminalInstance(new InMemoryTerminalBackend());

        var scope = Terminal.UseInstance(terminal);
        Assert.IsFalse(scope.IsEmpty);
        Assert.AreSame(terminal, Terminal.Instance);

        scope.Dispose();

        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    public void UseInstance_DisposeTwice_IsNoOp()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var terminal1 = new TerminalInstance(new InMemoryTerminalBackend());
        using var terminal2 = new TerminalInstance(new InMemoryTerminalBackend());

        var scope1 = Terminal.UseInstance(terminal1);
        scope1.Dispose();
        Assert.AreSame(global, Terminal.Instance);

        using (Terminal.UseInstance(terminal2))
        {
            // A second dispose must not restore the ambient captured by scope1 again.
            scope1.Dispose();
            Assert.AreSame(terminal2, Terminal.Instance);
        }

        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    public void UseInstance_DisposeOnOtherThread_DoesNothing()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var terminal = new TerminalInstance(new InMemoryTerminalBackend());

        var scope = Terminal.UseInstance(terminal);

        TerminalInstance? seenFromOtherThread = null;
        var thread = new Thread(() =>
        {
            scope.Dispose();
            seenFromOtherThread = Terminal.Instance;
        });
        thread.Start();
        thread.Join();

        Assert.AreSame(global, seenFromOtherThread);
        Assert.AreSame(terminal, Terminal.Instance);

        // The ignored dispose did not consume the scope: disposing on the owning thread still restores.
        scope.Dispose();
        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    public void UseInstance_GlobalInitializeAndClose_KeepGlobalSemantics()
    {
        var backend = new InMemoryTerminalBackend();
        using var terminal = new TerminalInstance(backend);

        using (Terminal.UseInstance(terminal))
        {
            Assert.IsFalse(Terminal.IsInitialized);

            var global = Terminal.Initialize(new InMemoryTerminalBackend());
            Assert.AreNotSame(terminal, global);
            Assert.IsTrue(Terminal.IsInitialized);
            Assert.AreSame(terminal, Terminal.Instance);

            Terminal.Close();
            Assert.IsFalse(Terminal.IsInitialized);
            Assert.AreSame(terminal, Terminal.Instance);

            // Closing the global instance must not dispose the ambient one.
            Terminal.Write("still alive");
        }

        Assert.AreEqual("still alive", backend.GetOutText());
        Assert.IsFalse(Terminal.IsInitialized);
    }
}
