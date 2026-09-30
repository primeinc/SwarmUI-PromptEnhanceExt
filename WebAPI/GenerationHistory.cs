using System.IO;
using System.Runtime.CompilerServices;
using LiteDB;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Text2Image;
using SwarmUI.Utils;

namespace PromptEnhance.WebAPI;

/// <summary>
/// Per-user record of the most recent finished SwarmUI generation requests, the source of the Past Generations context channel.
/// Stored in its own LiteDB file (records plus image bytes in LiteDB file storage), so it survives page reloads and server restarts.
/// A request is recorded only when the user's Past Generations setting is above 0, SwarmUI saves files for the user, and the request did not set Do Not Save.
/// Only still-image outputs are recorded. Each user keeps the newest <see cref="MaxRequestsPerUser"/> requests.
/// </summary>
public static class GenerationHistory
{
    /// <summary>Requests kept per user: the contract maximum of the pastGenerations setting.</summary>
    public const int MaxRequestsPerUser = 10;

    /// <summary>One recorded output.</summary>
    public class OutputEntry
    {
        /// <summary>Id of the image bytes in the database's file storage.</summary>
        public string FileId { get; set; }

        /// <summary>The image MIME type.</summary>
        public string MediaType { get; set; }

        /// <summary>The prompt SwarmUI generated this output from, after wildcard and random-tag resolution.</summary>
        public string Prompt { get; set; }

        /// <summary>SwarmUI's raw generation metadata JSON for this output.</summary>
        public string Metadata { get; set; }
    }

    /// <summary>One recorded generation request.</summary>
    public class RequestEntry
    {
        /// <summary>Database id.</summary>
        public ObjectId Id { get; set; }

        /// <summary>The SwarmUI user id.</summary>
        public string UserId { get; set; }

        /// <summary>When the request finished, in UTC. Orders records: SwarmUI request ids restart on every launch.</summary>
        public DateTime RecordedAt { get; set; }

        /// <summary>SwarmUI's request id, unique only within one server run.</summary>
        public long SwarmRequestId { get; set; }

        /// <summary>The recorded outputs, in the order SwarmUI produced them.</summary>
        public List<OutputEntry> Outputs { get; set; } = [];
    }

    /// <summary>What <see cref="OnPostGenerate"/> captured for one output, keyed by its <see cref="MediaFile"/> instance until the request's batch event.</summary>
    private record class CapturedOutput(string Prompt, string Metadata);

    /// <summary>Per-output captures, keyed by object identity; entries disappear with their <see cref="MediaFile"/>.</summary>
    private static readonly ConditionalWeakTable<MediaFile, CapturedOutput> Captured = [];

    /// <summary>Guards <see cref="Database"/> and <see cref="Requests"/>.</summary>
    private static readonly object DatabaseLock = new();

    private static LiteDatabase Database;

    private static ILiteCollection<RequestEntry> Requests;

    /// <summary>The database file under SwarmUI's data directory.</summary>
    public static string DefaultPath => Path.Combine(Program.DataDir, "PromptEnhance", "history.ldb");

    /// <summary>Opens the store: a file path, or an in-memory stream for tests.</summary>
    public static void Open(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        Open(new LiteDatabase(path));
    }

    /// <summary>Opens the store over an already-constructed database.</summary>
    public static void Open(LiteDatabase database)
    {
        lock (DatabaseLock)
        {
            if (Database != null)
            {
                throw new InvalidOperationException("The PromptEnhance generation history is already open.");
            }
            Database = database;
            Requests = Database.GetCollection<RequestEntry>("requests");
            Requests.EnsureIndex(r => r.UserId);
        }
    }

    /// <summary>Closes the store. Safe to call when it is not open.</summary>
    public static void Close()
    {
        lock (DatabaseLock)
        {
            Database?.Dispose();
            Database = null;
            Requests = null;
        }
    }

    /// <summary>Subscribes to SwarmUI's generation events.</summary>
    public static void Attach()
    {
        T2IEngine.PostGenerateEvent += OnPostGenerate;
        T2IEngine.PostBatchEvent += OnPostBatch;
    }

    /// <summary>Unsubscribes from SwarmUI's generation events.</summary>
    public static void Detach()
    {
        T2IEngine.PostGenerateEvent -= OnPostGenerate;
        T2IEngine.PostBatchEvent -= OnPostBatch;
    }

    /// <summary>True when this request should be recorded; see the class summary for the rules.</summary>
    public static bool ShouldRecord(T2IParamInput input)
    {
        Session session = input.SourceSession;
        if (session?.User == null || !session.User.Settings.SaveFiles || input.Get(T2IParamTypes.DoNotSave, false))
        {
            return false;
        }
        return SessionSettings.Effective(session, out _)["pastGenerations"].Value<int>() > 0;
    }

    /// <summary>Captures one output's resolved prompt and metadata. <see cref="T2IEngine.PostGenerateEvent"/> gives the per-output input after prompt resolution; the batch event does not.
    /// SwarmUI invokes this unguarded inside the backend's output loop, so a failure is logged as an error and never thrown into SwarmUI.</summary>
    public static void OnPostGenerate(T2IEngine.PostGenerationEventParams generated)
    {
        try
        {
            if (generated.File?.Type?.MetaType != MediaMetaType.Image || !ShouldRecord(generated.UserInput))
            {
                return;
            }
            T2IParamInput snapshot = generated.UserInput.Clone();
            Captured.AddOrUpdate(generated.File, new CapturedOutput(snapshot.Get(T2IParamTypes.Prompt, ""), snapshot.GenRawMetadata()));
        }
        catch (Exception ex)
        {
            Logs.Error($"[PromptEnhance] Could not capture a generated output for the Past Generations history: {ex}");
        }
    }

    /// <summary>Records a finished request. SwarmUI invokes <see cref="T2IEngine.PostBatchEvent"/> unguarded, so a failure is logged as an error and never thrown into SwarmUI.</summary>
    public static void OnPostBatch(T2IEngine.PostBatchEventParams batch)
    {
        try
        {
            Record(batch.UserInput, batch.Images, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            Logs.Error($"[PromptEnhance] Could not record generation request {batch.UserInput?.UserRequestId} in the Past Generations history: {ex}");
        }
    }

    /// <summary>Records the still-image outputs of one finished request and prunes the user's history to <see cref="MaxRequestsPerUser"/>. Throws when an output has no capture from <see cref="OnPostGenerate"/>.</summary>
    public static void Record(T2IParamInput input, T2IEngine.ImageOutput[] images, DateTime recordedAt)
    {
        if (!ShouldRecord(input))
        {
            return;
        }
        List<(MediaFile File, CapturedOutput Captured)> outputs = [];
        foreach (T2IEngine.ImageOutput image in images)
        {
            if (image.File.Type.MetaType != MediaMetaType.Image)
            {
                continue;
            }
            if (!Captured.TryGetValue(image.File, out CapturedOutput captured))
            {
                throw new InvalidOperationException($"An output of request {input.UserRequestId} reached the batch event without a capture from the generate event.");
            }
            MediaFile final = image.ActualFileTask?.GetAwaiter().GetResult() ?? throw new InvalidOperationException($"An output of request {input.UserRequestId} has no final file.");
            outputs.Add((final, captured));
        }
        if (outputs.Count == 0)
        {
            return;
        }
        string userId = input.SourceSession.User.UserID;
        lock (DatabaseLock)
        {
            if (Database == null)
            {
                throw new InvalidOperationException("The PromptEnhance generation history is not open.");
            }
            RequestEntry entry = new() { Id = ObjectId.NewObjectId(), UserId = userId, RecordedAt = recordedAt, SwarmRequestId = input.UserRequestId };
            for (int i = 0; i < outputs.Count; i++)
            {
                string fileId = $"{entry.Id}/{i}";
                using MemoryStream stream = new(outputs[i].File.RawData);
                Database.FileStorage.Upload(fileId, $"{fileId}.{outputs[i].File.Type.Extension}", stream);
                entry.Outputs.Add(new OutputEntry { FileId = fileId, MediaType = outputs[i].File.Type.MimeType, Prompt = outputs[i].Captured.Prompt, Metadata = outputs[i].Captured.Metadata });
            }
            Requests.Insert(entry);
            foreach (RequestEntry old in Requests.Find(r => r.UserId == userId).OrderByDescending(r => r.RecordedAt).Skip(MaxRequestsPerUser).ToList())
            {
                foreach (OutputEntry output in old.Outputs)
                {
                    Database.FileStorage.Delete(output.FileId);
                }
                Requests.Delete(old.Id);
            }
        }
    }

    /// <summary>The user's newest <paramref name="count"/> recorded requests, oldest first, with image bytes. Fewer when fewer are recorded. Throws when a record's image bytes are missing.</summary>
    public static List<BackendSchema.PastGeneration> Recent(string userId, int count)
    {
        lock (DatabaseLock)
        {
            if (Database == null)
            {
                throw new InvalidOperationException("The PromptEnhance generation history is not open.");
            }
            List<RequestEntry> newest = [.. Requests.Find(r => r.UserId == userId).OrderByDescending(r => r.RecordedAt).Take(count)];
            newest.Reverse();
            List<BackendSchema.PastGeneration> result = [];
            for (int g = 0; g < newest.Count; g++)
            {
                BackendSchema.PastGeneration generation = new() { RecordedAt = newest[g].RecordedAt, SwarmRequestId = newest[g].SwarmRequestId };
                for (int o = 0; o < newest[g].Outputs.Count; o++)
                {
                    OutputEntry output = newest[g].Outputs[o];
                    if (!Database.FileStorage.Exists(output.FileId))
                    {
                        throw new InvalidOperationException($"The Past Generations history is missing image {output.FileId}.");
                    }
                    using MemoryStream bytes = new();
                    Database.FileStorage.Download(output.FileId, bytes);
                    generation.Outputs.Add(new BackendSchema.PastGenerationOutput
                    {
                        Image = new BackendSchema.MediaContent { Data = Convert.ToBase64String(bytes.ToArray()), MediaType = output.MediaType, Label = $"Past Generation {g + 1} Output {o + 1}" },
                        Prompt = output.Prompt,
                        Metadata = output.Metadata
                    });
                }
                result.Add(generation);
            }
            return result;
        }
    }
}
