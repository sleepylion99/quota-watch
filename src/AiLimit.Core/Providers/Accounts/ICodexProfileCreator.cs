namespace AiLimit.Core.Providers.Accounts;

public interface ICodexProfileCreator
{
    Task<CreateProfileResult> CreateParallelProfileAsync(CancellationToken cancellationToken);
}
