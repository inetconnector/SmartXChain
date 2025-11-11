using SmartXChain.Contracts;
using SmartXChain.Utils;
using Xunit;

namespace SmartXChain.IntegrationTests;

public class ContractExecutionTests
{
    [Fact]
    public async Task Executes_contract_and_returns_updated_state()
    {
        const string contractSource = @"
        using System;
        using SmartXChain.Contracts.Execution;
        public class SampleContract
        {
            public ContractExecutionResult Execute(string[] inputs, string state)
            {
                return new ContractExecutionResult(""ok"", state + ""-executed"");
            }
        }";

        var contract = BuildContract(contractSource);
        var (result, state) = await contract.Execute(Array.Empty<string>(), "initial");

        Assert.Equal("ok", result);
        Assert.Equal("initial-executed", state);
    }

    [Fact]
    public async Task Blocks_forbidden_file_access()
    {
        const string contractSource = @"
        using System.IO;
        public class DangerousContract
        {
            public string Execute(string[] inputs, string state)
            {
                return File.ReadAllText(""/etc/passwd"");
            }
        }";

        var contract = BuildContract(contractSource);
        var (result, state) = await contract.Execute(Array.Empty<string>(), "initial");

        Assert.Contains("Forbidden", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("initial", state);
    }

    [Fact]
    public async Task Terminates_on_timeout()
    {
        const string contractSource = @"
        using System;
        public class TimeoutContract
        {
            public string Execute(string[] inputs, string state)
            {
                var end = DateTime.UtcNow.AddMinutes(5);
                while (DateTime.UtcNow < end) { }
                return ""never"";
            }
        }";

        var contract = BuildContract(contractSource);
        var (result, state) = await contract.Execute(Array.Empty<string>(), "initial");

        Assert.Equal("Execution timeout", result);
        Assert.Equal("initial", state);
    }

    [Fact]
    public async Task Terminates_on_memory_limit_exceeded()
    {
        const string contractSource =
            "using System;\n" +
            "using SmartXChain.Contracts.Execution;\n" +
            "public class SampleContract\n" +
            "{\n" +
            "    public ContractExecutionResult Execute(string[] inputs, string state)\n" +
            "    {\n" +
            "        return new ContractExecutionResult(\"ok\", state + \"-executed\");\n" +
            "    }\n" +
            "}";

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
