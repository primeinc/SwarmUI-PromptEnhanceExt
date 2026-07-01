using System.Net;

namespace PromptEnhance.Tests;

public class ErrorHandlerTests
{
    [Xunit.Theory]
    [Xunit.InlineData(HttpStatusCode.Unauthorized, WebAPI.PromptEnhanceErrorCategory.Authentication)]
    [Xunit.InlineData(HttpStatusCode.Forbidden, WebAPI.PromptEnhanceErrorCategory.Authentication)]
    [Xunit.InlineData(HttpStatusCode.NotFound, WebAPI.PromptEnhanceErrorCategory.ModelMissing)]
    [Xunit.InlineData(HttpStatusCode.RequestTimeout, WebAPI.PromptEnhanceErrorCategory.Timeout)]
    [Xunit.InlineData(HttpStatusCode.GatewayTimeout, WebAPI.PromptEnhanceErrorCategory.Timeout)]
    [Xunit.InlineData(HttpStatusCode.RequestEntityTooLarge, WebAPI.PromptEnhanceErrorCategory.UnsupportedImage)]
    [Xunit.InlineData(HttpStatusCode.UnprocessableEntity, WebAPI.PromptEnhanceErrorCategory.UnsupportedImage)]
    [Xunit.InlineData(HttpStatusCode.InternalServerError, WebAPI.PromptEnhanceErrorCategory.ServerUnavailable)]
    [Xunit.InlineData(HttpStatusCode.ServiceUnavailable, WebAPI.PromptEnhanceErrorCategory.ServerUnavailable)]
    [Xunit.InlineData(HttpStatusCode.BadRequest, WebAPI.PromptEnhanceErrorCategory.HttpError)]
    public void CategorizeHttpStatus_MapsStatusToCategory(HttpStatusCode status, WebAPI.PromptEnhanceErrorCategory expected)
    {
        WebAPI.PromptEnhanceErrorCategory actual = WebAPI.ErrorHandler.CategorizeHttpStatus(status);

        Xunit.Assert.Equal(expected, actual);
    }

    [Xunit.Fact]
    public void CategoryCode_IsStableSnakeCase()
    {
        Xunit.Assert.Equal("server_unavailable", WebAPI.ErrorHandler.CategoryCode(WebAPI.PromptEnhanceErrorCategory.ServerUnavailable));
        Xunit.Assert.Equal("invalid_response_shape", WebAPI.ErrorHandler.CategoryCode(WebAPI.PromptEnhanceErrorCategory.InvalidResponseShape));
        Xunit.Assert.Equal("unsupported_image", WebAPI.ErrorHandler.CategoryCode(WebAPI.PromptEnhanceErrorCategory.UnsupportedImage));
        Xunit.Assert.Equal("generic", WebAPI.ErrorHandler.CategoryCode(WebAPI.PromptEnhanceErrorCategory.Generic));
    }

    [Xunit.Fact]
    public void Format_AppendsDetailOnlyWhenPresent()
    {
        string withDetail = WebAPI.ErrorHandler.Format(WebAPI.PromptEnhanceErrorCategory.HttpError, "boom");
        string without = WebAPI.ErrorHandler.Format(WebAPI.PromptEnhanceErrorCategory.HttpError);

        Xunit.Assert.Contains("boom", withDetail);
        Xunit.Assert.Contains("Detail:", withDetail);
        Xunit.Assert.DoesNotContain("Detail:", without);
    }

    [Xunit.Fact]
    public void Excerpt_TruncatesTextLongerThanMax()
    {
        string big = new('x', 1000);

        string excerpt = WebAPI.ErrorHandler.Excerpt(big, 600);

        Xunit.Assert.True(excerpt.Length < big.Length);
        Xunit.Assert.Contains("truncated", excerpt);
    }

    [Xunit.Fact]
    public void Excerpt_LeavesShortTextUntouched()
    {
        string excerpt = WebAPI.ErrorHandler.Excerpt("short", 600);

        Xunit.Assert.Equal("short", excerpt);
    }

    [Xunit.Fact]
    public void Format_Authentication_DoesNotInstructSettingAnApiKey()
    {
        string message = WebAPI.ErrorHandler.Format(WebAPI.PromptEnhanceErrorCategory.Authentication);

        Xunit.Assert.DoesNotContain("Set the API key", message, System.StringComparison.OrdinalIgnoreCase);
        Xunit.Assert.Contains("does not require authentication", message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Xunit.Theory]
    [Xunit.InlineData("{\"error\":{\"message\":\"This model does not support image input.\",\"type\":\"invalid_request_error\",\"code\":null}}")]
    [Xunit.InlineData("{\"error\":{\"message\":\"image_url is not supported by this model\"}}")]
    [Xunit.InlineData("The model has no vision capability")]
    [Xunit.InlineData("multimodal input rejected")]
    public void LooksLikeImageRejection_TrueWhenBodyBlamesImage(string body)
    {
        Xunit.Assert.True(WebAPI.ErrorHandler.LooksLikeImageRejection(body));
    }

    [Xunit.Theory]
    [Xunit.InlineData(null)]
    [Xunit.InlineData("")]
    [Xunit.InlineData("   ")]
    [Xunit.InlineData("{\"error\":{\"message\":\"This model's maximum context length is 4096 tokens.\",\"type\":\"invalid_request_error\"}}")]
    [Xunit.InlineData("{\"error\":{\"message\":\"Unknown parameter: 'foo'.\"}}")]
    public void LooksLikeImageRejection_FalseForUnrelatedOrEmptyBody(string? body)
    {
        Xunit.Assert.False(WebAPI.ErrorHandler.LooksLikeImageRejection(body!));
    }
}
