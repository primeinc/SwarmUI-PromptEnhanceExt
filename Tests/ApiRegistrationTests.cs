namespace PromptEnhance.Tests;

public class ApiRegistrationTests
{
    [Xunit.Fact]
    public void Register_WiresIsUserUpdateAndPermissionPerRoute()
    {
        PromptEnhance.WebAPI.PromptEnhanceAPI.Register();

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
}
