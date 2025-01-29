


using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIPSorcery.Net;
using SmartXChain.BlockchainCore;
using SmartXChain.Contracts;
using SmartXChain.Utils;
using SmartXChain.Validators;

using System.Collections.Concurrent; 

namespace SmartXChain.ClientServer;

public enum SdpMessageType
{
    Offer,
    Answer
}

public class WebRtcManager  
{
    /// <summary>
    ///     Stores a TaskCompletionSource for each correlation ID,
    ///     allowing us to await responses from the remote peer.
    /// </summary>
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string?>> _pendingRequests = new();

    private RTCPeerConnection _peerConnection;

    // Events for signaling SDP or ICE back to the caller
    public event Action<string> OnSdpOfferGenerated;
    public event Action<string> OnSdpAnswerGenerated;
    public event Action<string> OnIceCandidateGenerated;
     


    /// <summary>
    ///     Initializes the RTCPeerConnection by reading STUN servers from 'settings.json'.
    /// </summary>
    public async Task InitializeAsync(Blockchain blockchain)
    {
        _blockchain = blockchain;

        // Read the JSON settings file
        var jsonFilePath = "settings.json";
        if (!File.Exists(jsonFilePath)) throw new FileNotFoundException("Could not find settings file.", jsonFilePath);

        var json = await File.ReadAllTextAsync(jsonFilePath);
        var webrtcSettings = JsonSerializer.Deserialize<WebRtcSettings>(json);

        // If the JSON file is missing "IceServers" or it's empty, handle it
        if (webrtcSettings?.IceServers == null || webrtcSettings.IceServers.Count == 0)
            Logger.Log("Warning: No STUN servers found in settings.json. Using empty configuration.");

        // Build the RTCConfiguration with STUN servers
        var config = new RTCConfiguration
        {
            // SIPSorcery expects 'iceServers' (lowercase 'i')
            iceServers = webrtcSettings?.IceServers?
                             .Select(url => new RTCIceServer { urls = url })
                             .ToList()
                         ?? new List<RTCIceServer>()
        };
        // Create the RTCPeerConnection
        _peerConnection = new RTCPeerConnection(config);

        // Create a data channel in SIPSorcery:
        _dataChannel = await _peerConnection.createDataChannel("api_channel");

        // Subscribe to incoming messages
        _dataChannel.onmessage += _dataChannel_onmessage;

        // Subscribe to ICE candidate events
        _peerConnection.onicecandidate += candidate =>
        {
            if (candidate != null && !string.IsNullOrWhiteSpace(candidate.candidate))
                OnIceCandidateGenerated?.Invoke(candidate.candidate);
        };

        Logger.Log("RTCPeerConnection initialized with servers:");
        if (config.iceServers.Count > 0)
            foreach (var srv in config.iceServers)
                Logger.Log("  " + srv.urls);
        else
            Logger.Log("  (none)");

        // If you need more initialization logic (e.g. adding tracks), put it here.
        RegisterApiHandlers();

        await Task.CompletedTask;
    }
     
    /// <summary>
    ///     Creates a local SDP offer and raises OnSdpOfferGenerated.
    /// </summary>
    public void GenerateOffer()
    {
        var offer = _peerConnection.createOffer();
        _peerConnection.setLocalDescription(offer);

        OnSdpOfferGenerated?.Invoke(offer.sdp);
    }

    /// <summary>
    ///     Creates a local SDP answer and raises OnSdpAnswerGenerated.
    /// </summary>
    public void GenerateAnswer()
    {
        var answer = _peerConnection.createAnswer();
        _peerConnection.setLocalDescription(answer);

        OnSdpAnswerGenerated?.Invoke(answer.sdp);
    }

    /// <summary>
    ///     Sets a remote offer or answer SDP.
    /// </summary>
    public async Task SetRemoteDescriptionAsync(string sdp, SdpMessageType type)
    {
        var desc = new RTCSessionDescriptionInit
        {
            sdp = sdp,
            type = type == SdpMessageType.Offer ? RTCSdpType.offer : RTCSdpType.answer
        };

        _peerConnection.setRemoteDescription(desc);

        await Task.CompletedTask;
    }

    /// <summary>
    ///     Adds an ICE candidate received from the remote peer.
    /// </summary>
    public void AddIceCandidate(string candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            var iceCandidate = new RTCIceCandidateInit
            {
                candidate = candidate
            };
            _peerConnection.addIceCandidate(iceCandidate);
        }
    }

    /// <summary>
    ///     Sends a message asynchronously over the DataChannel
    ///     and waits up to 5 seconds for a response.
    /// </summary>
    /// <param name="message">The payload to send.</param>
    /// <returns>The response string or a timeout message.</returns>
    public async Task<string?> SendRequestAsync(string nodeAddress, string message)
    {
        if (_dataChannel == null || _peerConnection == null)
            return "WebRTC not properly initialized (DataChannel or PeerConnection is missing).";


        // Create a TaskCompletionSource and store it in the dictionary
        var tcs = new TaskCompletionSource<string?>();
        _pendingRequests[nodeAddress] = tcs;

        // Serialize the request as JSON
        var request = new WebRtcMessage
        {
            NodeAddress = nodeAddress,
            Payload = message
        };
        var json = JsonSerializer.Serialize(request);

        // Send the request over the DataChannel
        _dataChannel.send(json);

        // Use a 5-second timeout
        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(5000));

        // Remove the TCS from the dictionary (cleanup)
        _pendingRequests.TryRemove(nodeAddress, out _);

        // If the TCS completes first, it means we got a response in time
        if (completedTask == tcs.Task) return tcs.Task.Result; // The actual response

        // Otherwise, it's a timeout
        return "Timeout: No response received.";
    }

    /// <summary>
    ///     A simple model used to exchange messages (requests/responses)
    ///     over the DataChannel in JSON format.
    /// </summary>
    private class WebRtcMessage
    {
        public string? NodeAddress { get; set; }
        public string? Payload { get; set; }
    }

    // Internal class (or you could define it externally) for JSON parsing
    private class WebRtcSettings
    {
        // Must match the property name(s) in settings.json
        public List<string> IceServers { get; set; }
    }  
    private readonly Dictionary<string, TaskCompletionSource<string>> _pendingResponses = new();

    private Dictionary<string, Func<string, Task<string>>> _apiHandlers;
    internal Blockchain _blockchain;
    internal RTCDataChannel _dataChannel;
    public double NodeTimeoutSeconds { get; set; } = 30;

    /// <summary>
    ///     Callback for when a message arrives from the remote peer over the data channel.
    /// </summary>
    private void _dataChannel_onmessage(RTCDataChannel dc, DataChannelPayloadProtocols protocol, byte[] data)
    {
        var message = Encoding.UTF8.GetString(data);
        Logger.Log($"[DataChannel] Received: {message}");

        // Attempt to parse as ApiRequest
        ApiRequest requestOrResponse = null;
        try
        {
            requestOrResponse = JsonSerializer.Deserialize<ApiRequest>(message);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to parse message as ApiRequest: {ex.Message}");
            return;
        }

        if (requestOrResponse == null)
        {
            Logger.LogError("Parsed ApiRequest is null.");
            return;
        }

        // Check if this "ApiName" is in our handlers -> i.e. an incoming request
        if (_apiHandlers.ContainsKey(requestOrResponse.ApiName))
            // Handle as a request
            HandleIncomingRequest(requestOrResponse);
        else
            // Possibly a response to a request we sent
            HandleIncomingResponse(requestOrResponse);
    }

    /// <summary>
    ///     Handles an incoming API request (the remote peer calling one of our APIs).
    /// </summary>
    private async void HandleIncomingRequest(ApiRequest request)
    {
        // We have a dictionary of local API handlers
        if (_apiHandlers.TryGetValue(request.ApiName, out var handler))
            try
            {
                // Execute the async handler
                var result = await handler.Invoke(request.Parameters);

                // Send the response back as an ApiRequest for consistency
                var responseObj = new ApiRequest
                {
                    ApiName = request.ApiName,
                    Parameters = result
                };
                var responseJson = JsonSerializer.Serialize(responseObj);
                SendDataChannelMessage(responseJson);
            }
            catch (Exception ex)
            {
                SendDataChannelMessage($"ERROR: {ex.Message}");
            }
        else
            // Unknown API
            SendDataChannelMessage($"ERROR: Unknown API: {request.ApiName}");
    }

    /// <summary>
    ///     Sends a text message over the data channel.
    /// </summary>
    public void SendDataChannelMessage(string message)
    {
        if (_dataChannel == null || !_dataChannel.IsOpened)
        {
            Logger.LogError("Data channel is not open. Cannot send message.");
            return;
        }

        var buffer = Encoding.UTF8.GetBytes(message);
        _dataChannel.send(buffer);
        Logger.Log($"[DataChannel] Sent: {message}");
    }

    /// <summary>
    ///     Handles an incoming response for a request we previously sent.
    /// </summary>
    private void HandleIncomingResponse(ApiRequest response)
    {
        lock (_pendingResponses)
        {
            // If we have a pending TCS for that APIName, set its result
            if (_pendingResponses.TryGetValue(response.ApiName, out var tcs))
            {
                _pendingResponses.Remove(response.ApiName);
                tcs.SetResult(response.Parameters);
            }
            else
            {
                // No TCS waiting => maybe a stray or unknown response
                Logger.LogError($"No pending request for ApiName {response.ApiName}.");
            }
        }
    }

    /// <summary>
    ///     Registers the local API handlers, just like your snippet's _apiHandlers dictionary.
    ///     Adjust or add as needed from your original code.
    /// </summary>
    public void RegisterApiHandlers()
    {
        _apiHandlers = new Dictionary<string, Func<string, Task<string>>>
        {
            { "GetUserTransactions", HandleGetUserTransactions },
            { "GetContractCode", HandleGetContractCode },
            { "GetContractNames", HandleGetContractNames },
            { "GetChainInfo", _ => HandleGetChainInfo() },
            { "GetBlockData", HandleGetBlockData },
            { "Register", HandleRegister },
            { "Nodes", HandleNodes },
            { "Vote", HandleVote },
            { "NewBlocks", HandleNewBlocks },
            { "ChainInfo", HandleChainInfo },
            { "GetBlocks", HandleGetBlocks },
            { "GetBlock", HandleGetBlock },
            { "VerifyCode", HandleVerifyCode },
            { "ValidateChain", _ => HandleValidateChain() }
        };
    }

    private class ApiRequest
    {
        public string ApiName { get; set; }
        public string Parameters { get; set; }
    }

    #region API-Handler-Implementierungen

    private bool ValidateSignature(string nodeAddress, string signature)
    {
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Config.Default.ChainId)))
        {
            var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(nodeAddress));
            var computedSignature = Convert.ToBase64String(computedHash);

            return computedSignature == signature;
        }
    }

    private async Task<string> HandleGetUserTransactions(string user)
    {
        if (string.IsNullOrEmpty(user))
            return "ERROR: User name cannot be null or empty.";

        var transactions =
            BlockchainStorage.GetUserTransactions(user, Config.Default.BlockchainPath, Config.Default.ChainId);
        return string.IsNullOrEmpty(transactions)
            ? $"ERROR: No transactions for {user} found."
            : transactions;
    }

    private async Task<string> HandleGetContractCode(string contract)
    {
        if (string.IsNullOrEmpty(contract))
            return "ERROR: Contract name cannot be null or empty.";

        var contractCode =
            BlockchainStorage.GetContractCode(contract, Config.Default.BlockchainPath, Config.Default.ChainId);
        return string.IsNullOrEmpty(contractCode)
            ? $"ERROR: Contract code for {contract} not found."
            : contractCode;
    }

    private async Task<string> HandleGetContractNames(string search)
    {
        var contractNames = BlockchainStorage.GetContractNames(
            Config.Default.BlockchainPath,
            Config.Default.ChainId,
            search,
            50);
        return JsonSerializer.Serialize(contractNames);
    }

    private async Task<string> HandleGetChainInfo()
    {
        if (_blockchain?.Chain == null)
            return "ERROR: Blockchain not initialized.";

        var chainInfo = ChainInfo.CreateChainInfo(_blockchain);
        return JsonSerializer.Serialize(chainInfo);
    }

    private async Task<string> HandleGetBlockData(string block)
    {
        if (!int.TryParse(block, out var blockInt))
            return $"ERROR: Invalid block number: {block}";

        if (_blockchain?.Chain == null || blockInt < 0 || blockInt >= _blockchain.Chain.Count)
            return $"ERROR: Block {block} not found.";

        return _blockchain.Chain[blockInt].ToBase64();
    }

    private async Task<string> HandleRegister(string message)
    {
        // Split the message into its "Register" part and payload
        var parts = message.Split(new[] { ':' }, 2);
        if (parts.Length != 2 || parts[0] != "Register")
            return "ERROR: Invalid registration format.";

        var remainingParts = parts[1];
        var addressSignatureParts = remainingParts.Split('|');
        if (addressSignatureParts.Length != 2)
            return "ERROR: Invalid registration format.";

        var nodeAddress = addressSignatureParts[0];
        var signature = addressSignatureParts[1];

        // Validate the node's signature
        if (!ValidateSignature(nodeAddress, signature))
        {
            Logger.Log($"ERROR: ValidateSignature failed. Node not registered: {nodeAddress}");
            return "ERROR: Invalid signature.";
        }

        // Add the node to the list
        Node.AddNodeIP(nodeAddress);
        Logger.Log($"Node registered: {nodeAddress}");
        return "ok";
    }

    private async Task<string> HandleNodes(string message)
    {
        var now = DateTime.UtcNow;

        // Process all nodes from the message and add them to the list
        foreach (var peer in message.Split(','))
            if (!Node.CurrentNodes.Contains(peer) && peer.Contains("."))
                Node.AddNodeIP(peer);

        // Remove inactive nodes
        var inactiveNodes = Node.CurrentNodes_LastActive
            .Where(kvp => (now - kvp.Value).TotalSeconds > NodeTimeoutSeconds)
            .Select(kvp => kvp.Key)
            .ToList();

        if (inactiveNodes.Contains(Config.Default.NodeAddress))
            inactiveNodes.Remove(Config.Default.NodeAddress);

        foreach (var peer in Config.Default.SignalHubs.Where(peer => inactiveNodes.Contains(peer)))
            inactiveNodes.Remove(peer);

        foreach (var node in inactiveNodes)
        {
            Node.RemoveNodeAddress(node);
            Logger.Log($"Node removed: {node} (Inactive)");
        }

        // Build the node list as a response
        var nodesString = Node.CurrentNodes.Count > 0
            ? string.Join(",", Node.CurrentNodes.Where(node => !string.IsNullOrWhiteSpace(node)))
            : "";

        var responseInfo = ChainInfo.CreateChainInfo(_blockchain, nodesString);
        return JsonSerializer.Serialize(responseInfo);
    }

    private async Task<string> HandleVote(string message)
    {
        const string prefix = "Vote:";

        // Ensure the message starts with the "Vote:" prefix
        if (!message.StartsWith(prefix))
        {
            Logger.Log("ERROR: Invalid Vote message received.");
            return "ERROR: Invalid vote format.";
        }

        try
        {
            // Decode the block from the message
            var base64 = message.Substring(prefix.Length);
            var block = Block.FromBase64(base64);

            if (block != null)
            {
                var hash = block.Hash;
                var calculatedHash = block.CalculateHash();

                // Validate the block's hash
                if (calculatedHash == hash)
                    return $"ok#{Config.Default.MinerAddress}";
            }
        }
        catch (Exception e)
        {
            Logger.Log($"ERROR: Invalid Vote message. {e.Message}");
        }

        return "ERROR: Invalid block or hash mismatch.";
    }

    private async Task<string> HandleNewBlocks(string message)
    {
        try
        {
            var responseInfo = ChainInfo.CreateChainInfo(_blockchain, "Error: Block(s) rejected");

            if (_blockchain != null && _blockchain.Chain != null)
            {
                var allBlocksAdded = true;

                // Synchronize access to the blockchain
                lock (_blockchain.Chain)
                {
                    var chainInfo = JsonSerializer.Deserialize<ChainInfo>(message);
                    if (chainInfo != null)
                    {
                        var blocks = Blockchain.DecodeBlocksFromBase64(chainInfo.Message);
                        foreach (var block in blocks)
                        {
                            if (block.Nonce == -1) _blockchain.Clear();

                            // Add the block and check validity
                            if (!_blockchain.AddBlock(block, false))
                            {
                                allBlocksAdded = false;
                                break;
                            }
                        }
                    }
                }

                responseInfo.Message = allBlocksAdded ? "ok" : "Error: One or more blocks rejected";
            }

            return JsonSerializer.Serialize(responseInfo);
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"ERROR: {ex.Message}\n{ex.StackTrace}");
            return "Error: Unexpected server error";
        }
    }

    private async Task<string> HandleChainInfo(string message)
    {
        if (Config.Default.Debug)
            Logger.Log($"Decrypted BlockCount message: {message}");

        if (_blockchain?.Chain != null)
        {
            if (!string.IsNullOrEmpty(message))
            {
                ChainInfo chainInfo = null;

                try
                {
                    // Deserialize the chain information
                    chainInfo = JsonSerializer.Deserialize<ChainInfo>(message.Substring("ChainInfo:".Length));
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "ChainInfo deserialize failed: Invalid response structure");
                }

                if (chainInfo != null)
                    Node.AddNodeIP(chainInfo.URL);
            }

            return JsonSerializer.Serialize(ChainInfo.CreateChainInfo(_blockchain));
        }

        return "ERROR: Blockchain not initialized.";
    }

    private async Task<string> HandleGetBlocks(string range)
    {
        // Split the range into start and end blocks
        var parts = range.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var fromBlock) || !int.TryParse(parts[1], out var toBlock))
            return $"ERROR: Invalid block range: {range}";

        if (_blockchain == null || _blockchain.Chain == null || fromBlock < 0 || toBlock < fromBlock)
            return "ERROR: Invalid block range or blockchain not initialized.";

        // Adjust the end block if it exceeds the chain's length
        toBlock = Math.Min(toBlock, _blockchain.Chain.Count - 1);

        // Collect all blocks in the range
        var blocks = new List<string>();
        for (var block = fromBlock; block <= toBlock; block++)
        {
            var blockData = _blockchain.Chain?[block].ToBase64();
            if (blockData != null)
                blocks.Add(blockData);

            if (Config.Default.Debug)
                Logger.Log($"Get block {block}");
        }

        // Serialize the blocks and return them
        var responseInfo = ChainInfo.CreateChainInfo(_blockchain, JsonSerializer.Serialize(blocks));
        return JsonSerializer.Serialize(responseInfo);
    }

    private async Task<string> HandleGetBlock(string blockString)
    {
        if (!int.TryParse(blockString, out var block))
            return $"ERROR: Invalid block number: {blockString}";

        if (_blockchain == null || _blockchain.Chain == null || block < 0 ||
            block >= _blockchain.Chain.Count)
            return $"ERROR: Block {block} not found.";

        var blockData = _blockchain.Chain?[block].ToBase64();
        if (Config.Default.Debug)
            Logger.Log($"Get block {block}");

        // Create the response with the block data
        var responseInfo = ChainInfo.CreateChainInfo(_blockchain, blockData ?? "ERROR: Block data unavailable.");
        return JsonSerializer.Serialize(responseInfo);
    }

    private async Task<string> HandleVerifyCode(string message)
    {
        const string prefix = "VerifyCode:";

        // Check if the message has the correct prefix
        if (!message.StartsWith(prefix))
            return "ERROR: Invalid verification request.";

        // Extract and decompress the code
        var compressedBase64Data = message.Substring(prefix.Length);
        var code = Compress.DecompressString(Convert.FromBase64String(compressedBase64Data));


        var codecheck = "";
        var isCodeSafe = CodeSecurityAnalyzer.IsCodeSafe(code, ref codecheck);

        var result = isCodeSafe ? "ok" : $"ERROR: failed: {codecheck}";


        if (Config.Default.Debug)
            Logger.Log($"VerifyCode Result: {result}");

        return result;
    }

    private async Task<string> HandleValidateChain()
    {
        // Validate if the blockchain is correct
        var isValid = _blockchain != null && _blockchain.IsValid();
        if (Config.Default.Debug)
            Logger.Log($"Blockchain validation result: {isValid}");

        return isValid ? "ok" : "ERROR: Blockchain is invalid.";
    }

    #endregion
}