namespace PromptEnhance.Tests;

using SwarmUI.Accounts;

public class PermissionsTests
{
    [Xunit.Fact]
    public void PermUseBackend_DeclaresPowerfulSafetyLevel()
    {
        PermInfo perm = WebAPI.PromptEnhancePermissions.PermUseBackend;

        Xunit.Assert.Equal(PermSafetyLevel.POWERFUL, perm.SafetyLevel);
    }

    [Xunit.Fact]
    public void PermConfig_DeclaresPowerfulSafetyLevel()
    {
        PermInfo perm = WebAPI.PromptEnhancePermissions.PermConfig;

        Xunit.Assert.Equal(PermSafetyLevel.POWERFUL, perm.SafetyLevel);
    }
}
