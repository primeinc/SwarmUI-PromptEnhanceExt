using LiteDB;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Media;
using SwarmUI.Text2Image;

namespace PromptEnhance.Tests;

/// <summary>GenerationHistory over an in-memory LiteDB, fed the way SwarmUI feeds it: a per-output input clone through the generate event, SwarmUI's own ApplyMetadata for the saved file, then the batch event.</summary>
public class GenerationHistoryTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    public GenerationHistoryTests()
    {
        SwarmHost.EnsureInitialized();
        WebAPI.GenerationHistory.Detach();
        WebAPI.GenerationHistory.Close();
        WebAPI.GenerationHistory.Open(new LiteDatabase(new MemoryStream()));
    }

    public void Dispose()
    {
        WebAPI.GenerationHistory.Detach();
        WebAPI.GenerationHistory.Close();
    }

    private static Session UserWithHistory(int pastGenerations, string userId = "promptenhance_test_user")
    {
        Session session = SwarmHost.PermittedSession();
        session.User.Data.ID = userId;
        SwarmHost.SaveSettings(session, $$"""{"pastGenerations":{{pastGenerations}}}""");
        return session;
    }

    private static MediaFile Png(string base64) => ImageFile.FromDataString($"data:image/png;base64,{base64}");

    /// <summary>One output as SwarmUI's T2IEngine produces it: a per-output input clone (T2IEngine.cs:219) with its resolved prompt, the generate event (:227), then SwarmUI's ApplyMetadata for the saved file (:234).</summary>
    private static T2IEngine.ImageOutput GenerateOutput(T2IParamInput request, string prompt, string png, int index)
    {
        T2IParamInput perOutput = request.Clone();
        perOutput.Set(T2IParamTypes.Prompt, prompt);
        // A backend reads the parameters it generates from; SwarmUI's metadata lists only queried parameters (T2IParamInput.cs:418).
        perOutput.Get(T2IParamTypes.Prompt);
        perOutput.Get(T2IParamTypes.Seed, -1L);
        MediaFile file = Png(png);
        WebAPI.GenerationHistory.OnPostGenerate(new T2IEngine.PostGenerationEventParams(file, perOutput, () => { }));
        (Task<MediaFile> saved, string _) = perOutput.SourceSession.ApplyMetadata(file, perOutput, index, true);
        return new T2IEngine.ImageOutput { File = file, ActualFileTask = saved };
    }

    private static T2IEngine.ImageOutput[] GenerateOutputs(T2IParamInput request, params (string Prompt, string Png)[] outputs)
    {
        return [.. outputs.Select((output, index) => GenerateOutput(request, output.Prompt, output.Png, index))];
    }

    /// <summary>Runs one request through the history: prepare at the batch event, then record, as the worker does.</summary>
    private static async Task<T2IParamInput> Generate(Session session, DateTime at, params (string Prompt, string Png)[] outputs)
    {
        T2IParamInput request = new(session);
        WebAPI.GenerationHistory.PendingRequest pending = WebAPI.GenerationHistory.Prepare(request, GenerateOutputs(request, outputs), at);
        Xunit.Assert.NotNull(pending);
        await WebAPI.GenerationHistory.Record(pending);
        return request;
    }

    private static (int Width, int Height) Dimensions(string base64)
    {
        using SixLabors.ImageSharp.Image image = SixLabors.ImageSharp.Image.Load(Convert.FromBase64String(base64));
        return (image.Width, image.Height);
    }

    [Xunit.Fact]
    public async Task RecordedRequest_ComesBackWithEachOutputsOwnResolvedPromptImageAndSavedMetadata()
    {
        Session session = UserWithHistory(2);
        await Generate(session, T0, ("a red cat", SwarmHost.PngBase64), ("a blue cat", SwarmHost.PngBase64B));

        List<BackendSchema.PastGeneration> recent = WebAPI.GenerationHistory.Recent(session.User.UserID, 2);

        BackendSchema.PastGeneration generation = Xunit.Assert.Single(recent);
        Xunit.Assert.Equal(["a red cat", "a blue cat"], generation.Outputs.Select(output => output.Prompt));
        Xunit.Assert.Equal(["Past Generation 1 Output 1", "Past Generation 1 Output 2"], generation.Outputs.Select(output => output.Image.Label));
        Xunit.Assert.All(generation.Outputs, output => Xunit.Assert.Equal("image/jpeg", output.Image.MediaType));
        Xunit.Assert.All(generation.Outputs, output => Xunit.Assert.Equal((1, 1), Dimensions(output.Image.Data)));
        Xunit.Assert.Equal("a red cat", JObject.Parse(generation.Outputs[0].Metadata)["sui_image_params"]!["prompt"]!.Value<string>());
        Xunit.Assert.Equal("a blue cat", JObject.Parse(generation.Outputs[1].Metadata)["sui_image_params"]!["prompt"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task BatchOutputs_RecordTheSeedSwarmUISavedForEachOutput()
    {
        Session session = UserWithHistory(1);
        T2IParamInput request = new(session);
        request.Set(T2IParamTypes.BatchSize, 2);
        request.Set(T2IParamTypes.Seed, 100L);

        await WebAPI.GenerationHistory.Record(WebAPI.GenerationHistory.Prepare(request, GenerateOutputs(request, ("a", SwarmHost.PngBase64), ("b", SwarmHost.PngBase64B)), T0));

        BackendSchema.PastGeneration generation = Xunit.Assert.Single(WebAPI.GenerationHistory.Recent(session.User.UserID, 1));
        Xunit.Assert.Equal([100L, 101L], generation.Outputs.Select(output => JObject.Parse(output.Metadata)["sui_image_params"]!["seed"]!.Value<long>()));
    }

    [Xunit.Fact]
    public async Task OutputWithoutSavedMetadata_IsRecordedWithNullMetadata()
    {
        Session session = UserWithHistory(1);
        session.User.Settings.FileFormat.SaveMetadata = false;

        await Generate(session, T0, ("no metadata", SwarmHost.PngBase64));

        BackendSchema.PastGenerationOutput output = Xunit.Assert.Single(Xunit.Assert.Single(WebAPI.GenerationHistory.Recent(session.User.UserID, 1)).Outputs);
        Xunit.Assert.Equal("no metadata", output.Prompt);
        Xunit.Assert.Null(output.Metadata);
    }

    [Xunit.Fact]
    public async Task StoredImage_IsAJpegWithTheLongestEdgeCappedAndTheAspectKept()
    {
        Session session = UserWithHistory(1);
        await Generate(session, T0, ("wide", SwarmHost.Png(128, 2048, 1024)));

        BackendSchema.PastGenerationOutput output = Xunit.Assert.Single(Xunit.Assert.Single(WebAPI.GenerationHistory.Recent(session.User.UserID, 1)).Outputs);

        Xunit.Assert.Equal("image/jpeg", output.Image.MediaType);
        Xunit.Assert.Equal((WebAPI.GenerationHistory.MaxStoredEdge, WebAPI.GenerationHistory.MaxStoredEdge / 2), Dimensions(output.Image.Data));
    }

    [Xunit.Fact]
    public async Task EachRequest_KeepsOnlyItsFirstMaxOutputs()
    {
        Session session = UserWithHistory(1);
        (string, string)[] outputs = [.. Enumerable.Range(0, WebAPI.GenerationHistory.MaxOutputsPerRequest + 2).Select(i => ($"output {i}", SwarmHost.PngBase64))];
        await Generate(session, T0, outputs);

        BackendSchema.PastGeneration generation = Xunit.Assert.Single(WebAPI.GenerationHistory.Recent(session.User.UserID, 1));

        Xunit.Assert.Equal(Enumerable.Range(0, WebAPI.GenerationHistory.MaxOutputsPerRequest).Select(i => $"output {i}"), generation.Outputs.Select(output => output.Prompt));
    }

    [Xunit.Fact]
    public async Task Recent_ReturnsTheNewestRequestsOldestFirst_OrderedByTimeNotRequestId()
    {
        Session session = UserWithHistory(3);
        await Generate(session, T0.AddMinutes(2), ("second", SwarmHost.PngBase64));
        await Generate(session, T0.AddMinutes(1), ("first", SwarmHost.PngBase64));
        await Generate(session, T0.AddMinutes(3), ("third", SwarmHost.PngBase64));

        List<BackendSchema.PastGeneration> recent = WebAPI.GenerationHistory.Recent(session.User.UserID, 2);

        Xunit.Assert.Equal(["second", "third"], recent.Select(g => g.Outputs[0].Prompt));
        Xunit.Assert.Equal("Past Generation 1 Output 1", recent[0].Outputs[0].Image.Label);
        Xunit.Assert.Equal("Past Generation 2 Output 1", recent[1].Outputs[0].Image.Label);
    }

    [Xunit.Fact]
    public async Task EachUser_KeepsOnlyTheNewestMaxRequests()
    {
        Session session = UserWithHistory(10);
        for (int i = 0; i < WebAPI.GenerationHistory.MaxRequestsPerUser + 2; i++)
        {
            await Generate(session, T0.AddMinutes(i), ($"request {i}", SwarmHost.PngBase64));
        }

        List<BackendSchema.PastGeneration> recent = WebAPI.GenerationHistory.Recent(session.User.UserID, 100);

        Xunit.Assert.Equal(WebAPI.GenerationHistory.MaxRequestsPerUser, recent.Count);
        Xunit.Assert.Equal("request 2", recent[0].Outputs[0].Prompt);
    }

    [Xunit.Fact]
    public async Task Users_DoNotSeeEachOthersHistory()
    {
        Session alice = UserWithHistory(5);
        await Generate(alice, T0, ("alice's", SwarmHost.PngBase64));
        Session bob = UserWithHistory(5, "promptenhance_test_bob");

        Xunit.Assert.Empty(WebAPI.GenerationHistory.Recent(bob.User.UserID, 5));
        Xunit.Assert.Single(WebAPI.GenerationHistory.Recent(alice.User.UserID, 5));
    }

    [Xunit.Fact]
    public void NothingIsPrepared_WhilePastGenerationsIsZero()
    {
        Session session = UserWithHistory(0);
        T2IParamInput request = new(session);

        Xunit.Assert.Null(WebAPI.GenerationHistory.Prepare(request, GenerateOutputs(request, ("unrecorded", SwarmHost.PngBase64)), T0));
    }

    [Xunit.Fact]
    public async Task ARequestPreparedBeforeTheUserTurnedHistoryOff_IsNotRecordedAfterwards()
    {
        Session session = UserWithHistory(2);
        T2IParamInput request = new(session);
        WebAPI.GenerationHistory.PendingRequest pending = WebAPI.GenerationHistory.Prepare(request, GenerateOutputs(request, ("in flight", SwarmHost.PngBase64)), T0);

        JObject result = await WebAPI.SessionSettings.SavePromptEnhanceSettings(new JObject { ["settings"] = new JObject { ["pastGenerations"] = 0 } }, session);
        await WebAPI.GenerationHistory.Record(pending);

        Xunit.Assert.True(result["success"]!.Value<bool>());
        Xunit.Assert.Empty(WebAPI.GenerationHistory.Recent(session.User.UserID, 10));
    }

    [Xunit.Fact]
    public async Task TurningPastGenerationsOn_DuringARequest_RecordsIt()
    {
        Session session = UserWithHistory(0);
        T2IParamInput request = new(session);
        T2IEngine.ImageOutput[] images = GenerateOutputs(request, ("before the toggle", SwarmHost.PngBase64));
        SwarmHost.SaveSettings(session, """{"pastGenerations":2}""");

        await WebAPI.GenerationHistory.Record(WebAPI.GenerationHistory.Prepare(request, images, T0));

        Xunit.Assert.Single(WebAPI.GenerationHistory.Recent(session.User.UserID, 10));
    }

    [Xunit.Fact]
    public void NothingIsPrepared_ForADoNotSaveRequest()
    {
        Session session = UserWithHistory(2);
        T2IParamInput request = new(session);
        request.Set(T2IParamTypes.DoNotSave, true);

        Xunit.Assert.Null(WebAPI.GenerationHistory.Prepare(request, GenerateOutputs(request, ("x", SwarmHost.PngBase64)), T0));
    }

    [Xunit.Fact]
    public void NothingIsPrepared_WhenTheUserDoesNotSaveFiles()
    {
        Session session = UserWithHistory(2);
        session.User.Settings.SaveFiles = false;
        T2IParamInput request = new(session);

        Xunit.Assert.Null(WebAPI.GenerationHistory.Prepare(request, GenerateOutputs(request, ("x", SwarmHost.PngBase64)), T0));
    }

    [Xunit.Fact]
    public void GenerateEvent_DoesNotQueryTheInputSwarmUIBuildsMetadataFrom()
    {
        Session session = UserWithHistory(2);
        T2IParamInput perOutput = new(session);
        MediaFile file = Png(SwarmHost.PngBase64);

        WebAPI.GenerationHistory.OnPostGenerate(new T2IEngine.PostGenerationEventParams(file, perOutput, () => { }));

        Xunit.Assert.Empty(perOutput.ParamsQueried);
    }

    [Xunit.Fact]
    public void AnOutputWithoutAGenerateEventEntry_FailsLoudly()
    {
        Session session = UserWithHistory(2);
        MediaFile file = Png(SwarmHost.PngBase64);

        InvalidOperationException ex = Xunit.Assert.Throws<InvalidOperationException>(() =>
            WebAPI.GenerationHistory.Prepare(new T2IParamInput(session), [new T2IEngine.ImageOutput { File = file, ActualFileTask = Task.FromResult(file) }], T0));

        Xunit.Assert.Contains("without an entry from the generate event", ex.Message);
    }

    [Xunit.Fact]
    public void AnIntermediateOutput_IsNotCaptured()
    {
        Session session = UserWithHistory(2);
        T2IParamInput perOutput = new(session);
        perOutput.ExtraMeta["intermediate"] = "intermediate output";
        MediaFile file = Png(SwarmHost.PngBase64);
        WebAPI.GenerationHistory.OnPostGenerate(new T2IEngine.PostGenerationEventParams(file, perOutput, () => { }));

        Xunit.Assert.Throws<InvalidOperationException>(() =>
            WebAPI.GenerationHistory.Prepare(new T2IParamInput(session), [new T2IEngine.ImageOutput { File = file, ActualFileTask = Task.FromResult(file) }], T0));
    }

    [Xunit.Fact]
    public async Task RecordAndRecent_FailLoudly_WhenTheStoreIsClosed()
    {
        Session session = UserWithHistory(2);
        WebAPI.GenerationHistory.Close();

        await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => Generate(session, T0, ("x", SwarmHost.PngBase64)));
        Xunit.Assert.Throws<InvalidOperationException>(() => WebAPI.GenerationHistory.Recent(session.User.UserID, 1));
    }

    [Xunit.Fact]
    public void SwarmUIsOwnEvents_ReachTheHistory_AndDetachRecordsEverythingQueued()
    {
        Session session = UserWithHistory(2);
        WebAPI.GenerationHistory.Attach();
        T2IParamInput request = new(session);
        T2IParamInput perOutput = request.Clone();
        perOutput.Set(T2IParamTypes.Prompt, "through the events");
        MediaFile file = Png(SwarmHost.PngBase64);

        T2IEngine.PostGenerateEvent?.Invoke(new T2IEngine.PostGenerationEventParams(file, perOutput, () => { }));
        (Task<MediaFile> saved, string _) = session.ApplyMetadata(file, perOutput, 0, true);
        T2IEngine.PostBatchEvent?.Invoke(new T2IEngine.PostBatchEventParams(request, [new T2IEngine.ImageOutput { File = file, ActualFileTask = saved }]));
        WebAPI.GenerationHistory.Detach();

        BackendSchema.PastGeneration generation = Xunit.Assert.Single(WebAPI.GenerationHistory.Recent(session.User.UserID, 1));
        Xunit.Assert.Equal("through the events", Xunit.Assert.Single(generation.Outputs).Prompt);
    }

    [Xunit.Fact]
    public async Task SavingPastGenerationsAsZero_DeletesTheUsersHistory()
    {
        Session session = UserWithHistory(2);
        await Generate(session, T0, ("kept until turned off", SwarmHost.PngBase64));
        Session other = UserWithHistory(2, "promptenhance_test_other");
        await Generate(other, T0, ("someone else's", SwarmHost.PngBase64));

        JObject result = await WebAPI.SessionSettings.SavePromptEnhanceSettings(new JObject { ["settings"] = new JObject { ["pastGenerations"] = 0 } }, session);

        Xunit.Assert.True(result["success"]!.Value<bool>());
        Xunit.Assert.Empty(WebAPI.GenerationHistory.Recent(session.User.UserID, 10));
        Xunit.Assert.Single(WebAPI.GenerationHistory.Recent(other.User.UserID, 10));
    }

    [Xunit.Fact]
    public async Task ResettingSettings_DeletesTheUsersHistory()
    {
        Session session = UserWithHistory(2);
        await Generate(session, T0, ("reset me", SwarmHost.PngBase64));

        JObject result = await WebAPI.SessionSettings.ResetPromptEnhanceSettings(session);

        Xunit.Assert.True(result["success"]!.Value<bool>());
        Xunit.Assert.Empty(WebAPI.GenerationHistory.Recent(session.User.UserID, 10));
    }

    [Xunit.Fact]
    public async Task Enhance_WithoutAModel_IsModelMissing_BeforeTheHistoryIsConsulted()
    {
        Session session = SwarmHost.PermittedSession();
        SwarmHost.SaveSettings(session, """{"baseUrl":"http://127.0.0.1:9","model":"","pastGenerations":2}""");
        WebAPI.GenerationHistory.Close();

        JObject result = await WebAPI.BackendClient.PromptEnhanceRun(session, new JObject { ["prompt"] = "a cat" });

        Xunit.Assert.Equal("model_missing", result["error_id"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task Enhance_WithPastGenerationsOnAndTheStoreClosed_SaysSo()
    {
        Session session = SwarmHost.PermittedSession();
        SwarmHost.SaveSettings(session, """{"baseUrl":"http://127.0.0.1:9","model":"m","pastGenerations":2}""");
        WebAPI.GenerationHistory.Close();

        JObject result = await WebAPI.BackendClient.PromptEnhanceRun(session, new JObject { ["prompt"] = "a cat" });

        Xunit.Assert.Equal("generic", result["error_id"]!.Value<string>());
        Xunit.Assert.Contains("history store did not open", result["error"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task Enhance_WithTheBackendDown_IsServerUnavailable_WithPastGenerationsOn()
    {
        Session session = UserWithHistory(2);
        await Generate(session, T0, ("recorded", SwarmHost.PngBase64));
        SwarmHost.SaveSettings(session, """{"baseUrl":"http://127.0.0.1:9","model":"m","pastGenerations":2}""");

        JObject result = await WebAPI.BackendClient.PromptEnhanceRun(session, new JObject { ["prompt"] = "a cat" });

        Xunit.Assert.Equal("server_unavailable", result["error_id"]!.Value<string>());
    }

    [Xunit.Fact]
    public async Task TurningPastGenerationsOn_IsRejected_WhenTheStoreDidNotOpen()
    {
        Session session = UserWithHistory(0);
        WebAPI.GenerationHistory.Close();

        JObject result = await WebAPI.SessionSettings.SavePromptEnhanceSettings(new JObject { ["settings"] = new JObject { ["pastGenerations"] = 1 } }, session);

        Xunit.Assert.False(result["success"]!.Value<bool>());
        Xunit.Assert.Contains("history store did not open", result["error"]!.Value<string>());
    }
}
