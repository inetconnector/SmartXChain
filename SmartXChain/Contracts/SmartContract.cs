using SmartXChain.BlockchainCore;
using SmartXChain.Utils;

namespace SmartXChain.Contracts;

public class SmartContract
{
    public SmartContract(string owner, string serializedContractCode, string name = "")
    {
        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(Name))
            Name = ShortGuid.NewGuid();
        if (name != "")
            Name = name;

        //ContractCode = contractCode;
        SerializedContractCode = serializedContractCode;
        Owner = owner;
    }

    public string SerializedContractCode { get; }

    //public string ContractCode { get; private set; }
    public string Name { get; set; }
    public string Owner { get; private set; }
    public int Gas { get; private set; }


    // Execute the contract using CodeRunner
    // Execute the contract using CodeRunner
    public async Task<(string result, string serializedState)> Execute(string[] inputs, string currentState)
    {
        var calculator = new GasAndRewardCalculator
        {
            SerializedContractCode = SerializedContractCode
        };
        calculator.CalculateGasForContract();
        Gas = calculator.Gas;

        Logger.LogMessage($"Executing contract {Name} (Gas:{Gas})");

        // Deserialize state before execution
        var contractCode = Serializer.DeserializeFromBase64<string>(SerializedContractCode);

        var codeRunner = new CodeRunner();
        (string, string) result;

        try
        {
            using (var cts = new CancellationTokenSource())
            {
                result = await codeRunner.RunScriptAsync(contractCode, inputs, currentState, cts.Token);
            }
        }
        catch (Exception ex)
        {
            result = ($"Execution of {Name} failed: {ex.Message}", currentState);
        }

        return result;
    }


    public static async Task<SmartContract> Create(string contractName, Blockchain blockchain, string ownerAddress,
        string contractCode)
    {
        var contract =
            new SmartContract(ownerAddress, Serializer.SerializeToBase64(contractCode), contractName);
        await blockchain.AddSmartContract(contract);
        return contract;
    }
}