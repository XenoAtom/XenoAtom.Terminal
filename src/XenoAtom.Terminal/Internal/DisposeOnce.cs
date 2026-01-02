// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Threading;

namespace XenoAtom.Terminal.Internal;

internal sealed class DisposeOnce : IDisposable
{
    private Action? _dispose;

    public DisposeOnce(Action dispose) => _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));

    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}

