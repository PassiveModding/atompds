﻿using System.Runtime.CompilerServices;
using Sequencer.Types;
using Xrpc;

namespace Sequencer;

public record OutboxOpts(int MaxBufferSize);
public class Outbox
{
    private readonly SequencerRepository _sequencer;
    private readonly OutboxOpts _opts;
    private bool _caughtUp;
    public int LastSeen { get; private set; } = -1;
    public Queue<ISeqEvt> OutBuffer { get; } = new();
    public Queue<ISeqEvt> CutoverBuffer { get; } = new();
    
    public Outbox(SequencerRepository sequencer, OutboxOpts opts)
    {
        _sequencer = sequencer;
        _opts = opts;
    }

    public async IAsyncEnumerable<ISeqEvt> Events(int? backfillCursor, [EnumeratorCancellation] CancellationToken token)
    {
        if (backfillCursor != null)
        {
            await foreach (var evt in GetBackfill(backfillCursor.Value).WithCancellation(token))
            {
                if (token.IsCancellationRequested) yield break;
                LastSeen = evt.Seq;
                yield return evt;
            }
        }
        else
        {
            _caughtUp = true;
        }
        
        _sequencer.OnEvents += OnEvents;
        _sequencer.OnClose += (sender, args) => _sequencer.OnEvents -= OnEvents;
        
        await Cutover(backfillCursor);

        while (true)
        {
            while (OutBuffer.TryDequeue(out var evt))
            {
                if (token.IsCancellationRequested) yield break;
                if (evt.Seq > LastSeen)
                {
                    LastSeen = evt.Seq;
                    yield return evt;
                }
                
                if (OutBuffer.Count > _opts.MaxBufferSize)
                {
                    throw new XRPCError(new ErrorDetail("ConsumerTooSlow", "Stream consumer too slow"));
                }
            }
        }
    }

    private async Task Cutover(int? backfillCursor)
    {
        // only need to perform cutover if we've been backfilling
        if (backfillCursor != null)
        {
            var cutoverEvts = await _sequencer.GetRange(LastSeen > -1 ? LastSeen : backfillCursor.Value, null, null, null);
            foreach (var evt in cutoverEvts)
            {
                OutBuffer.Enqueue(evt);
            }
            // dont worry about dupes, we ensure order on yield
            foreach (var evt in CutoverBuffer)
            {
                OutBuffer.Enqueue(evt);
            }
        }
    }

    public async IAsyncEnumerable<ISeqEvt> GetBackfill(int backfillCursor)
    {
        const int PAGE_SIZE = 500;
        while (true)
        {
            var evts = await _sequencer.GetRange(LastSeen > -1 ? LastSeen : backfillCursor, null, null, PAGE_SIZE);
            foreach (var t in evts)
            {
                yield return t;
            }

            var seqCursor = _sequencer.LastSeen ?? -1;
            if (seqCursor - LastSeen < PAGE_SIZE / 2) break;
            if (evts.Length < 1) break;
        }
    }
    
    private void OnEvents(object? sender, ISeqEvt[] e)
    {
        if (_caughtUp)
        {
            foreach (var evt in e)
            {
                OutBuffer.Enqueue(evt);
            }
        }
        else
        {
            foreach (var evt in e)
            {
                CutoverBuffer.Enqueue(evt);
            }
        }
    }
}