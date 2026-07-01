namespace PromptEnhance.Tests;

/// <summary>Pins two properties of each PromptEnhance route registration against SwarmUI's real <c>API.APIHandlers</c>
/// table: (1) the <c>isUserUpdate</c> flag (idle-timeout bookkeeping — convention: getters/automated register false,
/// user-driven mutations/generation register true), and (2) the required <see cref="SwarmUI.Accounts.PermInfo"/> — the
/// field SwarmUI enforces at request time (API.cs: <c>handler.Permission is not null &amp;&amp; !session.User.HasPermission(...)</c>).
/// A dropped or swapped permission argument would otherwise leave every test green. Sole caller of Register() in this
/// assembly (APIHandlers.Add throws on duplicate keys), so both concerns are asserted from a single Register() call.</summary>
public class ApiRegistrationTests
{
    [Xunit.Fact]
    public void Register_WiresIsUserUpdateAndPermissionPerRoute()
    {
        // Arrange / Act — register the extension's routes into SwarmUI's real API.APIHandlers table.
        PromptEnhance.WebAPI.PromptEnhanceAPI.Register();

        // Assert (isUserUpdate) — getters/automated => false; user-driven mutations & generation => true.
        Xunit.Assert.False(SwarmUI.WebAPI.API.APIHandlers["promptenhancelistmodels"].IsUserUpdate);
        Xunit.Assert.True(SwarmUI.WebAPI.API.APIHandlers["promptenhancerun"].IsUserUpdate);
        Xunit.Assert.False(SwarmUI.WebAPI.API.APIHandlers["getpromptenhancesettings"].IsUserUpdate);
        Xunit.Assert.True(SwarmUI.WebAPI.API.APIHandlers["savepromptenhancesettings"].IsUserUpdate);
        Xunit.Assert.True(SwarmUI.WebAPI.API.APIHandlers["resetpromptenhancesettings"].IsUserUpdate);

        // Assert (permission binding) — outbound-backend routes require PermUseBackend; config routes require PermConfig.
        Xunit.Assert.Equal("promptenhance_use_backend", SwarmUI.WebAPI.API.APIHandlers["promptenhancelistmodels"].Permission.ID);
        Xunit.Assert.Equal("promptenhance_use_backend", SwarmUI.WebAPI.API.APIHandlers["promptenhancerun"].Permission.ID);
        Xunit.Assert.Equal("promptenhance_config", SwarmUI.WebAPI.API.APIHandlers["getpromptenhancesettings"].Permission.ID);
        Xunit.Assert.Equal("promptenhance_config", SwarmUI.WebAPI.API.APIHandlers["savepromptenhancesettings"].Permission.ID);
        Xunit.Assert.Equal("promptenhance_config", SwarmUI.WebAPI.API.APIHandlers["resetpromptenhancesettings"].Permission.ID);
    }
}
