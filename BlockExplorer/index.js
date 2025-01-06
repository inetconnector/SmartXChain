document.addEventListener("DOMContentLoaded", () => {
    const networkSwitch = document.getElementById("network-switch");
    const blockList = document.getElementById("blockList");
    const transactionList = document.getElementById("transactionList");
    const fetchContractCodeButton = document.getElementById("fetch-contract-code");
    const contractNameInput = document.getElementById("contract-name");

    // Dynamically generate endpoint URLs
    const getEndpoint = (network) => {
        const ports = {
            mainnet: "5555",
            testnet: "5556",
        };
        return `blockexplorer.php?port=${ports[network]}&timestamp=${Date.now()}`;
    };

    async function fetchLatestBlocks() {
        const network = networkSwitch.value;
        const url = `${getEndpoint(network)}&action=latest-blocks`;

        try {
            console.log("Fetching latest blocks from:", url); // Debugging
            const response = await fetch(url);
            if (!response.ok) {
                throw new Error(`HTTP error! Status: ${response.status}`);
            }
            const blocks = await response.json();
            console.log("Fetched blocks:", blocks); // Debugging
            blockList.innerHTML = "";
            if (!Array.isArray(blocks) || blocks.length === 0) {
                blockList.innerHTML = "<li>No blocks available.</li>";
                return;
            }
            blocks.forEach((block) => {
                const li = document.createElement("li");

                // Create link for the block
                const link = document.createElement("a");
                link.href = "#";
                link.textContent = `Block #${block.index} - Date: ${block.timestamp}    ${block.hash}`;
                link.addEventListener("click", (e) => {
                    e.preventDefault();
                    fetchBlockTransactions(block.index);
                });

                li.appendChild(link);
                blockList.appendChild(li);
            });
        } catch (error) {
            console.error("Error fetching latest blocks:", error);
        }
    }

    async function fetchBlockTransactions(blockId) {
        const network = networkSwitch.value;
        const url = `${getEndpoint(network)}&action=block-transactions&blockId=${blockId}`;

        // Enum for transaction types
        const transactionTypeMapping = [
            "NotDefined",
            "NativeTransfer",
            "MinerReward",
            "ContractCode",
            "ContractState",
            "Gas",
            "ValidatorReward",
            "Data"
        ];

        const SYSTEM_ADDRESS = "smartX0000000000000000000000000000000000000000";

        try {
            console.log("Fetching block transactions from:", url); // Debugging
            const response = await fetch(url);
            if (!response.ok) {
                throw new Error(`HTTP error! Status: ${response.status}`);
            }
            const transactions = await response.json();
            console.log("Fetched transactions:", transactions); // Debugging
            transactionList.innerHTML = "";

            if (!Array.isArray(transactions) || transactions.length === 0) {
                transactionList.innerHTML = "<li>No transactions found for this block.</li>";
                return;
            }

            transactions.forEach((tx, index) => {
                const li = document.createElement("li");

                // Format transaction details
                const transactionDetails = `
                    <strong>Transaction ${index + 1}:</strong>
                    <ul>
                        <li><strong>Type:</strong> ${
                            transactionTypeMapping[tx.TransactionType] || "Unknown"
                        }</li>
                        <li><strong>Sender:</strong> ${
                            tx.Sender === SYSTEM_ADDRESS ? "System" : tx.Sender || "N/A"
                        }</li>
                        <li><strong>Recipient:</strong> ${
                            tx.Recipient === SYSTEM_ADDRESS ? "System" : tx.Recipient || "N/A"
                        }</li>
                        <li><strong>Amount:</strong> ${tx.Amount || 0}</li>
                        <li><strong>Gas:</strong> ${tx.Gas || "N/A"}</li>
                        <li><strong>Timestamp:</strong> ${tx.Timestamp || "N/A"}</li>
                        <li><strong>Data:</strong> ${
                            tx.Data ? truncate(decodeBase64(tx.Data), 80) : "N/A"
                        }</li>
                        <li><strong>Info:</strong> ${tx.Info || "N/A"}</li>
                    </ul>
                `;

                li.innerHTML = transactionDetails;
                transactionList.appendChild(li);
            });
        } catch (error) {
            console.error("Error fetching block transactions:", error);
            transactionList.innerHTML = "<li>Error fetching transactions. Please try again later.</li>";
        }
    }

    // Utility function to decode Base64 data
    function decodeBase64(encodedData) {
        try {
            return atob(encodedData);
        } catch (e) {
            console.error("Error decoding Base64 data:", e);
            return "Invalid Base64";
        }
    }

    // Utility function to truncate strings
    function truncate(str, length) {
        if (!str || str.length <= length) {
            return str;
        }
        return str.substring(0, length) + "...";
    }

    async function fetchContractCode(contractName) {
        const network = networkSwitch.value;
        const url = `${getEndpoint(network)}&action=contract-code&contractName=${encodeURIComponent(contractName)}`;

        try {
            console.log("Fetching contract code from:", url); // Debugging
            const response = await fetch(url);
            if (!response.ok) {
                throw new Error(`HTTP error! Status: ${response.status}`);
            }

            const data = await response.json();
            if (data.error) {
                alert(data.error);
                return;
            }

            alert(`Contract Code for ${contractName}:\n\n${data.code}`);
        } catch (error) {
            console.error("Error fetching contract code:", error);
            alert("An error occurred while fetching the contract code.");
        }
    }

    fetchContractCodeButton.addEventListener("click", () => {
        const contractName = contractNameInput.value.trim();
        if (contractName) {
            fetchContractCode(contractName);
        } else {
            alert("Please enter a contract name.");
        }
    });

    networkSwitch.addEventListener("change", fetchLatestBlocks);

    // Load initial blocks for default network
    fetchLatestBlocks();
});
