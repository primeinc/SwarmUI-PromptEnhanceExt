using SwarmUI.Accounts;

namespace PromptEnhance.Tests;

/// <summary>Builds a real User with the generic data store swapped for an in-memory LiteDB collection.</summary>
internal static class TestSessions
{
    public static Session MakeRealSession()
    {
        SessionHandler handler = (SessionHandler)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SessionHandler));
        handler.DBLock = new();
        handler.Roles = new();
        LiteDB.LiteDatabase database = new(new MemoryStream());
        handler.GenericData = database.GetCollection<SessionHandler.GenericDataStore>("generic_data");
        User user = new(handler, new User.DatabaseEntry { ID = "promptenhance_test_user", RawSettings = "\n" });
        return new Session { User = user };
    }
}
