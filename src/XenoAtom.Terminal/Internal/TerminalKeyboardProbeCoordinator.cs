// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Ansi.Tokens;

namespace XenoAtom.Terminal.Internal;

internal sealed class TerminalKeyboardProbeCoordinator
{
    private readonly object _sync = new();
    private TaskCompletionSource<int?>? _kittyKeyboardQuery;

    public void BeginKittyKeyboardQuery()
    {
        lock (_sync)
        {
            _kittyKeyboardQuery?.TrySetCanceled(CancellationToken.None);
            _kittyKeyboardQuery = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void EndKittyKeyboardQuery()
    {
        lock (_sync)
        {
            _kittyKeyboardQuery = null;
        }
    }

    public bool TryGetKittyKeyboardQueryResult(out int? flags)
    {
        TaskCompletionSource<int?>? request;
        lock (_sync)
        {
            request = _kittyKeyboardQuery;
        }

        if (request is null || !request.Task.IsCompletedSuccessfully)
        {
            flags = null;
            return false;
        }

        flags = request.Task.Result;
        return true;
    }

    public bool TryConsume(CsiToken token)
    {
        if (TryConsumeKittyKeyboardQueryReply(token))
        {
            return true;
        }

        if (TryConsumeKittyKeyboardQuerySentinel(token))
        {
            return true;
        }

        return false;
    }

    private bool TryConsumeKittyKeyboardQueryReply(CsiToken token)
    {
        if (token.Final != 'u' || token.PrivateMarker != '?' || token.Intermediates.Length != 0)
        {
            return false;
        }

        CompleteKittyKeyboardQuery(token.Parameters.Length > 0 ? Math.Max(0, token.Parameters[0]) : 0);
        return true;
    }

    private bool TryConsumeKittyKeyboardQuerySentinel(CsiToken token)
    {
        if (token.Final != 'c' || token.PrivateMarker != '?' || token.Intermediates.Length != 0)
        {
            return false;
        }

        TaskCompletionSource<int?>? request;
        lock (_sync)
        {
            request = _kittyKeyboardQuery;
        }

        if (request is null)
        {
            return false;
        }

        // Primary Device Attributes is sent after the Kitty keyboard query. If it arrives first,
        // the terminal did not answer the keyboard query and therefore does not support it.
        CompleteKittyKeyboardQuery(null);
        return true;
    }

    private void CompleteKittyKeyboardQuery(int? flags)
    {
        TaskCompletionSource<int?>? request;
        lock (_sync)
        {
            request = _kittyKeyboardQuery;
        }

        request?.TrySetResult(flags);
    }
}
