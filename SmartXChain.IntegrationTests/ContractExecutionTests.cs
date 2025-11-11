using SmartXChain.Contracts;
using SmartXChain.Utils;
using Xunit;

namespace SmartXChain.IntegrationTests;

public class ContractExecutionTests
{
    [Fact]
    public async Task Executes_contract_and_returns_updated_state()
    {
        const string contractSource = @"using System;\nusing SmartXChain.Contracts.Execution;\npublic class SampleContract\n{\n    public ContractExecutionResult Execute(string[] inputs, string state)\n    {\n        return new ContractExecutionResult(\"ok\", state + \"-executed\");\n    }\n}";

        var contract = BuildContract(contractSource);
        var (result, state) = await contract.Execute(Array.Empty<string>(), "initial");

        Assert.Equal("ok", result);
        Assert.Equal("initial-executed", state);
    }

    [Fact]
    public async Task Blocks_forbidden_file_access()
    {
        const string contractSource = @"using System.IO;\npublic class DangerousContract\n{\n    public string Execute(string[] inputs, string state)\n    {\n        return File.ReadAllText(\"/etc/passwd\");\n    }\n}";

        var contract = BuildContract(contractSource);
        var (result, state) = await contract.Execute(Array.Empty<string>(), "initial");

        Assert.Contains("Forbidden", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("initial", state);
    }

    [Fact]
    public async Task Terminates_on_timeout()
    {
        const string contractSource = @"using System;\npublic class TimeoutContract\n{\n    public string Execute(string[] inputs, string state)\n    {\n        var end = DateTime.UtcNow.AddMinutes(5);\n        while (DateTime.UtcNow < end) { }\n        return \"never\";\n    }\n}";

        var contract = BuildContract(contractSource);
        var (result, state) = await contract.Execute(Array.Empty<string>(), "initial");

        Assert.Equal("Execution timeout", result);
        Assert.Equal("initial", state);
    }

    [Fact]
    public async Task Terminates_on_memory_limit_exceeded()
    {
        const string contractSource = @"using System;\nusing System.Collections.Generic;\npublic class MemoryContract\n{\n    public string Execute(string[] inputs, string state)\n    {\n        var data = new List<byte[]>();\n        for (var i = 0; i < int.MaxValue; i++)\n        {\n            data.Add(new byte[8 * 1024 * 1024]);\n        }\n        return \"ok\";\n    }\n}";

        var contract = BuildContract(contractSource);
        var (result, state) = await contract.Execute(Array.Empty<string>(), "initial");

        Assert.StartsWith("Execution failed", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("initial", state);
    }

    private static SmartContract BuildContract(string source)
    {
        var serialized = Serializer.SerializeToBase64(source);
        return new SmartContract("owner", serialized, "TestContract");
    }
}
