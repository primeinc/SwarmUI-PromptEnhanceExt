using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;

namespace PromptEnhance.Tests;

/// <summary>Pins every C# mirror of contracts/pe-contract.json to the contract file itself.</summary>
public class ContractParityTests
{
    internal static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Path.Exists(Path.Combine(dir.FullName, ".git"))
                && File.Exists(Path.Combine(dir.FullName, "contracts", "pe-contract.json")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate the repo root (a directory with .git and contracts/pe-contract.json) from the test output directory.");
    }

    internal static JObject Contract() =>
        JObject.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "contracts", "pe-contract.json")));

    [Xunit.Fact]
    public void Defaults_MatchContractExactly()
    {
        JObject specs = (JObject)Contract()["settings"]!;
        JObject defaults = WebAPI.SessionSettings.Defaults;
        string[] contractKeys = [.. specs.Properties().Select(p => p.Name).OrderBy(x => x)];
        string[] defaultKeys = [.. defaults.Properties().Select(p => p.Name).OrderBy(x => x)];
        Xunit.Assert.Equal(contractKeys, defaultKeys);
        foreach (JProperty prop in specs.Properties())
        {
            Xunit.Assert.True(JToken.DeepEquals(prop.Value["default"], defaults[prop.Name]),
                $"Default for '{prop.Name}' must match the contract: expected {prop.Value["default"]}, got {defaults[prop.Name]}");
        }
    }

    [Xunit.Fact]
    public void ValidateSettings_EnforcesContractBounds()
    {
        JObject specs = (JObject)Contract()["settings"]!;

        long timeoutMin = specs["timeoutSeconds"]!["min"]!.Value<long>();
        long timeoutMax = specs["timeoutSeconds"]!["max"]!.Value<long>();
        Xunit.Assert.Equal(WebAPI.SessionSettings.MaxTimeoutSeconds, timeoutMax);
        Xunit.Assert.Null(WebAPI.SessionSettings.ValidateSettings(new JObject { ["timeoutSeconds"] = timeoutMin }));
        Xunit.Assert.Null(WebAPI.SessionSettings.ValidateSettings(new JObject { ["timeoutSeconds"] = timeoutMax }));
        Xunit.Assert.NotNull(WebAPI.SessionSettings.ValidateSettings(new JObject { ["timeoutSeconds"] = timeoutMin - 1 }));
        Xunit.Assert.NotNull(WebAPI.SessionSettings.ValidateSettings(new JObject { ["timeoutSeconds"] = timeoutMax + 1 }));

        double temperatureMin = specs["temperature"]!["min"]!.Value<double>();
        double temperatureMax = specs["temperature"]!["max"]!.Value<double>();
        Xunit.Assert.Null(WebAPI.SessionSettings.ValidateSettings(new JObject { ["temperature"] = temperatureMin }));
        Xunit.Assert.Null(WebAPI.SessionSettings.ValidateSettings(new JObject { ["temperature"] = temperatureMax }));
        Xunit.Assert.NotNull(WebAPI.SessionSettings.ValidateSettings(new JObject { ["temperature"] = temperatureMin - 0.1 }));
        Xunit.Assert.NotNull(WebAPI.SessionSettings.ValidateSettings(new JObject { ["temperature"] = temperatureMax + 0.1 }));

        long maxTokensMin = specs["maxTokens"]!["min"]!.Value<long>();
        long maxTokensMax = specs["maxTokens"]!["max"]!.Value<long>();
        Xunit.Assert.Null(WebAPI.SessionSettings.ValidateSettings(new JObject { ["maxTokens"] = maxTokensMin }));
        Xunit.Assert.Null(WebAPI.SessionSettings.ValidateSettings(new JObject { ["maxTokens"] = maxTokensMax }));
        Xunit.Assert.NotNull(WebAPI.SessionSettings.ValidateSettings(new JObject { ["maxTokens"] = maxTokensMin - 1 }));
        Xunit.Assert.NotNull(WebAPI.SessionSettings.ValidateSettings(new JObject { ["maxTokens"] = maxTokensMax + 1 }));

        foreach (JToken mode in (JArray)specs["replaceMode"]!["enum"]!)
        {
            Xunit.Assert.Null(WebAPI.SessionSettings.ValidateSettings(new JObject { ["replaceMode"] = mode.Value<string>() }));
        }
        Xunit.Assert.NotNull(WebAPI.SessionSettings.ValidateSettings(new JObject { ["replaceMode"] = "not_a_contract_mode" }));
    }

    [Xunit.Fact]
    public void ErrorCategoryCodes_MatchContractEntryWise()
    {
        JObject map = (JObject)Contract()["errorCategories"]!;
        WebAPI.PromptEnhanceErrorCategory[] enumValues = Enum.GetValues<WebAPI.PromptEnhanceErrorCategory>();
        Xunit.Assert.Equal(enumValues.Length, map.Count);
        foreach (WebAPI.PromptEnhanceErrorCategory category in enumValues)
        {
            Xunit.Assert.Equal(map[category.ToString()]!.Value<string>(), WebAPI.ErrorHandler.CategoryCode(category));
        }
    }

    [Xunit.Fact]
    public void ValidateSettings_EnforcesContractTypes()
    {
        JObject specs = (JObject)Contract()["settings"]!;
        foreach (JProperty prop in specs.Properties())
        {
            Xunit.Assert.Null(WebAPI.SessionSettings.ValidateSettings(new JObject { [prop.Name] = prop.Value["default"] }));
            JToken rejectProbe = prop.Value["type"]!.Value<string>() switch
            {
                "string" => new JValue(42),
                "integer" => new JValue("not a number"),
                "number" => new JValue("not a number"),
                "boolean" => new JValue("yes"),
                _ => throw new InvalidOperationException($"Unknown contract type for '{prop.Name}'")
            };
            Xunit.Assert.NotNull(WebAPI.SessionSettings.ValidateSettings(new JObject { [prop.Name] = rejectProbe }));
        }
    }

    [Xunit.Fact]
    public async Task StoreKey_MatchesContract_ThroughRealStore()
    {
        JObject store = (JObject)Contract()["store"]!;
        Session session = TestSessions.MakeRealSession();
        JObject rawInput = new() { ["settings"] = new JObject { ["model"] = "contract-probe" } };
        JObject result = await WebAPI.SessionSettings.SavePromptEnhanceSettings(rawInput, session);
        Xunit.Assert.True(result["success"]!.Value<bool>());
        string? stored = session.User.GetGenericData(store["dataname"]!.Value<string>(), store["name"]!.Value<string>());
        Xunit.Assert.NotNull(stored);
        Xunit.Assert.Equal("contract-probe", JObject.Parse(stored!)["model"]!.Value<string>());
    }

    [Xunit.Fact]
    public void EmittedContractsJs_CarriesRoutesAndSystemPrompt()
    {
        string js = File.ReadAllText(Path.Combine(RepoRoot(), "Assets", "contracts.js"));
        foreach (JProperty route in ((JObject)Contract()["routes"]!).Properties())
        {
            Xunit.Assert.Contains($"'{route.Value.Value<string>()}'", js);
        }
        Xunit.Assert.Contains(Contract()["settings"]!["systemPrompt"]!["default"]!.Value<string>()!, js);
    }
}

/// <summary>Contract routes vs SwarmUI's real registry (keys are lowercased by the host).</summary>
[Xunit.Collection(ApiRegistryCollectionDefinition.Name)]
public class ContractRouteParityTests
{
    [Xunit.Fact]
    public void RegisteredRoutes_CoverContractRoutesExactly()
    {
        string[] contractRoutes = [.. ((JObject)ContractParityTests.Contract()["routes"]!).Properties()
            .Select(p => p.Value.Value<string>()!.ToLowerInvariant()).OrderBy(x => x)];
        foreach (string route in contractRoutes)
        {
            Xunit.Assert.True(SwarmUI.WebAPI.API.APIHandlers.ContainsKey(route), $"Contract route '{route}' must be registered.");
        }
        string[] registeredOurs = [.. SwarmUI.WebAPI.API.APIHandlers.Keys
            .Where(k => k.Contains("promptenhance")).OrderBy(x => x)];
        Xunit.Assert.Equal(contractRoutes, registeredOurs);
    }
}

/// <summary>The SwarmUI ref CI builds and the ref the justfile vendors must be the same commit.</summary>
public class PinParityTests
{
    [Xunit.Fact]
    public void SwarmUIPin_EveryGatesYmlRefEqualsTheJustfilePin()
    {
        string root = ContractParityTests.RepoRoot();
        string gates = File.ReadAllText(Path.Combine(root, ".github", "workflows", "gates.yml"));
        string justfile = File.ReadAllText(Path.Combine(root, "justfile"));
        Match justPin = Regex.Match(justfile, "swarmui_pin\\s*:=\\s*\"([0-9a-f]{40})\"");
        Xunit.Assert.True(justPin.Success, "justfile must define swarmui_pin as a full 40-char SHA.");
        // Anchored to the SwarmUI checkout blocks: a future SHA-pinned checkout of any
        // other repository is neither forced to this pin nor rewritten by vendor-bump.
        MatchCollection gatesPins = Regex.Matches(gates, @"repository: mcmonkeyprojects/SwarmUI\s*\r?\n\s*ref:\s*([0-9a-f]{40})");
        Xunit.Assert.True(gatesPins.Count >= 1, "gates.yml must pin the SwarmUI host checkout to a full 40-char SHA.");
        foreach (Match pin in gatesPins)
        {
            Xunit.Assert.Equal(justPin.Groups[1].Value, pin.Groups[1].Value);
        }
    }

    [Xunit.Fact]
    public void VendoredPropertyGroup_MirrorsSwarmUIExtensionProps()
    {
        string root = ContractParityTests.RepoRoot();
        string propsPath = Path.Combine(root, "vendor", "SwarmUI", "src", "SwarmUI.extension.props");
        if (!File.Exists(propsPath))
        {
            propsPath = Path.GetFullPath(Path.Combine(root, "..", "..", "SwarmUI.extension.props"));
        }
        Xunit.Assert.True(File.Exists(propsPath),
            $"No SwarmUI.extension.props found in the vendored or host layout ({propsPath}) — the vendored PropertyGroup mirror cannot be verified.");
        string props = File.ReadAllText(propsPath);
        string csproj = File.ReadAllText(Path.Combine(root, "PromptEnhance.csproj"));
        MatchCollection expected = Regex.Matches(
            Regex.Match(props, @"<PropertyGroup>[\s\S]*?</PropertyGroup>").Value,
            @"<(\w+)>([^<]*)</\1>");
        Xunit.Assert.True(expected.Count >= 5, "SwarmUI.extension.props no longer defines the property block this mirror check expects.");
        foreach (Match property in expected)
        {
            Match actual = Regex.Match(csproj, $"<{property.Groups[1].Value}>([^<]*)</{property.Groups[1].Value}>");
            Xunit.Assert.True(actual.Success,
                $"PromptEnhance.csproj's vendored PropertyGroup is missing <{property.Groups[1].Value}>, which SwarmUI.extension.props defines at the current pin.");
            Xunit.Assert.Equal(property.Groups[2].Value, actual.Groups[1].Value);
        }
        foreach (string removal in new[] { "<None Remove=\"**\" />", "<Content Remove=\"**\" />" })
        {
            Xunit.Assert.True(props.Contains(removal), $"SwarmUI.extension.props no longer strips build items with {removal}; re-check the mirror in PromptEnhance.csproj.");
            Xunit.Assert.True(csproj.Contains(removal), $"PromptEnhance.csproj must strip build items with {removal} as SwarmUI.extension.props does, or the Web SDK copies the repo's JSON (Tests/bin included) into bin.");
        }
    }

    [Xunit.Fact]
    public void SwarmUIProjectReferenceProperties_IdenticalAcrossCsprojFiles()
    {
        string root = ContractParityTests.RepoRoot();
        string extensionCsproj = File.ReadAllText(Path.Combine(root, "PromptEnhance.csproj"));
        string testsCsproj = File.ReadAllText(Path.Combine(root, "Tests", "PromptEnhance.Tests.csproj"));
        string[] propertySets = [.. Regex.Matches(extensionCsproj + testsCsproj, @"<Properties>([^<]*)</Properties>")
            .Select(m => m.Groups[1].Value)];
        Xunit.Assert.True(propertySets.Length >= 2, "Expected Properties metadata on the SwarmUI ProjectReferences in both csproj files.");
        Xunit.Assert.True(propertySets.All(p => p == propertySets[0]),
            $"ProjectReference Properties differ across csproj files — this recreates the two-instance SwarmUI build race (CS2012): [{string.Join(" | ", propertySets.Distinct())}]");
    }
}
