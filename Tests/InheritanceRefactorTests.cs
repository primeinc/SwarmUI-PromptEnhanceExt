namespace PromptEnhance.Tests;

public class InheritanceRefactorTests
{
    [Xunit.Fact]
    public void BackendClient_DoesNotInheritApiHelperClass()
    {
        System.Type type = typeof(WebAPI.BackendClient);

        System.Type? baseType = type.BaseType;

        Xunit.Assert.Equal(typeof(object), baseType);
    }

    [Xunit.Fact]
    public void SessionSettings_DoesNotInheritApiHelperClass()
    {
        System.Type type = typeof(WebAPI.SessionSettings);

        System.Type? baseType = type.BaseType;

        Xunit.Assert.Equal(typeof(object), baseType);
    }
}
