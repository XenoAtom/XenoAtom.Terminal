// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Text;
using XenoAtom.Ansi;

namespace XenoAtom.Terminal.Internal;

internal sealed class AnsiBuilderTextWriter : TextWriter
{
    private readonly AnsiBuilder _builder;

    public AnsiBuilderTextWriter(AnsiBuilder builder)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        Span<char> tmp = stackalloc char[1];
        tmp[0] = value;
        _builder.Append(tmp);
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        _builder.Append(value.AsSpan());
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        _builder.Append(buffer);
    }

    public override void WriteLine()
    {
        _builder.Append("\n".AsSpan());
    }

    public override void WriteLine(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _builder.Append(value.AsSpan());
        }

        _builder.Append("\n".AsSpan());
    }

    public override void WriteLine(ReadOnlySpan<char> buffer)
    {
        _builder.Append(buffer);
        _builder.Append("\n".AsSpan());
    }
}

