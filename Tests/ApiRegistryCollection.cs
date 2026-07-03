namespace PromptEnhance.Tests;

[Xunit.CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ApiRegistryCollectionDefinition : Xunit.ICollectionFixture<ApiRegistryFixture>
{
    internal const string Name = "API registry";
}

/// <summary>
/// Registers the PromptEnhance API routes exactly once for the "API registry"
/// collection and removes them on disposal. Tests in the collection read the
/// known-registered process-global registry (SwarmUI.WebAPI.API.APIHandlers)
/// without ever calling Register() themselves — RegisterAPICall uses
/// Dictionary.Add, which throws on a duplicate key, so a single owned
/// registration keeps the suite green by construction rather than by run order.
/// </summary>
public sealed class ApiRegistryFixture : System.IDisposable
{
    /// <summary>The five route keys Register() adds.</summary>
    private static readonly string[] RouteKeys =
    [
        "promptenhancelistmodels",
        "promptenhancerun",
        "getpromptenhancesettings",
        "savepromptenhancesettings",
        "resetpromptenhancesettings"
    ];

    private readonly bool _ownsRegistration;

    public ApiRegistryFixture()
    {
        // Robust to partial state: count how many routes are already present.
        int present = 0;
        foreach (string key in RouteKeys)
        {
            if (SwarmUI.WebAPI.API.APIHandlers.ContainsKey(key))
            {
                present++;
            }
        }
        // Fully registered elsewhere -> read-only; do not own or clean up.
        if (present == RouteKeys.Length)
        {
            return;
        }
        // Partially registered (leftover/corrupt) -> clear so Register()'s
        // Dictionary.Add cannot throw on a lingering duplicate key.
        if (present > 0)
        {
            foreach (string key in RouteKeys)
            {
                SwarmUI.WebAPI.API.APIHandlers.Remove(key);
            }
        }
        PromptEnhance.WebAPI.PromptEnhanceAPI.Register();
        _ownsRegistration = true;
    }

    public void Dispose()
    {
        if (!_ownsRegistration)
        {
            return;
        }
        foreach (string key in RouteKeys)
        {
            SwarmUI.WebAPI.API.APIHandlers.Remove(key);
        }
    }
}
