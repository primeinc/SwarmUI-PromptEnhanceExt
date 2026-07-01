namespace PromptEnhance.Tests;

using SwarmUI.Accounts;

/// <summary>Verifies each PromptEnhance permission declares an explicit <see cref="PermSafetyLevel"/> rather than
/// falling through to the <see cref="PermSafetyLevel.UNTESTED"/> default, matching SwarmUI's built-in convention
/// (Permissions.cs) where every registered permission states its safety level.</summary>
public class PermissionsTests
{
    [Xunit.Fact]
    public void PermUseBackend_DeclaresPowerfulSafetyLevel()
    {
        // Arrange / Act
        PermInfo perm = WebAPI.PromptEnhancePermissions.PermUseBackend;

        // Assert
        Xunit.Assert.Equal(PermSafetyLevel.POWERFUL, perm.SafetyLevel);
    }

    [Xunit.Fact]
    public void PermConfig_DeclaresPowerfulSafetyLevel()
    {
        // Arrange / Act
        PermInfo perm = WebAPI.PromptEnhancePermissions.PermConfig;

        // Assert
        Xunit.Assert.Equal(PermSafetyLevel.POWERFUL, perm.SafetyLevel);
    }
}
