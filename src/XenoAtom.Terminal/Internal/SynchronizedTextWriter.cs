// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace XenoAtom.Terminal.Internal;

internal sealed class SynchronizedTextWriter : TextWriter
{
    private readonly TextWriter _inner;
    private readonly Lock _sync;

    public SynchronizedTextWriter(TextWriter inner, Lock sync)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
    }

    public override Encoding Encoding => _inner.Encoding;

    public override IFormatProvider FormatProvider => _inner.FormatProvider;

    [AllowNull]
    public override string NewLine
    {
        get => _inner.NewLine;
        set => _inner.NewLine = value ?? Environment.NewLine;
    }

    public override void Flush()
    {
        lock (_sync)
        {
            _inner.Flush();
        }
    }

    public override Task FlushAsync()
    {
        lock (_sync)
        {
            return _inner.FlushAsync();
        }
    }

    public override void Write(char value)
    {
        lock (_sync)
        {
            _inner.Write(value);
        }
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        lock (_sync)
        {
            _inner.Write(buffer);
        }
    }

    public override void Write(string? value)
    {
        lock (_sync)
        {
            _inner.Write(value);
        }
    }

    public override void WriteLine()
    {
        lock (_sync)
        {
            _inner.WriteLine();
        }
    }

    public override void WriteLine(string? value)
    {
        lock (_sync)
        {
            _inner.WriteLine(value);
        }
    }

    public override Task WriteAsync(char value)
    {
        lock (_sync)
        {
            return _inner.WriteAsync(value);
        }
    }

    public override Task WriteAsync(string? value)
    {
        lock (_sync)
        {
            return _inner.WriteAsync(value);
        }
    }

    public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return _inner.WriteAsync(buffer, cancellationToken);
        }
    }

    public override Task WriteLineAsync()
    {
        lock (_sync)
        {
            return _inner.WriteLineAsync();
        }
    }

    public override Task WriteLineAsync(string? value)
    {
        lock (_sync)
        {
            return _inner.WriteLineAsync(value);
        }
    }
}
