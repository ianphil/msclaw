namespace MsClaw.Core;

public interface IIdentityLoader
{
    Task<string> LoadSystemMessageAsync(string mindRoot, CancellationToken cancellationToken = default);
}
