using Newtonsoft.Json.Linq;

namespace PromptEnhance.Tests;

public class ParseMediaTests
{
    [Xunit.Fact]
    public void ParseMedia_ThrowsWhenAnAttachedEntryHasNoData()
    {
        JArray media = new() { new JObject { ["type"] = "base64", ["mediaType"] = "image/png" } };

        Xunit.Assert.Throws<ArgumentException>(() => WebAPI.BackendClient.ParseMedia(media));
    }

    [Xunit.Fact]
    public void ParseMedia_KeepsEntryWhenDataPresent()
    {
        JArray media = new() { new JObject { ["type"] = "base64", ["data"] = "QUJD", ["mediaType"] = "image/png" } };

        List<BackendSchema.MediaContent> result = WebAPI.BackendClient.ParseMedia(media);

        Xunit.Assert.Single(result);
        Xunit.Assert.Equal("QUJD", result[0].Data);
    }

    [Xunit.Fact]
    public void ParseMedia_ReturnsEmptyWhenNoMediaArray()
    {
        List<BackendSchema.MediaContent> result = WebAPI.BackendClient.ParseMedia(null!);

        Xunit.Assert.Empty(result);
    }
}
