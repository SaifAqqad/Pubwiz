using Pubwiz.Core.Infrastructure.Platform;
using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Infrastructure.Secrets;

namespace Pubwiz.Core.Tests.Infrastructure.Secrets;

/// <summary>
/// Round-trips a secret through the real OS store. Skipped where no store is usable (CI, SSH sessions, ...).
/// </summary>
public class SecretStoreTests
{
    [Fact]
    public async Task SetGetDelete_RoundTrips()
    {
        var store = SecretStoreFactory.Create(new ProcessRunner());
        if (!store.IsAvailable || EnvironmentVariables.IsCi)
        {
            Assert.Skip($"{store.Name} isn't available here.");
        }

        var key = $"test:{Guid.NewGuid()}";
        var token = TestContext.Current.CancellationToken;

        try
        {
            await store.SetAsync(key, "Pubwiz test secret", "p@ss: wörd \"quoted\"", token);
            Assert.Equal("p@ss: wörd \"quoted\"", await store.GetAsync(key, token));

            await store.DeleteAsync(key, token);
            Assert.Null(await store.GetAsync(key, token));
        }
        finally
        {
            await store.DeleteAsync(key, token);
        }
    }
}
