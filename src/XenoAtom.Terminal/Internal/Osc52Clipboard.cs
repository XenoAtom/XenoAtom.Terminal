// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Buffers;
using System.Text;

namespace XenoAtom.Terminal.Internal;

internal static class Osc52Clipboard
{
    public static bool TrySetText(TextWriter writer, ReadOnlySpan<char> text, int maxUtf8Bytes)
    {
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var byteCount = Encoding.UTF8.GetByteCount(text);
            if (byteCount < 0 || byteCount > maxUtf8Bytes)
            {
                return false;
            }

            var bytes = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                var writtenBytes = Encoding.UTF8.GetBytes(text, bytes);
                if (writtenBytes != byteCount)
                {
                    byteCount = writtenBytes;
                }

                var base64Len = ((byteCount + 2) / 3) * 4;
                var base64 = ArrayPool<char>.Shared.Rent(base64Len);
                try
                {
                    if (!Convert.TryToBase64Chars(bytes.AsSpan(0, byteCount), base64, out var writtenChars))
                    {
                        return false;
                    }

                    writer.Write("\x1b]52;c;");
                    writer.Write(base64.AsSpan(0, writtenChars));
                    writer.Write('\x07');
                    writer.Flush();
                    return true;
                }
                finally
                {
                    ArrayPool<char>.Shared.Return(base64, clearArray: true);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes, clearArray: true);
            }
        }
        catch
        {
            return false;
        }
    }
}
