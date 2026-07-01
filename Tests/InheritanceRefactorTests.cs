namespace PromptEnhance.Tests;

/// <summary>Proves F13: <see cref="PromptEnhance.WebAPI.BackendClient"/> and
/// <see cref="PromptEnhance.WebAPI.SessionSettings"/> no longer inherit the helper class
/// <see cref="PromptEnhance.WebAPI.PromptEnhanceAPI"/> — its public static payload helpers are
/// reached by explicit qualification, not by an empty base relationship.</summary>
public class InheritanceRefactorTests
{
    [Xunit.Fact]
    public void BackendClient_DoesNotInheritApiHelperClass()
    {
        // Arrange
        System.Type type = typeof(WebAPI.BackendClient);

        // Act
        System.Type? baseType = type.BaseType;

        // Assert
        Xunit.Assert.Equal(typeof(object), baseType);
    }

    [Xunit.Fact]
    public void SessionSettings_DoesNotInheritApiHelperClass()
    {
        // Arrange
        System.Type type = typeof(WebAPI.SessionSettings);

        // Act
        System.Type? baseType = type.BaseType;

        // Assert
        Xunit.Assert.Equal(typeof(object), baseType);
    }
}
