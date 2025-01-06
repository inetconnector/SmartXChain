
### SmartTokenX-Extended (STX-E) Standard Specification

#### Version: 1.1.0

---

**Purpose:**  
The SmartTokenX-Extended standard builds upon the SmartTokenX (STX) standard to include additional administrative and security features like minting, burning, freezing accounts, pausing transfers, and transferring ownership.

**Key Features:**  
1. **Minting:** Ability to create new tokens.
2. **Burning:** Ability to destroy tokens.
3. **Freezing Accounts:** Temporarily disable token transfers for specific accounts.
4. **Pause/Resume Transfers:** Globally pause and resume token transfers.
5. **Ownership Transfer:** Allow transfer of token ownership to another address.
6. **Total Token Holders:** Retrieve the total number of token holders.

**Mandatory Methods:**  
1. `bool Mint(decimal amount, string to, string owner, string privateKey)`  
2. `bool Burn(decimal amount, string owner, string privateKey)`  
3. `void PauseTransfers(string owner, string privateKey)`  
4. `void ResumeTransfers(string owner, string privateKey)`  
5. `void FreezeAccount(string account, string owner, string privateKey)`  
6. `void UnfreezeAccount(string account, string owner, string privateKey)`  
7. `int GetTotalTokenHolders()`  
8. `void TransferOwnership(string newOwner, string currentOwner, string privateKey)`

**Events:**  
- `OnMint(string to, decimal amount)`  
- `OnBurn(string owner, decimal amount)`  
- `OnAccountFrozen(string account)`  
- `OnAccountUnfrozen(string account)`  
- `OnTransfersPaused()`  
- `OnTransfersResumed()`  
