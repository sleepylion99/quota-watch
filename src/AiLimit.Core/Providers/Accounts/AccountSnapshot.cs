namespace AiLimit.Core.Providers.Accounts;

public sealed record AccountSnapshot(
    IReadOnlyList<QuotaBucket> Buckets,
    AccountPlan Plan,
    DateTimeOffset CheckedAt,
    string? ErrorMessage)
{
    public bool IsSuccess => ErrorMessage is null;

    public static AccountSnapshot Success(IReadOnlyList<QuotaBucket> buckets, AccountPlan plan)
        => new(buckets, plan, DateTimeOffset.UtcNow, null);

    public static AccountSnapshot Failure(string error)
        => new(Array.Empty<QuotaBucket>(), AccountPlan.Unknown, DateTimeOffset.UtcNow, error);
}
