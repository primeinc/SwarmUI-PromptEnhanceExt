using SwarmUI.Accounts;

namespace PromptEnhance.Tests;

/// <summary>
/// Builds a REAL User through the real constructor and the real upstream
/// Get/SaveGenericData code paths (including their Program.NoPersist and
/// MayCreateSessions gates). Only the SessionHandler constructor is bypassed —
/// it opens an on-disk LiteDB user database — so the generic data store is
/// swapped for an in-memory LiteDB collection.
/// </summary>
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
