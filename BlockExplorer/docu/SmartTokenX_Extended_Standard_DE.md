
### SmartTokenX-Extended (STX-E) Standard Spezifikation

#### Version: 1.1.0

---

**Zweck:**  
Der SmartTokenX-Extended-Standard baut auf dem SmartTokenX (STX)-Standard auf und fügt zusätzliche Verwaltungs- und Sicherheitsfunktionen hinzu, wie das Prägen und Verbrennen von Tokens, das Einfrieren von Konten, das Pausieren von Transfers und die Übertragung des Eigentums.

**Wichtige Funktionen:**  
1. **Prägung:** Möglichkeit, neue Tokens zu erstellen.  
2. **Verbrennung:** Möglichkeit, Tokens zu zerstören.  
3. **Konten einfrieren:** Temporäres Deaktivieren von Token-Transfers für bestimmte Konten.  
4. **Transfers pausieren/wiederaufnehmen:** Transfers global pausieren und wieder aktivieren.  
5. **Eigentumsübertragung:** Übertragung des Eigentums an einen neuen Besitzer.  
6. **Anzahl der Token-Halter:** Abrufen der Gesamtanzahl der Token-Halter.

**Verpflichtende Methoden:**  
1. `bool Mint(decimal amount, string to, string owner, string privateKey)`  
2. `bool Burn(decimal amount, string owner, string privateKey)`  
3. `void PauseTransfers(string owner, string privateKey)`  
4. `void ResumeTransfers(string owner, string privateKey)`  
5. `void FreezeAccount(string account, string owner, string privateKey)`  
6. `void UnfreezeAccount(string account, string owner, string privateKey)`  
7. `int GetTotalTokenHolders()`  
8. `void TransferOwnership(string newOwner, string currentOwner, string privateKey)`

**Ereignisse:**  
- `OnMint(string to, decimal amount)`  
- `OnBurn(string owner, decimal amount)`  
- `OnAccountFrozen(string account)`  
- `OnAccountUnfrozen(string account)`  
- `OnTransfersPaused()`  
- `OnTransfersResumed()`  

---
**Generated on:** {datetime.utcnow().strftime('%Y-%m-%d %H:%M:%S')} UTC
