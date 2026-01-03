// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Terminal.Internal.Windows;

namespace XenoAtom.Terminal.Tests;

[TestClass]
public sealed class TerminalWindowsVtInputDetectionTests
{
    [TestMethod]
    public void IsLikelyConPtyHost_True_WhenWtSessionPresent()
    {
        Assert.IsTrue(TerminalWindowsVtInputDetection.IsLikelyConPtyHost(name => name == "WT_SESSION" ? "1" : null));
    }

    [TestMethod]
    public void IsLikelyConPtyHost_True_WhenTermProgramPresent()
    {
        Assert.IsTrue(TerminalWindowsVtInputDetection.IsLikelyConPtyHost(name => name == "TERM_PROGRAM" ? "vscode" : null));
    }

    [TestMethod]
    public void IsLikelyConPtyHost_True_WhenVsCodePidPresent()
    {
        Assert.IsTrue(TerminalWindowsVtInputDetection.IsLikelyConPtyHost(name => name == "VSCODE_PID" ? "123" : null));
    }

    [TestMethod]
    public void IsLikelyConPtyHost_False_WhenNoMarkersPresent()
    {
        Assert.IsFalse(TerminalWindowsVtInputDetection.IsLikelyConPtyHost(_ => null));
    }
}

