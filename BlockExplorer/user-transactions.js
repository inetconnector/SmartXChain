
document.addEventListener("DOMContentLoaded", () => {
    const form = document.getElementById("transaction-query-form");
    const transactionsList = document.getElementById("transactions-list");

    form.addEventListener("submit", async (event) => {
        event.preventDefault();

        const username = document.getElementById("username").value;
        if (!username) {
            alert("User name is required.");
            return;
        }

        const networkSwitch = document.getElementById("network-switch");
        const network = networkSwitch ? networkSwitch.value : "mainnet";

        const ports = { mainnet: "5555", testnet: "5556" };
        const url = `blockexplorer.php?port=${ports[network]}&action=get-user-transactions&user=${username}`;

        try {
            const response = await fetch(url);
            if (!response.ok) {
                throw new Error(`Error fetching transactions: ${response.statusText}`);
            }

            const transactions = await response.json();
            transactionsList.innerHTML = "";

            if (transactions.error) {
                transactionsList.textContent = transactions.error;
                return;
            }

            if (transactions.length === 0) {
                transactionsList.textContent = "No transactions found for this user.";
                return;
            }

            transactions.forEach(tx => {
                const txElement = document.createElement("div");
                txElement.className = "transaction";
                txElement.innerHTML = `
                    <p><strong>ID:</strong> ${tx.Id}</p>
                    <p><strong>Sender:</strong> ${tx.Sender}</p>
                    <p><strong>Recipient:</strong> ${tx.Recipient}</p>
                    <p><strong>Amount:</strong> ${tx.Amount}</p>
                    <p><strong>Timestamp:</strong> ${tx.Timestamp}</p>
                    <p><strong>Data:</strong> ${tx.Data}</p>
                `;
                transactionsList.appendChild(txElement);
            });
        } catch (error) {
            console.error(error);
            transactionsList.textContent = "Failed to fetch transactions. Please try again.";
        }
    });
});
