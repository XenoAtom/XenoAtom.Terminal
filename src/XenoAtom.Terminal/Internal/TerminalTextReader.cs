// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal.Internal;

internal sealed class TerminalTextReader : TextReader
{
    private readonly TerminalInstance _terminal;
    private string? _buffer;
    private int _bufferIndex;

    public TerminalTextReader(TerminalInstance terminal)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
    }

    public override int Read()
    {
        if (!TryEnsureBuffered())
        {
            return -1;
        }

        return _buffer![_bufferIndex++];
    }

    public override int Read(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if ((uint)index > (uint)buffer.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if ((uint)count > (uint)(buffer.Length - index)) throw new ArgumentOutOfRangeException(nameof(count));

        var written = 0;
        while (written < count)
        {
            if (!TryEnsureBuffered())
            {
                return written == 0 ? -1 : written;
            }

            var toCopy = Math.Min(count - written, _buffer!.Length - _bufferIndex);
            _buffer.AsSpan(_bufferIndex, toCopy).CopyTo(buffer.AsSpan(index + written, toCopy));
            _bufferIndex += toCopy;
            written += toCopy;
        }

        return written;
    }

    public override string? ReadLine()
    {
        ClearBuffer();
        return _terminal.ReadLine();
    }

    public override async Task<string?> ReadLineAsync()
    {
        ClearBuffer();
        return await _terminal.ReadLineAsync().ConfigureAwait(false);
    }

    private bool TryEnsureBuffered()
    {
        if (_buffer is not null && _bufferIndex < _buffer.Length)
        {
            return true;
        }

        var line = _terminal.ReadLine();
        if (line is null)
        {
            return false;
        }

        _buffer = line + Environment.NewLine;
        _bufferIndex = 0;
        return true;
    }

    private void ClearBuffer()
    {
        _buffer = null;
        _bufferIndex = 0;
    }
}
