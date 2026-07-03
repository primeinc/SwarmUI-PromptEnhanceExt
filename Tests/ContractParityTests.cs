using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;

namespace PromptEnhance.Tests;

/// <summary>
/// Pins every mirror of contracts/pe-contract.json — the single source of
/// truth for values shared across the C# backend, the TypeScript frontend,
/// and the tests — to the contract file itself. The frontend mirrors are
/// pinned by the jsdom contract tests against the same file, so a change to
/// either side without updating the contract (or vice versa) fails a gate.
/// </summary>
public class ContractParityTests
{
    internal static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            // Anchor on .git: build output can carry copies of repo files (the Web
            // SDK content glob once swept contracts/ into Tests/bin), but never .git.
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
    public void ErrorCategoryCodes_MatchContractExactly()
    {
        string[] contractCodes = [.. Contract()["errorCategories"]!.Select(t => t.Value<string>()!).OrderBy(x => x)];
        string[] actualCodes = [.. Enum.GetValues<WebAPI.PromptEnhanceErrorCategory>()
            .Select(WebAPI.ErrorHandler.CategoryCode).OrderBy(x => x)];
        Xunit.Assert.Equal(contractCodes, actualCodes);
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

    /// <summary>
    /// The emitted frontend must carry the contract's route names and the
    /// verbatim systemPrompt default — this bridges the contract to the exact
    /// JavaScript SwarmUI serves (the parity gate bridges it back to Frontend/*.ts).
    /// </summary>
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

/// <summary>Contract routes must be exactly what SwarmUI's real registry ends up holding (keys are lowercased by the host).</summary>
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

/// <summary>The SwarmUI ref CI builds and the ref the justfile vendors must be the same commit — enforced, not comment-enforced.</summary>
public class PinParityTests
{
    [Xunit.Fact]
    public void SwarmUIPin_GatesYmlAndJustfileAgree()
    {
        string root = ContractParityTests.RepoRoot();
        string gates = File.ReadAllText(Path.Combine(root, ".github", "workflows", "gates.yml"));
        string justfile = File.ReadAllText(Path.Combine(root, "justfile"));
        Match gatesPin = Regex.Match(gates, @"ref:\s*([0-9a-f]{40})");
        Match justPin = Regex.Match(justfile, "swarmui_pin\\s*:=\\s*\"([0-9a-f]{40})\"");
        Xunit.Assert.True(gatesPin.Success, "gates.yml must pin the SwarmUI host to a full 40-char SHA.");
        Xunit.Assert.True(justPin.Success, "justfile must define swarmui_pin as a full 40-char SHA.");
        Xunit.Assert.Equal(gatesPin.Groups[1].Value, justPin.Groups[1].Value);
    }
}
