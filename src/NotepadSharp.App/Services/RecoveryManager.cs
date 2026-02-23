using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NotepadSharp.Core;

namespace NotepadSharp.App.Services;

public sealed class RecoveryManager : IDisposable
{
    private readonly RecoveryStore _store;
    private readonly TimeSpan _interval;
    private readonly ConcurrentDictionary<Guid, long> _lastSavedVersion = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public RecoveryManager(RecoveryStore store, TimeSpan? interval = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _interval = interval ?? TimeSpan.FromSeconds(10);
    }

    public void Start(Func<IReadOnlyCollection<TextDocument>> documentsProvider)
    {
        if (documentsProvider is null)
        {
            throw new ArgumentNullException(nameof(documentsProvider));
        }

        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(documentsProvider, _cts.Token));
    }

    public void OnDocumentClosed(TextDocument doc)
    {
        if (doc is null)
        {
            return;
        }

        _lastSavedVersion.TryRemove(doc.DocumentId, out _);
        _store.Delete(doc.DocumentId);
    }

    public void OnDocumentSaved(TextDocument doc)
    {
        if (doc is null)
        {
            return;
        }

        _lastSavedVersion[doc.DocumentId] = doc.ChangeVersion;
        _store.Delete(doc.DocumentId);
    }

    private async Task LoopAsync(Func<IReadOnlyCollection<TextDocument>> documentsProvider, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                break;
            }

            IReadOnlyCollection<TextDocument> docs;
            try
            {
                docs = documentsProvider();
            }
            catch
            {
                continue;
            }

            foreach (var doc in docs)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (doc is null)
                {
                    continue;
                }

                if (!doc.IsDirty)
                {
                    _store.Delete(doc.DocumentId);
                    _lastSavedVersion[doc.DocumentId] = doc.ChangeVersion;
                    continue;
                }

                var last = _lastSavedVersion.TryGetValue(doc.DocumentId, out var lastV) ? lastV : -1;
                if (doc.ChangeVersion == last)
                {
                    continue;
                }

                var snapshot = new RecoverySnapshot(
                    doc.DocumentId,
                    TimestampUtc: DateTimeOffset.UtcNow,
                    FilePath: doc.FilePath,
                    EncodingWebName: doc.Encoding.WebName,
                    HasBom: doc.HasBom,
                    PreferredLineEnding: doc.PreferredLineEnding,
                    Text: doc.Text ?? string.Empty,
                    ChangeVersion: doc.ChangeVersion,
                    FileLastWriteTimeUtc: doc.FileLastWriteTimeUtc);

                try
                {
                    await _store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
                    _lastSavedVersion[doc.DocumentId] = doc.ChangeVersion;
                }
                catch
                {
                    // Ignore autosave failures.
                }
            }
        }
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // Ignore.
        }
    }
}
