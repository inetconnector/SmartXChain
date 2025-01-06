
### SmartTokenX (STX) Standard Specification

**Version:** 1.0.0  
**Zweck:**  
Der SmartTokenX-Standard definiert die Spezifikationen für die Erstellung sicherer, fungibler Tokens, die Benutzerregistrierung, Authentifizierung und klassische Funktionen wie Übertragungen, Genehmigungen und Token-Übertragungen durch Drittparteien unterstützen.

---

### 1. Eigenschaften des Standards

#### 1.1 Allgemeine Eigenschaften
- **Name:** Der Name des Tokens.
- **Symbol:** Abkürzung des Tokens (z. B. "STX").
- **Decimals:** Anzahl der Nachkommastellen (z. B. `2` für 0.01).
- **TotalSupply:** Gesamtanzahl der Tokens im Umlauf.

#### 1.2 Erweiterte Funktionen
- **Benutzerregistrierung:** Benutzer können sich mit einer Adresse und einem privaten Schlüssel registrieren.
- **Benutzerauthentifizierung:** Transaktionen und Operationen erfordern eine Authentifizierung durch den registrierten privaten Schlüssel.
- **Erweiterte Ereignisse:** Neben klassischen Transfer- und Genehmigungsereignissen bietet der Standard TransferFrom- und Logging-Ereignisse.

---

### 2. Verpflichtende Methoden

#### 2.1 Token-Management
1. **`decimal BalanceOf(string account)`**  
   Gibt die Balance eines bestimmten Kontos zurück.  
   - **Input:** `account` (Adresse des Kontos).  
   - **Output:** Balance als Dezimalwert.

2. **`decimal Allowance(string owner, string spender)`**  
   Gibt den verbleibenden Betrag zurück, den ein Spender ausgeben darf.  
   - **Input:** `owner` (Adresse des Kontobesitzers), `spender` (Adresse des Spenders).  
   - **Output:** Zugelassener Betrag als Dezimalwert.

#### 2.2 Transaktionen
1. **`bool Transfer(string from, string to, decimal amount, string privateKey)`**  
   Überträgt Token von einem Benutzer zu einem anderen, basierend auf der Authentifizierung.  
   - **Input:** Absenderadresse, Empfängeradresse, Betrag, privater Schlüssel.  
   - **Output:** `true`, wenn erfolgreich.

2. **`bool Approve(string owner, string spender, decimal amount, string privateKey)`**  
   Erlaubt es einem Spender, Token des Besitzers auszugeben.  
   - **Input:** Besitzeradresse, Spenderadresse, Betrag, privater Schlüssel.  
   - **Output:** `true`, wenn erfolgreich.

3. **`bool TransferFrom(string spender, string from, string to, decimal amount, string spenderKey)`**  
   Überträgt Token im Namen eines Besitzers, basierend auf vorheriger Genehmigung.  
   - **Input:** Spenderadresse, Besitzeradresse, Empfängeradresse, Betrag, privater Schlüssel des Spenders.  
   - **Output:** `true`, wenn erfolgreich.

---

### 3. Erweiterte Funktionen

#### 3.1 Benutzerregistrierung und Authentifizierung
1. **`bool RegisterUser(string address, string privateKey)`**  
   Registriert einen Benutzer mit einer eindeutigen Adresse und speichert den gehashten Schlüssel.  
   - **Input:** Adresse, privater Schlüssel.  
   - **Output:** `true`, wenn die Registrierung erfolgreich ist.

2. **`bool IsAuthenticated(string address, string privateKey)`**  
   Überprüft, ob ein Benutzer authentifiziert ist.  
   - **Input:** Adresse, privater Schlüssel.  
   - **Output:** `true`, wenn authentifiziert.

#### 3.2 Sicherheitsmethoden
1. **`bool IsValidAddress(string address)`**  
   Überprüft, ob eine Adresse ein gültiges Format hat (z. B. muss sie mit "smartX" beginnen).  
   - **Input:** Adresse.  
   - **Output:** `true`, wenn gültig.

---

### 4. Ereignisse

1. **`TransferEvent(string from, string to, decimal amount)`**  
   Wird ausgelöst, wenn Token zwischen Konten übertragen werden.

2. **`ApprovalEvent(string owner, string spender, decimal amount)`**  
   Wird ausgelöst, wenn ein Spender genehmigt wird, Token eines Besitzers auszugeben.

3. **`TransferFromEvent(string spender, string from, string to, decimal amount)`**  
   Wird ausgelöst, wenn ein Spender Token im Namen eines Besitzers überträgt.

4. **`Log(string message)`**  
   Wird für interne Protokollierung und Fehlermeldungen verwendet.

---

### 5. Beispielimplementierung

Die bereitgestellte Implementierung dient als Beispiel für diesen Standard. Entwickler können den Code verwenden und die Methoden anpassen, um spezifische Anforderungen zu erfüllen.

---

### 6. Vorteile des SmartTokenX-Standards
- **Sicherheit:** Authentifizierung durch private Schlüssel und Benutzerregistrierung.
- **Erweiterbarkeit:** Leicht anpassbar für zusätzliche Funktionen.
- **Kompatibilität:** Unterstützt Standardmethoden, die in gängigen Anwendungen verwendet werden.

---
**Erstellt am:** {datetime.utcnow().strftime('%Y-%m-%d %H:%M:%S')} UTC
