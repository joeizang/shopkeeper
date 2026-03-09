using NodaTime;
using Shopkeeper.Api.Domain;

namespace Shopkeeper.Api.Contracts;

public sealed record SyncPushChange(
    string DeviceId,
    string EntityName,
    Guid EntityId,
    SyncOperation Operation,
    string PayloadJson,
    Instant ClientUpdatedAtUtc,
    string? RowVersionBase64);

public sealed record SyncPushRequest(List<SyncPushChange> Changes);

public sealed record SyncConflictView(
    Guid ChangeId,
    string EntityName,
    Guid EntityId,
    string Reason,
    string? ServerPayloadJson,
    string? ServerRowVersionBase64);

public sealed record SyncPushResponse(int AcceptedCount, List<SyncConflictView> Conflicts);

public sealed record SyncPullRequest(string DeviceId, Instant? SinceUtc, string? Cursor = null);

public sealed record SyncPullResponse(Instant ServerTimestampUtc, List<SyncPushChange> Changes, bool HasMore, string? NextCursor);
