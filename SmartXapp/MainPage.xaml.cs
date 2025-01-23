using Microsoft.Maui.Controls;
using SmartXChain.Utils;
using System.Diagnostics;

namespace SmartXapp;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent(); 
        Logger.OnLog += AddLogMessage;
    }

    private void AddLogMessage(string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LogEditor.Text += message + Environment.NewLine; 
            LogEditor.CursorPosition = LogEditor.Text.Length;
        });
    }

    private async void OnInitializeBlockchainClicked(object sender, EventArgs e)
    {
        // Initialize blockchain logic
        await BlockchainHelper.InitializeApplicationAsync();
        //await DisplayAlert("Initialization", "Blockchain initialized successfully.", "OK");

        // Start the blockchain server
        var (_, startup) = await BlockchainHelper.StartServerAsync();
        //await DisplayAlert("Server Start", "Blockchain server started successfully.", "OK");
    } 
    private async void OnImportSCXTokensClicked(object sender, EventArgs e)
    {
        // Logic to import SCX tokens
        await BlockchainHelper.ImportAmountFromFile();
    }

    private async void OnSendSCXTokensClicked(object sender, EventArgs e)
    {
        // Logic to send SCX tokens
        var recipient = await DisplayPromptAsync("Send SCX Tokens", "Enter recipient address:");
        var amount = await DisplayPromptAsync("Send SCX Tokens", "Enter amount:");
        if (!string.IsNullOrEmpty(recipient) && decimal.TryParse(amount, out var parsedAmount))
        {
            await BlockchainHelper.SendNativeTokens(recipient, parsedAmount);
            await DisplayAlert("Success", "Tokens sent successfully.", "OK");
        }
        else
        {
            await DisplayAlert("Error", "Invalid input.", "OK");
        }
    }

    private async void OnDisplayBlockchainStateClicked(object sender, EventArgs e)
    {
        // Logic to display blockchain state
        var state = await BlockchainHelper.GetBlockchainState();
        await DisplayAlert("Blockchain State", state, "OK");
    }

    private async void OnDisplayWalletBalancesClicked(object sender, EventArgs e)
    {
        // Logic to display wallet balances
        var balances = await BlockchainHelper.GetWalletBalances();
        await DisplayAlert("Wallet Balances", balances, "OK");
    }

    private async void OnUploadSmartContractClicked(object sender, EventArgs e)
    {
        // Logic to upload smart contract
        var fileName = await DisplayPromptAsync("Upload Contract", "Enter contract filename:");
        if (!string.IsNullOrEmpty(fileName))
        {
            var success = await BlockchainHelper.UploadSmartContract(fileName);
            await DisplayAlert("Upload Contract",
                success ? "Contract uploaded successfully." : "Failed to upload contract.", "OK");
        }
    } 
    private async void OnRunSmartContractDemoClicked(object sender, EventArgs e)
    {
        // Logic to run Smart Contract demo
        await BlockchainHelper.RunSmartContractDemoAsync();
        await DisplayAlert("Smart Contract Demo", "Smart contract demo ran successfully.", "OK");
    }
    private async void OnConfigButtonClicked(object? sender, EventArgs e)
    {
        var configFilePath = Path.Combine(Config.AppDirectory(), Config.ConfigFileName());
        var configContent = File.ReadAllText(configFilePath);

        var editConfigPage = new EditConfigPage(configContent);
        editConfigPage.ConfigSaved += async editedConfig =>
        {
            if (!string.IsNullOrEmpty(editedConfig))
            {
                var directoryPath = Path.GetDirectoryName(configFilePath);
                if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath)) 
                    Directory.CreateDirectory(directoryPath);  

                File.WriteAllText(configFilePath, editedConfig);

                var restartConfirmed = await DisplayAlert("Restart Required", "The application needs to restart to apply changes. Restart now?", "Yes", "No");
                if (restartConfirmed)
                {
                    RestartApplication();
                }
            }
        };

        await Navigation.PushModalAsync(editConfigPage);
    }

    private void RestartApplication()
    {
        var startupPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(startupPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = startupPath,
                UseShellExecute = true
            });
            Application.Current.Quit();
        }
    }
}