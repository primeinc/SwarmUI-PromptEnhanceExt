namespace PromptEnhance.Tests;

[Xunit.Collection(ApiRegistryCollectionDefinition.Name)]
public class ApiRegistrationTests
{
    [Xunit.Fact]
    public void Register_WiresIsUserUpdateAndPermissionPerRoute()
    {
        Xunit.Assert.False(SwarmUI.WebAPI.API.APIHandlers["promptenhancelistmodels"].IsUserUpdate);
        Xunit.Assert.True(SwarmUI.WebAPI.API.APIHandlers["promptenhancerun"].IsUserUpdate);
        Xunit.Assert.False(SwarmUI.WebAPI.API.APIHandlers["getpromptenhancesettings"].IsUserUpdate);
        Xunit.Assert.True(SwarmUI.WebAPI.API.APIHandlers["savepromptenhancesettings"].IsUserUpdate);
        Xunit.Assert.True(SwarmUI.WebAPI.API.APIHandlers["resetpromptenhancesettings"].IsUserUpdate);

        Xunit.Assert.Equal("promptenhance_use_backend", SwarmUI.WebAPI.API.APIHandlers["promptenhancelistmodels"].Permission.ID);
        Xunit.Assert.Equal("promptenhance_use_backend", SwarmUI.WebAPI.API.APIHandlers["promptenhancerun"].Permission.ID);
        Xunit.Assert.Equal("promptenhance_config", SwarmUI.WebAPI.API.APIHandlers["getpromptenhancesettings"].Permission.ID);
        Xunit.Assert.Equal("promptenhance_config", SwarmUI.WebAPI.API.APIHandlers["savepromptenhancesettings"].Permission.ID);
        Xunit.Assert.Equal("promptenhance_config", SwarmUI.WebAPI.API.APIHandlers["resetpromptenhancesettings"].Permission.ID);
    }

    /// <summary>Mirrors SwarmUI's API.GenerateAPIDocs: no generated route doc may print a "(... NOT SET)" placeholder.</summary>
    [Xunit.Fact]
    public void EveryRoute_IsFullyDocumentedForSwarmUIsApiDocGenerator()
    {
        string[] ours = [.. SwarmUI.WebAPI.API.APIHandlers.Keys.Where(k => k.Contains("promptenhance")).OrderBy(k => k)];
        Xunit.Assert.Equal(5, ours.Length);
        List<string> missing = [];
        foreach (string key in ours)
        {
            System.Reflection.MethodInfo method = SwarmUI.WebAPI.API.APIHandlers[key].Original;
            if (string.IsNullOrWhiteSpace(System.Reflection.CustomAttributeExtensions.GetCustomAttribute<SwarmUI.WebAPI.API.APIClassAttribute>(method.DeclaringType!)?.Description))
            {
                missing.Add($"{method.DeclaringType!.Name}: no [API.APIClass]");
            }
            SwarmUI.WebAPI.API.APIDescriptionAttribute? description = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<SwarmUI.WebAPI.API.APIDescriptionAttribute>(method);
            if (string.IsNullOrWhiteSpace(description?.Description) || string.IsNullOrWhiteSpace(description?.ReturnInfo))
            {
                missing.Add($"{method.Name}: no [API.APIDescription] with description and return shape");
            }
            foreach (System.Reflection.ParameterInfo param in method.GetParameters().Where(p => p.ParameterType != typeof(SwarmUI.Accounts.Session)))
            {
                if (string.IsNullOrWhiteSpace(System.Reflection.CustomAttributeExtensions.GetCustomAttribute<SwarmUI.WebAPI.API.APIParameterAttribute>(param)?.Description))
                {
                    missing.Add($"{method.Name}({param.Name}): no [API.APIParameter]");
                }
            }
        }
        Xunit.Assert.Empty(missing);
    }
}
