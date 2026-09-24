namespace PromptEnhance.Tests;

[Xunit.CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ApiRegistryCollectionDefinition : Xunit.ICollectionFixture<ApiRegistryFixture>
{
    internal const string Name = "API registry";
}

/// <summary>Registers the PromptEnhance API routes exactly once for the "API registry" collection and removes them on disposal.</summary>
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
        int present = 0;
        foreach (string key in RouteKeys)
        {
            if (SwarmUI.WebAPI.API.APIHandlers.ContainsKey(key))
            {
                present++;
            }
        }
        if (present == RouteKeys.Length)
        {
            return;
        }
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
