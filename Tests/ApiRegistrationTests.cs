namespace PromptEnhance.Tests;

/// <summary>Pins the SwarmUI <c>isUserUpdate</c> flag each PromptEnhance route registers with. The flag only bumps the
/// session's last-used time (idle-timeout bookkeeping); per the SwarmUI convention (APICall param doc: "Use false for
/// basic getters or automated actions") pure reads register false and user-driven mutations/generation register true.
/// A wrong flag mis-accounts session activity. Sole caller of Register() in this assembly (APIHandlers.Add throws on
/// duplicate keys).</summary>
public class ApiRegistrationTests
{
    [Xunit.Fact]
    public void Register_SetsIsUserUpdatePerConvention()
    {
        // Arrange / Act
        PromptEnhance.WebAPI.PromptEnhanceAPI.Register();

        // Assert - getters/automated => false; user-driven mutations & generation => true
        Xunit.Assert.False(SwarmUI.WebAPI.API.APIHandlers["promptenhancelistmodels"].IsUserUpdate);
        Xunit.Assert.True(SwarmUI.WebAPI.API.APIHandlers["promptenhancerun"].IsUserUpdate);
        Xunit.Assert.False(SwarmUI.WebAPI.API.APIHandlers["getpromptenhancesettings"].IsUserUpdate);
        Xunit.Assert.True(SwarmUI.WebAPI.API.APIHandlers["savepromptenhancesettings"].IsUserUpdate);
        Xunit.Assert.True(SwarmUI.WebAPI.API.APIHandlers["resetpromptenhancesettings"].IsUserUpdate);
    }
}
