using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;

namespace PromptEnhance.Tests;

/// <summary>SwarmContext against SwarmUI's real parameter parser and model registries (see <see cref="SwarmHost"/>).</summary>
public class SwarmContextTests
{
    private static BackendSchema.PromptContext Resolve(Session session, string swarmInputJson, bool promptImages, bool activeModel)
    {
        BackendSchema.PromptContext context = new();
        WebAPI.SwarmContext.Resolve(session, JObject.Parse(swarmInputJson), promptImages, activeModel, context);
        return context;
    }

    [Xunit.Fact]
    public void PromptImages_KeepOrderAndGetImageOrdinals()
    {
        BackendSchema.PromptContext context = Resolve(SwarmHost.PermittedSession(),
            $$"""{"promptimages":["data:image/png;base64,{{SwarmHost.PngBase64}}","data:image/png;base64,{{SwarmHost.PngBase64B}}"]}""", true, false);

        Xunit.Assert.Equal(["Image 1", "Image 2"], context.PromptImages.Select(image => image.Label));
        Xunit.Assert.Equal([SwarmHost.PngBase64, SwarmHost.PngBase64B], context.PromptImages.Select(image => image.Data));
        Xunit.Assert.All(context.PromptImages, image => Xunit.Assert.Equal("image/png", image.MediaType));
        Xunit.Assert.Null(context.ActiveModel);
    }

    [Xunit.Fact]
    public void ActiveModel_ResolvesRegistryMetadataAndSwarmsListRules()
    {
        BackendSchema.PromptContext context = Resolve(SwarmHost.PermittedSession(),
            """{"model":"base-model","loras":["lora-a","lora-b"],"loraweights":"0.8","loratencweights":"0.5","lorasectionconfinement":"5,0"}""", false, true);

        BackendSchema.ActiveModelContext active = context.ActiveModel!;
        Xunit.Assert.Equal("base-model.safetensors", active.BaseModel.Name);
        Xunit.Assert.Equal("Base Title", active.BaseModel.Title);
        Xunit.Assert.Equal("base-trigger", active.BaseModel.TriggerPhrase);
        Xunit.Assert.Equal("sdxl-base", active.BaseModel.Architecture);
        Xunit.Assert.Equal("sdxl", active.BaseModel.CompatClass);
        Xunit.Assert.Equal(["tag-a", "tag-b"], active.BaseModel.Tags);
        Xunit.Assert.Equal(2, active.Loras.Count);
        Xunit.Assert.Equal(("lora-a.safetensors", "trigger-a", 0.8, 0.5, 5, "Base"), (active.Loras[0].Name, active.Loras[0].TriggerPhrase, active.Loras[0].Weight, active.Loras[0].TextEncoderWeight, active.Loras[0].ScopeId, active.Loras[0].Scope));
        // T2IParamInput.cs:64-86: a missing weight becomes "1"; a missing text-encoder weight becomes that LoRA's model weight.
        Xunit.Assert.Equal((1.0, 1.0, 0, "Global"), (active.Loras[1].Weight, active.Loras[1].TextEncoderWeight, active.Loras[1].ScopeId, active.Loras[1].Scope));
        Xunit.Assert.Empty(context.PromptImages);
    }

    [Xunit.Fact]
    public void ActiveModel_WithNothingSelected_IsEmpty()
    {
        BackendSchema.PromptContext context = Resolve(SwarmHost.PermittedSession(), "{}", false, true);

        Xunit.Assert.Null(context.ActiveModel!.BaseModel);
        Xunit.Assert.Empty(context.ActiveModel.Loras);
    }

    [Xunit.Theory]
    [Xunit.InlineData("""{"promptimages":[]}""", false, true, "promptimages")]
    [Xunit.InlineData("""{"model":"base-model"}""", true, false, "model")]
    [Xunit.InlineData("""{"steps":20}""", true, true, "steps")]
    public void KeysOutsideTheEnabledChannels_AreRejected(string json, bool promptImages, bool activeModel, string key)
    {
        ArgumentException ex = Xunit.Assert.Throws<ArgumentException>(() => Resolve(SwarmHost.PermittedSession(), json, promptImages, activeModel));

        Xunit.Assert.Contains($"swarmInput.{key}", ex.Message);
    }

    [Xunit.Theory]
    [Xunit.InlineData("""{"loras":["no-such-lora"]}""")]
    [Xunit.InlineData("""{"model":"no-such-model"}""")]
    [Xunit.InlineData("""{"loras":["lora-a"],"loraweights":"heavy"}""")]
    [Xunit.InlineData("""{"loras":["lora-a"],"loraweights":"1","loratencweights":"NaN"}""")]
    [Xunit.InlineData("""{"loras":["lora-a"],"loraweights":"1","lorasectionconfinement":"base"}""")]
    [Xunit.InlineData("""{"promptimages":["not base64 at all"]}""")]
    [Xunit.InlineData("""{"promptimages":["AAAAAAAAAAA"]}""")]
    [Xunit.InlineData("""{"promptimages":["data:image/png;base64,AAAAAAAAAAA"]}""")]
    [Xunit.InlineData("""{"promptimages":["inputs/promptenhance-missing.png"]}""")]
    [Xunit.InlineData("""{"promptimages":["data:text/plain;base64,aGVsbG8="]}""")]
    [Xunit.InlineData("""{"promptimages":[""]}""")]
    [Xunit.InlineData("""{"promptimages":[null]}""")]
    [Xunit.InlineData("""{"promptimages":[1]}""")]
    [Xunit.InlineData("""{"promptimages":"data:image/png;base64,AAAA"}""")]
    [Xunit.InlineData("""{"loras":["lora-a"],"loraweights":"1,garbage"}""")]
    [Xunit.InlineData("""{"loras":["lora-a"],"loraweights":"1","loratencweights":"1,1"}""")]
    [Xunit.InlineData("""{"loras":["lora-a"],"lorasectionconfinement":"0,0"}""")]
    [Xunit.InlineData("""{"loraweights":"1"}""")]
    [Xunit.InlineData("""{"loratencweights":"1"}""")]
    [Xunit.InlineData("""{"lorasectionconfinement":"0"}""")]
    public void ValuesSwarmOrThisParserRejectsOrWouldDrop_AreArgumentExceptions(string json)
    {
        Xunit.Assert.Throws<ArgumentException>(() => Resolve(SwarmHost.PermittedSession(), json, true, true));
    }

    [Xunit.Fact]
    public void AnEmptyPromptImageBesideARealOne_IsRejectedNotDropped()
    {
        string json = $$"""{"promptimages":["data:image/png;base64,{{SwarmHost.PngBase64}}",""]}""";

        ArgumentException ex = Xunit.Assert.Throws<ArgumentException>(() => Resolve(SwarmHost.PermittedSession(), json, true, false));

        Xunit.Assert.Contains("swarmInput.promptimages", ex.Message);
    }

    [Xunit.Fact]
    public void ModelsTheUsersRoleForbids_AreRejected()
    {
        Xunit.Assert.Throws<ArgumentException>(() => Resolve(SwarmHost.PermittedSession("restricted/"), """{"model":"restricted/hidden"}""", false, true));
    }

    [Xunit.Fact]
    public async Task PromptEnhanceRun_SwarmInputRequiredWhileAChannelIsOn_IsInvalidRequest()
    {
        Session session = SwarmHost.PermittedSession();
        SwarmHost.SaveSettings(session, """{"baseUrl":"http://127.0.0.1:9","model":"m","sendActiveModelContext":true}""");

        JObject result = await WebAPI.BackendClient.PromptEnhanceRun(session, "a cat", new JObject { ["prompt"] = "a cat" });

        Xunit.Assert.Equal("invalid_request", result["error_id"]!.Value<string>());
        Xunit.Assert.Contains("swarmInput", result["error"]!.Value<string>());
    }
}
