using System.Text.RegularExpressions;
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
    public decimal Gas { get; private set; }

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

        Logger.Log($"Executing contract {Name} (Gas:{Gas})");

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
            Logger.LogException(ex, $"Execution of {Name} failed: {ex.Message}");
            if (ex.InnerException == null)
                result = (ex.Message, currentState);
            else
                result = (ex.Message + ex.InnerException.Message, currentState);
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
        var inheritContract = await AddInherits(blockchain, contractCode, ownerAddress);

        var message = ""; 
        var contract =
            new SmartContract(ownerAddress, Serializer.SerializeToBase64(inheritContract), contractName);
        
        var added = blockchain != null && await blockchain.AddSmartContract(blockchain, contract);
        return (contract, added);
    }

    private static async Task<string> AddInherits(Blockchain? blockchain, string contractCode, string ownerAddress)
    {
        contractCode = await CheckContractBase(blockchain, contractCode, ownerAddress);

        var baseClassName = ExtractInherit(contractCode);
        if (blockchain != null && blockchain.SmartContracts.TryGetValue(baseClassName, out var contract))
        {
            var baseClassCodeSerialized = contract!.SerializedContractCode;
            var baseClassCode = Serializer.DeserializeFromBase64<string>(baseClassCodeSerialized);

            var usingRegex = new Regex(@"^using\s+[^;]+;", RegexOptions.Multiline);
            var baseClassCodeWithoutUsings = usingRegex.Replace(baseClassCode, "").Trim();

            if (!contractCode.Contains(baseClassCodeWithoutUsings))
            {
                var newcode = Merge(contractCode, baseClassCode);
                contractCode = newcode;
            }
        }

        return contractCode;
    }

    /// <summary>
    ///     Checks if the class Contract is available in the chain, and deploys it if not.
    /// </summary>
    /// <param name="blockchain">The blockchain instance.</param>
    /// <param name="contractCode">The code of the contract.</param>
    /// <param name="ownerAddress">The address of the contract owner.</param>
    /// <returns>The updated contract code.</returns>
    private static async Task<string> CheckContractBase(Blockchain? blockchain, string contractCode,
        string ownerAddress)
    {
        if (blockchain == null)
        {
            Logger.LogError("Blockchain instance is null.");
            return contractCode;
        }

        var baseClassName = ExtractInherit(contractCode);
        if (baseClassName != "Contract")
            return contractCode;

        if (blockchain.SmartContracts.TryGetValue(baseClassName, out var existingContract))
        {
            var baseClassCodeSerialized = existingContract.SerializedContractCode;
            var baseClassCode = Serializer.DeserializeFromBase64<string>(baseClassCodeSerialized);

            return Merge(contractCode, baseClassCode);
        }

        var contractBaseFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Contracts", "Contract.cs");
        if (!File.Exists(contractBaseFile))
        {
            Logger.LogError($"Contract.cs not found at {contractBaseFile}.");
            return contractCode;
        }

        var contractBaseContent = File.ReadAllText(contractBaseFile);
        if (!contractBaseContent.Contains("public Contract()"))
        {
            Logger.LogError($"{contractBaseFile} is invalid.");
            return contractCode;
        }

        var newContract =
            new SmartContract(ownerAddress, Serializer.SerializeToBase64(contractBaseContent), baseClassName);
        var addedSuccessfully = await blockchain.AddSmartContract(blockchain, newContract);

        if (!addedSuccessfully)
        {
            Logger.LogError($"Failed to add Contract to blockchain {blockchain.ChainId}.");
            return contractCode;
        }

        var updatedBaseClassCodeSerialized = newContract.SerializedContractCode;
        var updatedBaseClassCode = Serializer.DeserializeFromBase64<string>(updatedBaseClassCodeSerialized);
        return Merge(contractCode, updatedBaseClassCode);
    }


    public static string Merge(string code1, string code2)
    {
        // Regex to match all using statements
        var usingRegex = new Regex(@"^using\s+[^;]+;", RegexOptions.Multiline);

        // Extract using statements from both code snippets
        var usings1 = new HashSet<string>(usingRegex.Matches(code1).Select(m => m.Value));
        var usings2 = new HashSet<string>(usingRegex.Matches(code2).Select(m => m.Value));

        // Merge the usings into a single set
        usings1.UnionWith(usings2);

        // Combine the unique using statements into a string
        var mergedUsings = string.Join("\n", usings1);

        // Remove using statements from the original code snippets
        var code1WithoutUsings = usingRegex.Replace(code1, "").Trim();
        var code2WithoutUsings = usingRegex.Replace(code2, "").Trim();

        // Combine the usings and the code snippets
        return mergedUsings + "\n\n" + code2WithoutUsings + "\n\n" + code1WithoutUsings;
    }

    public static string ExtractInherit(string contractCode)
    {
        // Regex to match the base class after the colon in a class declaration
        var inheritRegex = new Regex(@"class\s+\w+\s*:\s*(\w+)");

        // Match the regex pattern in the provided contract code
        var match = inheritRegex.Match(contractCode);

        // If a match is found, return the group containing the base class name
        if (match.Success) return match.Groups[1].Value;

        // If no inheritance is found, return an appropriate message or empty string
        Logger.LogError("No inheritance found in contract code");
        return "";
    }

    public override string ToString()
    {
        return $"Name: {Name} Owner {Owner}";
    }
}