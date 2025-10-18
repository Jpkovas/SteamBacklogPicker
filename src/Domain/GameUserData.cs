namespace Domain;

public sealed record class GameUserData
{
    public static GameUserData Empty { get; } = new();

    public BacklogStatus Status { get; init; } = BacklogStatus.Unspecified;

    public string Notes { get; init; } = string.Empty;

    public TimeSpan? Playtime { get; init; }
    public TimeSpan? TargetSessionLength { get; init; }
    public TimeSpan? EstimatedCompletionTime { get; init; }
    public DateTimeOffset? EstimatedCompletionFetchedAt { get; init; }

    public bool IsEmpty => Status == BacklogStatus.Unspecified &&
                           string.IsNullOrWhiteSpace(Notes) &&
                           !Playtime.HasValue &&
                           !TargetSessionLength.HasValue &&
                           !EstimatedCompletionTime.HasValue;
}
