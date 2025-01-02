using SmartXChain.BlockchainCore;
using SmartXChain.Utils;

namespace SmartXChain.Contracts;

/// <summary>
///     Represents a SmartContract within the SmartXChain ecosystem.
///     Provides mechanisms for contract initialization, execution, and integration with the blockchain.
/// </summary>
public class SmartContract
{
    private string _name;

    /// <summary>
    ///     Initializes a new instance of the SmartContract class.
    /// </summary>
    /// <param name="owner">The address of the contract owner.</param>
    /// <param name="serializedContractCode">The contract code in a serialized Base64 format.</param>
    /// <param name="name">Optional name for the contract. If not provided, a new GUID is generated.</param>
    public SmartContract(string owner, string serializedContractCode, string name = "")
    {
        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(Name))
            Name = Functions.NewGuid();
        if (name != "")
            Name = name;

        SerializedContractCode = serializedContractCode;
        Owner = owner;
    }

    /// <summary>
    ///     Gets the contract code in serialized Base64 format.
    /// </summary>
    public string SerializedContractCode { get; }

    /// <summary>
    ///     Gets or sets the name of the contract.
    /// </summary>
    public string Name
    {
        get => _name;
        set => _name = Functions.AllowOnlyAlphanumeric(value);
    }

    /// <summary>
    ///     Gets the owner of the contract.
    /// </summary>
    public string Owner { get; }

    /// <summary>
    ///     Gets the gas consumed by the last execution of the contract.
    /// </summary>
    public int Gas { get; private set; }

    /// <summary>
    ///     Executes the contract using the CodeRunner.
    /// </summary>
    /// <param name="inputs">An array of input parameters for the contract execution.</param>
    /// <param name="currentState">The current state of the contract as a serialized string.</param>
    /// <returns>A tuple containing the execution result and the updated serialized state.</returns>
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
            result = ($"ERROR: Execution of {Name} failed: {ex.Message}", currentState);
        }

        return result;
    }

    /// <summary>
    ///     Creates a new SmartContract and attempts to add it to the blockchain.
    /// </summary>
    /// <param name="contractName">The name of the contract.</param>
    /// <param name="blockchain">The blockchain instance where the contract will be added.</param>
    /// <param name="ownerAddress">The address of the contract owner.</param>
    /// <param name="contractCode">The contract code as a plain string.</param>
    /// <returns>A tuple containing the SmartContract instance and a boolean indicating whether the addition was successful.</returns>
    public static async Task<(SmartContract, bool)> Create(string contractName, Blockchain? blockchain,
        string ownerAddress,
        string contractCode)
    {
        var contract =
            new SmartContract(ownerAddress, Serializer.SerializeToBase64(contractCode), contractName);
        var added = blockchain != null && await blockchain.AddSmartContract(blockchain, contract);
        return (contract, added);
    }

    public override string ToString()
    {
        return $"Name: {Name} Owner {Owner}";
    }
}