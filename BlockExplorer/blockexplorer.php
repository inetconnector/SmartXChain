<?php
header('Content-Type: application/json');

// API-Basis-URL
$apiBase = "http://127.0.0.1:5556/api";

function fetchApi($endpoint) {
    $response = @file_get_contents($GLOBALS['apiBase'] . $endpoint);
    if ($response === false) {
        return null;
    }
    return json_decode($response, true);
}

function fetchRawApi($endpoint) {
    $response = @file_get_contents($GLOBALS['apiBase'] . $endpoint);
    if ($response === false) {
        return null;
    }
    return $response;
}

if (isset($_GET['action'])) {
    switch ($_GET['action']) {
        case 'latest-blocks':
            $blockCount = fetchApi("/GetBlockCount");
            if (!$blockCount) {
                echo json_encode(["error" => "Failed to fetch block count."]);
                exit;
            }

            $blockCount = intval($blockCount);
            $startBlock = max(1, $blockCount - 9);

            $blocks = [];
            for ($i = $startBlock; $i <= $blockCount; $i++) {
                $blockData = fetchApi("/GetBlock/$i");
                if ($blockData) {
                    $blocks[] = [
                        "index" => $i,
                        "timestamp" => $blockData['Timestamp'] ?? '',
                        "hash" => $blockData['Hash'] ?? ''
                    ];
                }
            }

            echo json_encode($blocks);
            break;

        case 'block-transactions':
            $blockId = intval($_GET['blockId'] ?? 0);
            $blockData = fetchApi("/GetBlock/$blockId");

            if (!$blockData || empty($blockData['Transactions'])) {
                echo json_encode([]);
                exit;
            }

            echo json_encode($blockData['Transactions']);
            break;

        case 'contract-code':
            $contractName = $_GET['contractName'] ?? '';
            if (!$contractName) {
                echo json_encode(["error" => "Contract name is missing."]);
                exit;
            }

            $contractCode = fetchRawApi("/GetContractCode/" . urlencode($contractName));
            if (!$contractCode) {
                echo json_encode(["error" => "Failed to fetch contract code."]);
                exit;
            }

            echo json_encode(["code" => $contractCode]);
            break;

        default:
            echo json_encode(["error" => "Invalid action."]);
    }
} else {
    echo json_encode(["error" => "Action parameter missing."]);
}
?>