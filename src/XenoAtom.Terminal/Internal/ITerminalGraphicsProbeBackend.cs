// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal.Internal;

internal interface ITerminalGraphicsProbeBackend
{
    TerminalGraphicsProbeCoordinator GraphicsProbeCoordinator { get; }

    bool TryWriteGraphicsProbe(string sequence);
}