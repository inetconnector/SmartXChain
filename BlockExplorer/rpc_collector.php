<?php
// Datei, in der die Ereignisdaten gespeichert werden
$dataFile = 'rpc_data.json';

// Sicherstellen, dass die Datei existiert und initialisiert ist
if (!file_exists($dataFile)) {
    file_put_contents($dataFile, json_encode([]));
}

// Laden der bestehenden Daten aus der Datei
$rawData = file_get_contents($dataFile);

// Zeilenendungen in der Datei normalisieren (entfernt CRLF oder andere Artefakte)
$normalizedData = str_replace(["\r\n", "\r"], "\n", $rawData);

// Versuchen, die Datei als JSON zu laden
$storedData = json_decode($normalizedData, true);

// Wenn die JSON-Dekodierung fehlschlägt, initialisiere die Daten als leeres Array
if (!is_array($storedData)) {
    $storedData = [];
}

// Prüfen, ob es sich um eine POST-Anfrage handelt
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    // Eingehende JSON-Daten lesen
    $input = file_get_contents("php://input");
    $data = json_decode($input, true);

    // Überprüfen, ob die notwendigen Felder vorhanden sind
    if (isset($data['address']) && isset($data['amount'])) {
        // Daten sammeln
        $event = [
            'timestamp' => date('Y-m-d H:i:s'),
            'address' => $data['address'],
            'amount' => $data['amount'],
            'event_type' => (strpos($_SERVER['REQUEST_URI'], 'mint') !== false) ? 'Mint' : 'Burn'
        ];

        // Daten zur Liste hinzufügen
        $storedData[] = $event;

        // Gespeicherte Daten aktualisieren
        file_put_contents($dataFile, json_encode($storedData, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES));

        // Erfolgsantwort
        echo json_encode(['status' => 'success', 'message' => 'Event stored successfully'], JSON_PRETTY_PRINT);
    } else {
        // Fehlende Felder in der Eingabe
        http_response_code(400);
        echo json_encode(['status' => 'error', 'message' => 'Invalid input data'], JSON_PRETTY_PRINT);
    }
} elseif ($_SERVER['REQUEST_METHOD'] === 'GET') {
    // Ausgabe der gesammelten Daten
    header('Content-Type: application/json');
    echo json_encode($storedData, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES);
} else {
    // Nur GET und POST sind erlaubt
    http_response_code(405);
    echo json_encode(['status' => 'error', 'message' => 'Method not allowed'], JSON_PRETTY_PRINT);
}
?>