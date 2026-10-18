namespace CloveParentalGuard.Core;

public record PipeRequest(string Type, string? Code = null, int DurationMinutes = 0);

public record PipeResponse(bool Success, bool IsLocked, int RemainingSeconds,
    string? Error = null, IReadOnlyList<int>? Durations = null,
    long BlockSeq = 0, IReadOnlyList<BlockEvent>? RecentBlocks = null);
