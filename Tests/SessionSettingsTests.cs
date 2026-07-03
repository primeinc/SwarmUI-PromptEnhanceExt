using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;

namespace PromptEnhance.Tests;

public class SessionSettingsTests
{
    private static JObject Full() => new()
    {
        ["baseUrl"] = "http://localhost:11434",
        ["timeoutSeconds"] = 60,
        ["temperature"] = 0.7,
        ["maxTokens"] = 1024,
        ["replaceMode"] = "preview"
    };

    [Xunit.Fact]
    public void ValidateSettings_AcceptsAValidObject()
    {
        JObject input = Full();
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        Xunit.Assert.Null(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_IgnoresAbsentKeys()
    {
        JObject input = new() { ["replaceMode"] = "append" };
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        Xunit.Assert.Null(error);
    }

    [Xunit.Theory]
    [Xunit.InlineData(0)]
    [Xunit.InlineData(-5)]
    public void ValidateSettings_RejectsNonPositiveTimeout(int timeout)
    {
        JObject input = Full();
        input["timeoutSeconds"] = timeout;
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsNonNumericTemperature()
    {
        JObject input = Full();
        input["temperature"] = "abc";
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    [Xunit.Theory]
    [Xunit.InlineData(-0.1)]
    [Xunit.InlineData(2.1)]
    public void ValidateSettings_RejectsTemperatureOutOfRange(double temperature)
    {
        JObject input = Full();
        input["temperature"] = temperature;
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsNonPositiveMaxTokens()
    {
        JObject input = Full();
        input["maxTokens"] = 0;
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsTimeoutAboveInt32()
    {
        JObject input = Full();
        input["timeoutSeconds"] = (long)int.MaxValue + 1;
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsMaxTokensAboveInt32()
    {
        JObject input = Full();
        input["maxTokens"] = (long)int.MaxValue + 1;
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsUnknownReplaceMode()
    {
        JObject input = Full();
        input["replaceMode"] = "obliterate";
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsEmptyBaseUrl()
    {
        JObject input = Full();
        input["baseUrl"] = "";
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsTimeoutAboveCeiling()
    {
        JObject input = Full();
        input["timeoutSeconds"] = 3601;
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
        Xunit.Assert.Contains("3600", error!["error"]!.Value<string>());
    }

    [Xunit.Fact]
    public void ValidateSettings_AcceptsTimeoutAtCeiling()
    {
        JObject input = Full();
        input["timeoutSeconds"] = 3600;
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        Xunit.Assert.Null(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsNonStringModel()
    {
        JObject input = Full();
        input["model"] = new JObject { ["name"] = "llama3" };
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_AcceptsEmptyStringModel()
    {
        JObject input = Full();
        input["model"] = "";
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        Xunit.Assert.Null(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsNonStringSystemPrompt()
    {
        JObject input = Full();
        input["systemPrompt"] = 42;
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsNonBooleanSendSelectedImage()
    {
        JObject input = Full();
        input["sendSelectedImage"] = "yes";
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_RejectsUnparseableBaseUrl()
    {
        JObject input = Full();
        input["baseUrl"] = "not a url";
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        AssertRejected(error);
    }

    [Xunit.Fact]
    public void ValidateSettings_AcceptsBaseUrlWithV1Suffix()
    {
        JObject input = Full();
        input["baseUrl"] = "http://localhost:11434/v1/";
        JObject? error = WebAPI.SessionSettings.ValidateSettings(input);
        Xunit.Assert.Null(error);
    }

    /// <summary>
    /// A REAL User through the real constructor and the real upstream
    /// Get/SaveGenericData code paths (including their Program.NoPersist and
    /// MayCreateSessions gates). Only the SessionHandler constructor is
    /// bypassed — it opens an on-disk LiteDB user database — so the generic
    /// data store is swapped for an in-memory LiteDB collection.
    /// </summary>
    private static Session MakeRealSession()
    {
        SessionHandler handler = (SessionHandler)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SessionHandler));
        handler.DBLock = new();
        handler.Roles = new();
        LiteDB.LiteDatabase database = new(new MemoryStream());
        handler.GenericData = database.GetCollection<SessionHandler.GenericDataStore>("generic_data");
        User user = new(handler, new User.DatabaseEntry { ID = "promptenhance_test_user", RawSettings = "\n" });
        return new Session { User = user };
    }

    [Xunit.Fact]
    public async Task SavePromptEnhanceSettings_UnderNoPersist_ReturnsClassifiedErrorNotSaved()
    {
        Session session = MakeRealSession();
        JObject rawInput = new() { ["settings"] = Full() };
        bool priorNoPersist = Program.NoPersist;
        Program.NoPersist = true;
        try
        {
            JObject result = await WebAPI.SessionSettings.SavePromptEnhanceSettings(rawInput, session);
            AssertRejected(result);
            Xunit.Assert.Contains("persist", result["error"]!.Value<string>());
        }
        finally
        {
            Program.NoPersist = priorNoPersist;
        }
    }

    [Xunit.Fact]
    public async Task ResetPromptEnhanceSettings_UnderNoPersist_ReturnsClassifiedErrorNotSaved()
    {
        Session session = MakeRealSession();
        bool priorNoPersist = Program.NoPersist;
        Program.NoPersist = true;
        try
        {
            JObject result = await WebAPI.SessionSettings.ResetPromptEnhanceSettings(session);
            AssertRejected(result);
            Xunit.Assert.Contains("persist", result["error"]!.Value<string>());
        }
        finally
        {
            Program.NoPersist = priorNoPersist;
        }
    }

    [Xunit.Fact]
    public async Task SavePromptEnhanceSettings_WhenUserMayNotCreateSessions_ReturnsClassifiedErrorNotSaved()
    {
        Session session = MakeRealSession();
        session.User.MayCreateSessions = false;
        JObject rawInput = new() { ["settings"] = Full() };
        JObject result = await WebAPI.SessionSettings.SavePromptEnhanceSettings(rawInput, session);
        AssertRejected(result);
        Xunit.Assert.Contains("persist", result["error"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task SavePromptEnhanceSettings_WhenPersistenceWorks_SavesAndReturnsMergedSettings()
    {
        Session session = MakeRealSession();
        JObject rawInput = new() { ["settings"] = new JObject { ["model"] = "llama3", ["timeoutSeconds"] = 90 } };
        JObject result = await WebAPI.SessionSettings.SavePromptEnhanceSettings(rawInput, session);
        Xunit.Assert.True(result["success"]!.Value<bool>());
        Xunit.Assert.Equal("llama3", result["settings"]!["model"]!.Value<string>());
        Xunit.Assert.Equal(90, result["settings"]!["timeoutSeconds"]!.Value<int>());
        string? stored = session.User.GetGenericData("promptenhance", "config");
        Xunit.Assert.NotNull(stored);
        Xunit.Assert.Equal("llama3", JObject.Parse(stored!)["model"]!.Value<string>());
    }

    private static void AssertRejected(JObject? error)
    {
        Xunit.Assert.NotNull(error);
        Xunit.Assert.False(error!["success"]!.Value<bool>());
        Xunit.Assert.Equal("generic", error["errorCategory"]!.Value<string>());
    }
}
