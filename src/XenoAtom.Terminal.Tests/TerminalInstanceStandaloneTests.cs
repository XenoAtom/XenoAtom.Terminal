// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Threading;
using XenoAtom.Ansi;
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
    public void Constructor_WhenBackendInitializeFails_DisposesBackend()
    {
        // A disposed backend makes its Initialize throw: this is the failure path of the constructor.
        // The instance owns the backend from the call on, so a failed construction must not leak it.
        var backend1 = new DisposeCountingBackend();
        backend1.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => new TerminalInstance(backend1));
        // Once by the test, once by the failed constructor.
        Assert.AreEqual(2, backend1.DisposeCount);

        var backend2 = new DisposeCountingBackend();
        backend2.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => new TerminalInstance(backend2, new TerminalOptions()));
        Assert.AreEqual(2, backend2.DisposeCount);

        Assert.IsFalse(Terminal.IsInitialized);
    }

    [TestMethod]
    public void Constructor_WithNullOptions_DoesNotTakeOwnershipOfBackend()
    {
        // Argument validation happens before ownership is taken: the caller still owns the backend.
        var backend = new DisposeCountingBackend();

        Assert.ThrowsExactly<ArgumentNullException>(() => new TerminalInstance(backend, null!));
        Assert.AreEqual(0, backend.DisposeCount);
        backend.Dispose();
        Assert.AreEqual(1, backend.DisposeCount);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void GlobalInitialize_WhenBackendInitializeFails_DisposesBackendAndAllowsRetry(bool replaceGlobal)
    {
        if (replaceGlobal)
        {
            Terminal.Initialize(new InMemoryTerminalBackend());
        }

        var backend = new DisposeCountingBackend();
        backend.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => Terminal.Initialize(backend, force: true));
        Assert.AreEqual(2, backend.DisposeCount);
        Assert.IsFalse(Terminal.IsInitialized);

        var replacement = Terminal.Initialize(new InMemoryTerminalBackend());
        Assert.AreSame(replacement, Terminal.Instance);
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

        try
        {
            Assert.IsTrue(scopeEntered.Wait(TimeSpan.FromSeconds(10)));
            // The other thread is inside its scope: this thread still targets the global instance.
            Assert.AreSame(global, Terminal.Instance);
        }
        finally
        {
            // Always release the other thread, otherwise a failed assertion would leave it blocked and hang the test host.
            releaseScope.Set();
            thread.Join();
        }

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
    public void UseInstance_DisposeOnOtherThread_ThrowsWithoutConsumingScope()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var terminal = new TerminalInstance(new InMemoryTerminalBackend());

        var scope = Terminal.UseInstance(terminal);

        TerminalInstance? seenFromOtherThread = null;
        Exception? disposeError = null;
        var thread = new Thread(() =>
        {
            try
            {
                scope.Dispose();
            }
            catch (Exception exception)
            {
                disposeError = exception;
            }

            seenFromOtherThread = Terminal.Instance;
        });
        thread.Start();
        thread.Join();

        try
        {
            Assert.IsInstanceOfType<InvalidOperationException>(disposeError);
            Assert.AreSame(global, seenFromOtherThread);
            Assert.AreSame(terminal, Terminal.Instance);
        }
        finally
        {
            scope.Dispose();
        }

        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UseInstance_DisposeOutOfOrder_ThrowsWithoutConsumingScope(bool sameInstance)
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var first = new TerminalInstance(new InMemoryTerminalBackend());
        using var second = new TerminalInstance(new InMemoryTerminalBackend());
        var inner = sameInstance ? first : second;
        var outerScope = Terminal.UseInstance(first);
        var innerScope = Terminal.UseInstance(inner);

        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => outerScope.Dispose());
            Assert.AreSame(inner, Terminal.Instance);
        }
        finally
        {
            innerScope.Dispose();
            outerScope.Dispose();
        }

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

    [TestMethod]
    public void UseInstance_DisposedInstance_ThrowsWithoutChangingAmbient()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var active = new TerminalInstance(new InMemoryTerminalBackend());
        var disposed = new TerminalInstance(new InMemoryTerminalBackend());
        disposed.Dispose();

        using (Terminal.UseInstance(active))
        {
            Assert.ThrowsExactly<ObjectDisposedException>(() => Terminal.UseInstance(disposed));
            Assert.AreSame(active, Terminal.Instance);
        }

        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    public void ProcessExit_DisposesStandaloneInstancesThatAreStillAlive()
    {
        // Never disposed by the test: the process-exit path must dispose it.
        var alive = new DisposeCountingBackend();
        _ = new TerminalInstance(alive);

        // Disposed by the test: the process-exit path must not dispose it again.
        var disposed = new DisposeCountingBackend();
        new TerminalInstance(disposed).Dispose();

        Terminal.DisposeInstancesForProcessExit();

        Assert.AreEqual(1, alive.DisposeCount);
        Assert.AreEqual(1, disposed.DisposeCount);
    }

    [TestMethod]
    public void CaptureOutput_OnlyCapturesOutputOfItsOwnInstance()
    {
        var backend1 = new InMemoryTerminalBackend();
        var backend2 = new InMemoryTerminalBackend();
        using var terminal1 = new TerminalInstance(backend1);
        using var terminal2 = new TerminalInstance(backend2);
        using var builder = new AnsiBuilder();

        using (terminal1.CaptureOutput(builder))
        {
            terminal1.Write("mine");
            terminal2.Write("other");
        }

        Assert.AreEqual("mine", builder.ToString());
        Assert.AreEqual("", backend1.GetOutText());
        Assert.AreEqual("other", backend2.GetOutText());
    }

    [TestMethod]
    public void CaptureOutput_NestedAcrossInstances_RoutesEachWriteToItsOwner()
    {
        var backend1 = new InMemoryTerminalBackend();
        var backend2 = new InMemoryTerminalBackend();
        using var terminal1 = new TerminalInstance(backend1);
        using var terminal2 = new TerminalInstance(backend2);
        using var builder1 = new AnsiBuilder();
        using var builder2 = new AnsiBuilder();

        using (terminal1.CaptureOutput(builder1))
        using (terminal2.CaptureOutput(builder2))
        {
            terminal1.Write("a");
            terminal2.Write("b");
            // The innermost capture belongs to terminal2, but this write is terminal1's.
            terminal1.Write("c");
        }

        Assert.AreEqual("ac", builder1.ToString());
        Assert.AreEqual("b", builder2.ToString());
        Assert.AreEqual("", backend1.GetOutText());
        Assert.AreEqual("", backend2.GetOutText());
    }

    [TestMethod]
    public void UseInstance_OutOfOrderOptIn_OuterDisposedFirst_KeepsInnerAndNeverRestoresOuter()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var outerTerminal = new TerminalInstance(new InMemoryTerminalBackend());
        using var innerTerminal = new TerminalInstance(new InMemoryTerminalBackend());

        var outer = Terminal.UseInstance(outerTerminal, allowOutOfOrderDisposal: true);
        var inner = Terminal.UseInstance(innerTerminal);

        outer.Dispose();
        Assert.AreSame(innerTerminal, Terminal.Instance);

        inner.Dispose();
        // The disposed outer terminal must not come back as the ambient instance.
        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    public void UseInstance_OutOfOrderOptIn_MiddleDisposed_IsSkippedWhenInnerScopesEnd()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var terminal1 = new TerminalInstance(new InMemoryTerminalBackend());
        using var terminal2 = new TerminalInstance(new InMemoryTerminalBackend());
        using var terminal3 = new TerminalInstance(new InMemoryTerminalBackend());

        var outer = Terminal.UseInstance(terminal1);
        var middle = Terminal.UseInstance(terminal2, allowOutOfOrderDisposal: true);
        var inner = Terminal.UseInstance(terminal3);

        middle.Dispose();
        Assert.AreSame(terminal3, Terminal.Instance);

        inner.Dispose();
        Assert.AreSame(terminal1, Terminal.Instance);

        outer.Dispose();
        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    public void UseInstance_OutOfOrderOptIn_OuterWithSeveralStrictInnerScopes_RestoresInOrder()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var terminal1 = new TerminalInstance(new InMemoryTerminalBackend());
        using var terminal2 = new TerminalInstance(new InMemoryTerminalBackend());
        using var terminal3 = new TerminalInstance(new InMemoryTerminalBackend());

        var outer = Terminal.UseInstance(terminal1, allowOutOfOrderDisposal: true);
        var inner1 = Terminal.UseInstance(terminal2);
        var inner2 = Terminal.UseInstance(terminal3);

        outer.Dispose();
        Assert.AreSame(terminal3, Terminal.Instance);

        inner2.Dispose();
        Assert.AreSame(terminal2, Terminal.Instance);

        inner1.Dispose();
        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    public void UseInstance_OutOfOrderOptIn_StrictMiddleStillThrows_WithoutConsumingScope()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var terminal1 = new TerminalInstance(new InMemoryTerminalBackend());
        using var terminal2 = new TerminalInstance(new InMemoryTerminalBackend());
        using var terminal3 = new TerminalInstance(new InMemoryTerminalBackend());

        var outer = Terminal.UseInstance(terminal1, allowOutOfOrderDisposal: true);
        var strictMiddle = Terminal.UseInstance(terminal2);
        var inner = Terminal.UseInstance(terminal3);

        Assert.ThrowsExactly<InvalidOperationException>(() => strictMiddle.Dispose());
        Assert.AreSame(terminal3, Terminal.Instance);

        inner.Dispose();
        Assert.AreSame(terminal2, Terminal.Instance);
        strictMiddle.Dispose();
        Assert.AreSame(terminal1, Terminal.Instance);
        outer.Dispose();
        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    public void UseInstance_OutOfOrderOptIn_SameInstanceNested_UsesScopeIdentity()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var terminal = new TerminalInstance(new InMemoryTerminalBackend());

        var outer = Terminal.UseInstance(terminal, allowOutOfOrderDisposal: true);
        var inner = Terminal.UseInstance(terminal);

        // Same terminal on both scopes: unlinking the outer one must not be mistaken for unlinking the inner one.
        outer.Dispose();
        Assert.AreSame(terminal, Terminal.Instance);

        inner.Dispose();
        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    public void UseInstance_OutOfOrderOptIn_TwoOptInScopesOnSameInstance_RestoreByIdentity()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var terminal = new TerminalInstance(new InMemoryTerminalBackend());

        var first = Terminal.UseInstance(terminal, allowOutOfOrderDisposal: true);
        var second = Terminal.UseInstance(terminal, allowOutOfOrderDisposal: true);

        first.Dispose();
        Assert.AreSame(terminal, Terminal.Instance);

        second.Dispose();
        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    public void UseInstance_OutOfOrderOptIn_RepeatedDisposal_IsNoOp()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var terminal1 = new TerminalInstance(new InMemoryTerminalBackend());
        using var terminal2 = new TerminalInstance(new InMemoryTerminalBackend());

        var outer = Terminal.UseInstance(terminal1, allowOutOfOrderDisposal: true);
        var inner = Terminal.UseInstance(terminal2);

        outer.Dispose();
        outer.Dispose();
        Assert.AreSame(terminal2, Terminal.Instance);

        inner.Dispose();
        inner.Dispose();
        Assert.AreSame(global, Terminal.Instance);

        // Disposing an unlinked scope after the chain has unwound must still do nothing.
        outer.Dispose();
        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    public void UseInstance_OutOfOrderOptIn_UnlinkedScopeIsSkippedWhenTopIsDisposed()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var terminal1 = new TerminalInstance(new InMemoryTerminalBackend());
        using var terminal2 = new TerminalInstance(new InMemoryTerminalBackend());

        var first = Terminal.UseInstance(terminal1, allowOutOfOrderDisposal: true);
        var second = Terminal.UseInstance(terminal2, allowOutOfOrderDisposal: true);

        first.Dispose();
        Assert.AreSame(terminal2, Terminal.Instance);

        second.Dispose();
        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    public void UseInstance_OutOfOrderOptIn_ChainedUnlinks_RestoreGlobalWhenInnerEnds()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var terminal1 = new TerminalInstance(new InMemoryTerminalBackend());
        using var terminal2 = new TerminalInstance(new InMemoryTerminalBackend());
        using var terminal3 = new TerminalInstance(new InMemoryTerminalBackend());

        var outer = Terminal.UseInstance(terminal1, allowOutOfOrderDisposal: true);
        var middle = Terminal.UseInstance(terminal2, allowOutOfOrderDisposal: true);
        var inner = Terminal.UseInstance(terminal3);

        middle.Dispose();
        Assert.AreSame(terminal3, Terminal.Instance);
        outer.Dispose();
        Assert.AreSame(terminal3, Terminal.Instance);

        inner.Dispose();
        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    public void UseInstance_OutOfOrderOptIn_ScopeCreatedAfterUnlink_RestoresThroughSplicedChain()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var terminal1 = new TerminalInstance(new InMemoryTerminalBackend());
        using var terminal2 = new TerminalInstance(new InMemoryTerminalBackend());
        using var terminal3 = new TerminalInstance(new InMemoryTerminalBackend());

        var outer = Terminal.UseInstance(terminal1, allowOutOfOrderDisposal: true);
        var middle = Terminal.UseInstance(terminal2, allowOutOfOrderDisposal: true);

        outer.Dispose();
        Assert.AreSame(terminal2, Terminal.Instance);

        // The scope pushed on the spliced chain restores the middle scope, which in turn restores the global instance.
        var later = Terminal.UseInstance(terminal3);
        later.Dispose();
        Assert.AreSame(terminal2, Terminal.Instance);

        middle.Dispose();
        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    public void UseInstance_OutOfOrderOptIn_DisposeOnOtherThread_ThrowsWithoutConsumingScope()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var terminal = new TerminalInstance(new InMemoryTerminalBackend());

        var scope = Terminal.UseInstance(terminal, allowOutOfOrderDisposal: true);

        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                scope.Dispose();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.Start();
        thread.Join();

        Assert.IsInstanceOfType<InvalidOperationException>(caught);
        Assert.AreSame(terminal, Terminal.Instance);

        scope.Dispose();
        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    public void UseInstance_OutOfOrderOptInFalse_IsStrictLikeTheDefault()
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var terminal1 = new TerminalInstance(new InMemoryTerminalBackend());
        using var terminal2 = new TerminalInstance(new InMemoryTerminalBackend());

        var outer = Terminal.UseInstance(terminal1, allowOutOfOrderDisposal: false);
        var inner = Terminal.UseInstance(terminal2);

        Assert.ThrowsExactly<InvalidOperationException>(() => outer.Dispose());
        Assert.AreSame(terminal2, Terminal.Instance);

        inner.Dispose();
        Assert.AreSame(terminal1, Terminal.Instance);
        outer.Dispose();
        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    public void UseInstance_OutOfOrderOptIn_ScopeUnreachableAfterReset_DisposeIsSafeAndIdempotent()
    {
        using var stale1 = new TerminalInstance(new InMemoryTerminalBackend());
        using var stale2 = new TerminalInstance(new InMemoryTerminalBackend());
        using var fresh = new TerminalInstance(new InMemoryTerminalBackend());
        var staleScope1 = Terminal.UseInstance(stale1, allowOutOfOrderDisposal: true);
        var staleScope2 = Terminal.UseInstance(stale2, allowOutOfOrderDisposal: true);

        // Clears the chain of this thread without disposing its scopes: both stale scopes become unreachable.
        Terminal.ResetForTests();
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        var freshScope = Terminal.UseInstance(fresh);

        // A new chain is active: disposing an unreachable scope must neither throw nor touch it.
        staleScope1.Dispose();
        staleScope1.Dispose();
        Assert.AreSame(fresh, Terminal.Instance);

        freshScope.Dispose();
        Assert.AreSame(global, Terminal.Instance);

        // The chain is now empty: disposing an unreachable scope must still be safe.
        staleScope2.Dispose();
        staleScope2.Dispose();
        Assert.AreSame(global, Terminal.Instance);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UseInstance_WithOption_NullInstance_Throws(bool allowOutOfOrderDisposal)
    {
        var ex = Assert.ThrowsExactly<ArgumentNullException>(() => Terminal.UseInstance(null!, allowOutOfOrderDisposal));
        Assert.AreEqual("instance", ex.ParamName);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UseInstance_WithOption_DisposedInstance_ThrowsWithoutChangingAmbient(bool allowOutOfOrderDisposal)
    {
        var global = Terminal.Initialize(new InMemoryTerminalBackend());
        using var active = new TerminalInstance(new InMemoryTerminalBackend());
        var disposed = new TerminalInstance(new InMemoryTerminalBackend());
        disposed.Dispose();

        using (Terminal.UseInstance(active))
        {
            Assert.ThrowsExactly<ObjectDisposedException>(() => Terminal.UseInstance(disposed, allowOutOfOrderDisposal));
            Assert.AreSame(active, Terminal.Instance);
        }

        Assert.AreSame(global, Terminal.Instance);
    }

    /// <summary>
    /// A backend that counts its <see cref="Dispose"/> calls, to observe which owner disposes it.
    /// </summary>
    private sealed class DisposeCountingBackend : VirtualTerminalBackend
    {
        public int DisposeCount { get; private set; }

        public override void Dispose()
        {
            DisposeCount++;
            base.Dispose();
        }
    }
}
