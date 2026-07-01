using Newtonsoft.Json.Linq;

namespace PromptEnhance.Tests;

/// <summary>Covers <see cref="PromptEnhance.WebAPI.BackendClient.ParseMedia"/> - the media-array parser. F3's invariant
/// is "never silently downgrade": a media entry the client attached but that carries no image data must raise a
/// categorized failure, not be dropped so the enhance request quietly goes out text-only.</summary>
public class ParseMediaTests
{
    [Xunit.Fact]
    public void ParseMedia_ThrowsWhenAnAttachedEntryHasNoData()
    {
        // Arrange - an entry the user meant to attach, but with no data (the old silent-drop trigger).
        JArray media = new() { new JObject { ["type"] = "base64", ["mediaType"] = "image/png" } };

        // Act / Assert - must surface, never silently skip.
        Xunit.Assert.Throws<ArgumentException>(() => WebAPI.BackendClient.ParseMedia(media));
    }

    [Xunit.Fact]
    public void ParseMedia_KeepsEntryWhenDataPresent()
    {
        // Arrange
        JArray media = new() { new JObject { ["type"] = "base64", ["data"] = "QUJD", ["mediaType"] = "image/png" } };

        // Act
        List<BackendSchema.MediaContent> result = WebAPI.BackendClient.ParseMedia(media);

        // Assert
        Xunit.Assert.Single(result);
        Xunit.Assert.Equal("QUJD", result[0].Data);
    }

    [Xunit.Fact]
    public void ParseMedia_ReturnsEmptyWhenNoMediaArray()
    {
        // Act - no media requested at all is legitimately text-only, not a drop.
        List<BackendSchema.MediaContent> result = WebAPI.BackendClient.ParseMedia(null!);

        // Assert
        Xunit.Assert.Empty(result);
    }
}
