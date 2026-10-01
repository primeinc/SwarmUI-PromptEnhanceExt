using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LiteDB;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp.Processing;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Text2Image;
using SwarmUI.Utils;

namespace PromptEnhance.WebAPI;

/// <summary>
/// Per-user record of the most recent finished SwarmUI generation requests, the source of the Past Generations context channel.
/// Stored in its own LiteDB file (records plus image bytes in LiteDB file storage), so it survives page reloads and server restarts.
/// A request is recorded only when SwarmUI saves files for the user, the request did not set Do Not Save, and the user's Past Generations setting is above 0 when the request is recorded.
/// Only still-image outputs are recorded: the first <see cref="MaxOutputsPerRequest"/> of each request, stored as JPEG with the longest edge at most <see cref="MaxStoredEdge"/> pixels, with the metadata SwarmUI saved in the output file.
/// Each user keeps the newest <see cref="MaxRequestsPerUser"/> requests. <see cref="Forget"/> deletes a user's history.
/// Recording runs on one background worker, off SwarmUI's generation path.
/// </summary>
public static class GenerationHistory
{
    /// <summary>Requests kept per user: the contract maximum of the pastGenerations setting.</summary>
    public const int MaxRequestsPerUser = 10;

    /// <summary>Outputs kept per recorded request, in the order SwarmUI produced them.</summary>
    public const int MaxOutputsPerRequest = 4;

    /// <summary>Longest edge, in pixels, of a stored output image.</summary>
    public const int MaxStoredEdge = 1024;

    /// <summary>One recorded output.</summary>
    public class OutputEntry
    {
        /// <summary>Id of the image bytes in the database's file storage.</summary>
        public string FileId { get; set; }

        /// <summary>The image MIME type.</summary>
        public string MediaType { get; set; }

        /// <summary>The prompt SwarmUI generated this output from, after wildcard and random-tag resolution.</summary>
        public string Prompt { get; set; }

        /// <summary>The raw SwarmUI metadata saved in the output file; null when SwarmUI saved none (the user's Save Metadata setting is off).</summary>
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

    /// <summary>One output of a finished request: the task SwarmUI resolves to the saved file, and the resolved prompt.</summary>
    public record class PendingOutput(Task<MediaFile> SavedFile, string Prompt);

    /// <summary>A finished request, read from SwarmUI's event on the request thread so the worker touches neither SwarmUI's live parameter input nor its session store (which SwarmUI disposes before extensions shut down). <paramref name="Epoch"/> is the user's <see cref="Epochs"/> value when it was prepared.</summary>
    public record class PendingRequest(string UserId, long Epoch, long SwarmRequestId, DateTime RecordedAt, List<PendingOutput> Outputs);

    /// <summary>The per-output input SwarmUI handed <see cref="T2IEngine.PostGenerateEvent"/> for each final output, keyed by its <see cref="MediaFile"/> instance; entries disappear with the file.</summary>
    private static readonly ConditionalWeakTable<MediaFile, T2IParamInput> Captured = [];

    /// <summary>Per-user count of <see cref="Forget"/> calls. A request prepared before the user's latest Forget is not recorded.</summary>
    private static readonly ConcurrentDictionary<string, long> Epochs = [];

    /// <summary>Guards <see cref="Database"/> and <see cref="Requests"/>.</summary>
    private static readonly object DatabaseLock = new();

    private static LiteDatabase Database;

    private static ILiteCollection<RequestEntry> Requests;

    /// <summary>Finished requests queued by <see cref="OnPostBatch"/> while attached.</summary>
    private static Channel<PendingRequest> Queue;

    /// <summary>The single reader of <see cref="Queue"/>.</summary>
    private static Task Worker;

    /// <summary>The database file under SwarmUI's data directory.</summary>
    public static string DefaultPath => Path.Combine(Program.DataDir, "PromptEnhance", "history.ldb");

    /// <summary>True while the store is open.</summary>
    public static bool IsOpen
    {
        get
        {
            lock (DatabaseLock)
            {
                return Database != null;
            }
        }
    }

    /// <summary>Opens the store at a file path.</summary>
    public static void Open(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        Open(new LiteDatabase(new ConnectionString { Filename = path }));
    }

    /// <summary>Opens the store over an already-constructed database, reading dates back as UTC.</summary>
    public static void Open(LiteDatabase database)
    {
        lock (DatabaseLock)
        {
            if (Database != null)
            {
                throw new InvalidOperationException("The PromptEnhance generation history is already open.");
            }
            database.UtcDate = true;
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

    /// <summary>Starts the worker and subscribes to SwarmUI's generation events.</summary>
    public static void Attach()
    {
        if (Queue != null)
        {
            throw new InvalidOperationException("The PromptEnhance generation history is already attached.");
        }
        Queue = Channel.CreateUnbounded<PendingRequest>(new UnboundedChannelOptions { SingleReader = true });
        Worker = Task.Run(() => Drain(Queue.Reader));
        T2IEngine.PostGenerateEvent += OnPostGenerate;
        T2IEngine.PostBatchEvent += OnPostBatch;
    }

    /// <summary>How long <see cref="Detach"/> waits for queued requests to be recorded.</summary>
    public static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Unsubscribes from SwarmUI's generation events, then waits up to <see cref="DrainTimeout"/> for the worker to record every queued request. Safe to call when not attached.</summary>
    public static void Detach()
    {
        T2IEngine.PostGenerateEvent -= OnPostGenerate;
        T2IEngine.PostBatchEvent -= OnPostBatch;
        if (Queue == null)
        {
            return;
        }
        Queue.Writer.Complete();
        if (!Worker.Wait(DrainTimeout))
        {
            Logs.Error($"[PromptEnhance] The Past Generations history worker did not finish within {DrainTimeout.TotalSeconds} seconds; {Queue.Reader.Count} queued requests are not recorded.");
        }
        Queue = null;
        Worker = null;
    }

    /// <summary>Records queued requests one at a time until the queue is completed. A failed request is logged as an error and the worker continues.</summary>
    private static async Task Drain(ChannelReader<PendingRequest> reader)
    {
        await foreach (PendingRequest pending in reader.ReadAllAsync())
        {
            try
            {
                await Record(pending);
            }
            catch (Exception ex)
            {
                Logs.Error($"[PromptEnhance] Could not record generation request {pending.SwarmRequestId} in the Past Generations history: {ex}");
            }
        }
    }

    /// <summary>Remembers which input produced each final still-image output. <see cref="T2IEngine.PostGenerateEvent"/> hands every subscriber its own per-output copy of the input, after prompt resolution; the batch event does not.
    /// Runs on SwarmUI's backend output loop before SwarmUI builds the output's metadata from that same input, so it reads nothing from the input and only stores a reference. Intermediate outputs never reach the batch event and are skipped.</summary>
    public static void OnPostGenerate(T2IEngine.PostGenerationEventParams generated)
    {
        if (generated.File?.Type?.MetaType != MediaMetaType.Image || generated.UserInput == null || generated.UserInput.ExtraMeta.ContainsKey("intermediate"))
        {
            return;
        }
        Captured.AddOrUpdate(generated.File, generated.UserInput);
    }

    /// <summary>Queues a finished request for the worker. SwarmUI invokes <see cref="T2IEngine.PostBatchEvent"/> unguarded on the generation request's path, so this never throws and never waits; a failure is logged as an error.</summary>
    public static void OnPostBatch(T2IEngine.PostBatchEventParams batch)
    {
        try
        {
            PendingRequest pending = Prepare(batch.UserInput, batch.Images, DateTime.UtcNow);
            if (pending != null && Queue?.Writer.TryWrite(pending) != true)
            {
                Logs.Info($"[PromptEnhance] Generation request {pending.SwarmRequestId} finished while the Past Generations history was shutting down; it is not recorded.");
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"[PromptEnhance] Could not queue generation request {batch.UserInput?.UserRequestId} for the Past Generations history: {ex}");
        }
    }

    /// <summary>Reads what the worker needs from a finished request, on the thread SwarmUI raised the batch event on (every output is saved by then): the first <see cref="MaxOutputsPerRequest"/> still-image outputs with their resolved prompts.
    /// Null when the request is not to be recorded: SwarmUI does not save files for the user, the request set Do Not Save, or the user's Past Generations setting is 0. Throws when an output has no entry from <see cref="OnPostGenerate"/>: the two events failed to pair.</summary>
    public static PendingRequest Prepare(T2IParamInput input, T2IEngine.ImageOutput[] images, DateTime recordedAt)
    {
        Session session = input.SourceSession;
        if (session?.User == null || !session.User.Settings.SaveFiles || input.Get(T2IParamTypes.DoNotSave, false))
        {
            return null;
        }
        string userId = session.User.UserID;
        // Epoch before setting: saving 0 persists before Forget bumps the epoch, so a request is refused either here or by Record's epoch check.
        long epoch = Epochs.GetValueOrDefault(userId);
        if (SessionSettings.Effective(session, out _)["pastGenerations"].Value<int>() <= 0)
        {
            return null;
        }
        List<PendingOutput> outputs = [];
        foreach (T2IEngine.ImageOutput image in images.Where(i => i.File.Type.MetaType == MediaMetaType.Image).Take(MaxOutputsPerRequest))
        {
            if (!Captured.TryGetValue(image.File, out T2IParamInput perOutput))
            {
                throw new InvalidOperationException($"An output of request {input.UserRequestId} reached the batch event without an entry from the generate event.");
            }
            Task<MediaFile> savedFile = image.ActualFileTask ?? throw new InvalidOperationException($"An output of request {input.UserRequestId} has no saved file.");
            outputs.Add(new PendingOutput(savedFile, perOutput.Get(T2IParamTypes.Prompt, "")));
        }
        return outputs.Count == 0 ? null : new PendingRequest(userId, epoch, input.UserRequestId, recordedAt, outputs);
    }

    /// <summary>JPEG bytes of <paramref name="image"/> with the longest edge at most <see cref="MaxStoredEdge"/>, encoded once with SwarmUI's JPEG helper.</summary>
    private static byte[] StoredJpeg(ImageFile image)
    {
        SixLabors.ImageSharp.Image source = image.ToIS;
        int longest = Math.Max(source.Width, source.Height);
        if (longest <= MaxStoredEdge)
        {
            return ImageFile.ISImgToJpgBytes(source);
        }
        double scale = (double)MaxStoredEdge / longest;
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        using SixLabors.ImageSharp.Image resized = source.Clone(i => i.Resize(width, height));
        return ImageFile.ISImgToJpgBytes(resized);
    }

    /// <summary>Records one prepared request, unless the user's history was forgotten since it was prepared, and prunes the user's history to <see cref="MaxRequestsPerUser"/>.
    /// Throws when SwarmUI failed to produce an output's saved file or the store is not open.</summary>
    public static async Task Record(PendingRequest pending)
    {
        List<(OutputEntry Entry, byte[] Jpeg)> outputs = [];
        foreach (PendingOutput output in pending.Outputs)
        {
            ImageFile saved = await output.SavedFile as ImageFile ?? throw new InvalidOperationException($"SwarmUI produced no saved image file for an output of request {pending.SwarmRequestId}.");
            outputs.Add((new OutputEntry { MediaType = MediaType.ImageJpg.MimeType, Prompt = output.Prompt, Metadata = saved.GetMetadata() }, StoredJpeg(saved)));
        }
        string userId = pending.UserId;
        lock (DatabaseLock)
        {
            if (Database == null)
            {
                throw new InvalidOperationException("The PromptEnhance generation history is not open.");
            }
            if (Epochs.GetValueOrDefault(userId) != pending.Epoch)
            {
                return;
            }
            RequestEntry entry = new() { Id = ObjectId.NewObjectId(), UserId = userId, RecordedAt = pending.RecordedAt, SwarmRequestId = pending.SwarmRequestId };
            for (int i = 0; i < outputs.Count; i++)
            {
                (OutputEntry output, byte[] jpeg) = outputs[i];
                output.FileId = $"{entry.Id}/{i}";
                using MemoryStream stream = new(jpeg);
                Database.FileStorage.Upload(output.FileId, $"{output.FileId}.jpg", stream);
                entry.Outputs.Add(output);
            }
            Requests.Insert(entry);
            foreach (RequestEntry old in Requests.Find(r => r.UserId == userId).OrderByDescending(r => r.RecordedAt).Skip(MaxRequestsPerUser).ToList())
            {
                Delete(old);
            }
        }
    }

    /// <summary>Deletes one record and its image bytes. Caller holds <see cref="DatabaseLock"/>.</summary>
    private static void Delete(RequestEntry entry)
    {
        foreach (OutputEntry output in entry.Outputs)
        {
            Database.FileStorage.Delete(output.FileId);
        }
        Requests.Delete(entry.Id);
    }

    /// <summary>Deletes every record of <paramref name="userId"/> with its image bytes, and every request of that user still being recorded. Throws when the store is not open.</summary>
    public static void Forget(string userId)
    {
        lock (DatabaseLock)
        {
            if (Database == null)
            {
                throw new InvalidOperationException("The PromptEnhance generation history is not open.");
            }
            Epochs.AddOrUpdate(userId, 1, (_, epoch) => epoch + 1);
            foreach (RequestEntry entry in Requests.Find(r => r.UserId == userId).ToList())
            {
                Delete(entry);
            }
        }
    }

    /// <summary>The user's newest <paramref name="count"/> recorded requests, oldest first, with image bytes. Fewer when fewer are recorded. Throws when a record's image bytes are missing or the store is not open.</summary>
    public static List<BackendSchema.PastGeneration> Recent(string userId, int count)
    {
        List<(OutputEntry Output, byte[] Bytes)[]> newest = [];
        lock (DatabaseLock)
        {
            if (Database == null)
            {
                throw new InvalidOperationException("The PromptEnhance generation history is not open.");
            }
            foreach (RequestEntry entry in Requests.Find(r => r.UserId == userId).OrderByDescending(r => r.RecordedAt).Take(count))
            {
                newest.Add([.. entry.Outputs.Select(output => (output, Download(output.FileId)))]);
            }
        }
        newest.Reverse();
        List<BackendSchema.PastGeneration> result = [];
        for (int g = 0; g < newest.Count; g++)
        {
            BackendSchema.PastGeneration generation = new();
            for (int o = 0; o < newest[g].Length; o++)
            {
                (OutputEntry output, byte[] bytes) = newest[g][o];
                generation.Outputs.Add(new BackendSchema.PastGenerationOutput
                {
                    Image = new BackendSchema.MediaContent { Data = Convert.ToBase64String(bytes), MediaType = output.MediaType, Label = $"Past Generation {g + 1} Output {o + 1}" },
                    Prompt = output.Prompt,
                    Metadata = output.Metadata
                });
            }
            result.Add(generation);
        }
        return result;
    }

    /// <summary>Reads one stored image into an exactly sized buffer. Caller holds <see cref="DatabaseLock"/>.</summary>
    private static byte[] Download(string fileId)
    {
        LiteFileInfo<string> info = Database.FileStorage.FindById(fileId) ?? throw new InvalidOperationException($"The Past Generations history is missing image {fileId}.");
        byte[] bytes = new byte[info.Length];
        using LiteFileStream<string> stream = info.OpenRead();
        stream.ReadExactly(bytes);
        return bytes;
    }
}
