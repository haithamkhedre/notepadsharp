using System;
using NotepadSharp.Core;

namespace NotepadSharp.App.Services;

public sealed record RecoverySnapshot(
    Guid DocumentId,
    DateTimeOffset TimestampUtc,
    string? FilePath,
    string EncodingWebName,
    bool HasBom,
    LineEnding PreferredLineEnding,
    string Text,
    long ChangeVersion,
    DateTimeOffset? FileLastWriteTimeUtc);
