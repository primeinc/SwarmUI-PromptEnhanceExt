using LiteDB;
using SwarmUI.Accounts;
using SwarmUI.Media;
using SwarmUI.Text2Image;

namespace PromptEnhance.Tests;

/// <summary>GenerationHistory over an in-memory LiteDB, driven through the same event handlers SwarmUI invokes.</summary>
public class GenerationHistoryTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    public GenerationHistoryTests()
    {
        SwarmHost.EnsureInitialized();
        WebAPI.GenerationHistory.Close();
        WebAPI.GenerationHistory.Open(new LiteDatabase(new MemoryStream()));
    }

    public void Dispose()
    {
        WebAPI.GenerationHistory.Close();
    }

    private static Session UserWithHistory(int pastGenerations)
    {
        Session session = SwarmHost.PermittedSession();
        SwarmHost.SaveSettings(session, $$"""{"pastGenerations":{{pastGenerations}}}""");
        return session;
    }

    private static MediaFile Png(string base64) => ImageFile.FromDataString($"data:image/png;base64,{base64}");

    /// <summary>Runs one request through the generate event (once per output, with that output's resolved prompt) and then the batch event, as SwarmUI does.</summary>
    private static T2IParamInput Generate(Session session, DateTime at, params (string Prompt, string Png)[] outputs)
    {
        T2IParamInput request = new(session);
        List<T2IEngine.ImageOutput> images = [];
        foreach ((string prompt, string png) in outputs)
        {
            T2IParamInput perOutput = request.Clone();
            perOutput.Set(T2IParamTypes.Prompt, prompt);
            MediaFile file = Png(png);
            WebAPI.GenerationHistory.OnPostGenerate(new T2IEngine.PostGenerationEventParams(file, perOutput, () => { }));
            images.Add(new T2IEngine.ImageOutput { File = file, ActualFileTask = Task.FromResult(file) });
        }
        WebAPI.GenerationHistory.Record(request, [.. images], at);
        return request;
    }

    [Xunit.Fact]
    public void RecordedRequest_ComesBackWithEachOutputsOwnResolvedPromptImageAndMetadata()
    {
        Session session = UserWithHistory(2);
        T2IParamInput request = Generate(session, T0, ("a red cat", SwarmHost.PngBase64), ("a blue cat", SwarmHost.PngBase64B));

        List<BackendSchema.PastGeneration> recent = WebAPI.GenerationHistory.Recent(session.User.UserID, 2);

        BackendSchema.PastGeneration generation = Xunit.Assert.Single(recent);
        Xunit.Assert.Equal(request.UserRequestId, generation.SwarmRequestId);
        Xunit.Assert.Equal(["a red cat", "a blue cat"], generation.Outputs.Select(output => output.Prompt));
        Xunit.Assert.Equal([SwarmHost.PngBase64, SwarmHost.PngBase64B], generation.Outputs.Select(output => output.Image.Data));
        Xunit.Assert.Equal(["Past Generation 1 Output 1", "Past Generation 1 Output 2"], generation.Outputs.Select(output => output.Image.Label));
        Xunit.Assert.All(generation.Outputs, output => Xunit.Assert.Equal("image/png", output.Image.MediaType));
        Xunit.Assert.Contains("a red cat", generation.Outputs[0].Metadata);
    }

    [Xunit.Fact]
    public void Recent_ReturnsTheNewestRequestsOldestFirst_OrderedByTimeNotRequestId()
    {
        Session session = UserWithHistory(3);
        Generate(session, T0.AddMinutes(2), ("second", SwarmHost.PngBase64));
        Generate(session, T0.AddMinutes(1), ("first", SwarmHost.PngBase64));
        Generate(session, T0.AddMinutes(3), ("third", SwarmHost.PngBase64));

        List<BackendSchema.PastGeneration> recent = WebAPI.GenerationHistory.Recent(session.User.UserID, 2);

        Xunit.Assert.Equal(["second", "third"], recent.Select(g => g.Outputs[0].Prompt));
        Xunit.Assert.Equal("Past Generation 1 Output 1", recent[0].Outputs[0].Image.Label);
        Xunit.Assert.Equal("Past Generation 2 Output 1", recent[1].Outputs[0].Image.Label);
    }

    [Xunit.Fact]
    public void EachUser_KeepsOnlyTheNewestMaxRequests()
    {
        Session session = UserWithHistory(10);
        for (int i = 0; i < WebAPI.GenerationHistory.MaxRequestsPerUser + 2; i++)
        {
            Generate(session, T0.AddMinutes(i), ($"request {i}", SwarmHost.PngBase64));
        }

        List<BackendSchema.PastGeneration> recent = WebAPI.GenerationHistory.Recent(session.User.UserID, 100);

        Xunit.Assert.Equal(WebAPI.GenerationHistory.MaxRequestsPerUser, recent.Count);
        Xunit.Assert.Equal("request 2", recent[0].Outputs[0].Prompt);
    }

    [Xunit.Fact]
    public void Users_DoNotSeeEachOthersHistory()
    {
        Session alice = UserWithHistory(5);
        Generate(alice, T0, ("alice's", SwarmHost.PngBase64));
        Session bob = UserWithHistory(5);
        bob.User.Data.ID = "promptenhance_test_bob";

        Xunit.Assert.Empty(WebAPI.GenerationHistory.Recent(bob.User.UserID, 5));
        Xunit.Assert.Single(WebAPI.GenerationHistory.Recent(alice.User.UserID, 5));
    }

    [Xunit.Fact]
    public void NothingIsRecorded_WhilePastGenerationsIsZero()
    {
        Session session = UserWithHistory(0);
        Generate(session, T0, ("unrecorded", SwarmHost.PngBase64));

        Xunit.Assert.Empty(WebAPI.GenerationHistory.Recent(session.User.UserID, 10));
    }

    [Xunit.Fact]
    public void NothingIsRecorded_ForADoNotSaveRequest()
    {
        Session session = UserWithHistory(2);
        T2IParamInput request = new(session);
        request.Set(T2IParamTypes.DoNotSave, true);

        Xunit.Assert.False(WebAPI.GenerationHistory.ShouldRecord(request));
    }

    [Xunit.Fact]
    public void NothingIsRecorded_WhenTheUserDoesNotSaveFiles()
    {
        Session session = UserWithHistory(2);
        session.User.Settings.SaveFiles = false;

        Xunit.Assert.False(WebAPI.GenerationHistory.ShouldRecord(new T2IParamInput(session)));
    }

    [Xunit.Fact]
    public void AnOutputWithoutAGenerateEventCapture_FailsLoudly()
    {
        Session session = UserWithHistory(2);
        MediaFile file = Png(SwarmHost.PngBase64);

        InvalidOperationException ex = Xunit.Assert.Throws<InvalidOperationException>(() =>
            WebAPI.GenerationHistory.Record(new T2IParamInput(session), [new T2IEngine.ImageOutput { File = file, ActualFileTask = Task.FromResult(file) }], T0));

        Xunit.Assert.Contains("without a capture", ex.Message);
    }

    [Xunit.Fact]
    public void RecordAndRecent_FailLoudly_WhenTheStoreIsClosed()
    {
        Session session = UserWithHistory(2);
        WebAPI.GenerationHistory.Close();

        Xunit.Assert.Throws<InvalidOperationException>(() => Generate(session, T0, ("x", SwarmHost.PngBase64)));
        Xunit.Assert.Throws<InvalidOperationException>(() => WebAPI.GenerationHistory.Recent(session.User.UserID, 1));
    }
}
