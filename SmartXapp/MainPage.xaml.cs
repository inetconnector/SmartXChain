using System.Diagnostics;
using System.IO;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using SmartXChain.Utils;
using FileSystem = SmartXChain.Utils.FileSystem;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmartXapp;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        Logger.OnLog += AddLogMessage;

        Task.Run(async () =>
        {
            await BlockchainHelper.InitializeApplicationAsync();
            _ = await BlockchainHelper.StartServerAsync();
        });
    }

    private void AddLogMessage(string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LogEditor.Text += message + Environment.NewLine;
            LogEditor.CursorPosition = LogEditor.Text.Length;
        });
    }

    private async void OnImportSCXTokensClicked(object sender, EventArgs e)
    {
        var recipient = await DisplayPromptAsync("Import SCX Tokens", "Enter recipient address:");
        if (string.IsNullOrWhiteSpace(recipient))
        {
            await DisplayAlert("Import SCX Tokens", "Recipient is required.", "OK");
            return;
        }

        var fileResult = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select SCX token export",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, new[] { "text/plain" } },
                { DevicePlatform.WinUI, new[] { ".txt" } },
                { DevicePlatform.iOS, new[] { "public.plain-text" } },
                { DevicePlatform.MacCatalyst, new[] { "public.plain-text" } }
            })
        });


        if (fileResult == null)
        {
            Logger.Log("Token import cancelled by user.");
            return;
        }

        await using var stream = await fileResult.OpenReadAsync();
        using var reader = new StreamReader(stream);
        var fileContent = await reader.ReadToEndAsync();

        var success = await BlockchainHelper.ImportAmountFromFile(recipient, fileContent);
        await DisplayAlert("Import SCX Tokens",
            success ? "Tokens imported successfully." : "Token import failed.", "OK");
    }

    private async void OnSendSCXTokensClicked(object sender, EventArgs e)
    {
        // Logic to send SCX tokens
        var recipient = await DisplayPromptAsync("Send SCX Tokens", "Enter recipient address:");
        var amount = await DisplayPromptAsync("Send SCX Tokens", "Enter amount:");
        if (!string.IsNullOrEmpty(recipient) && decimal.TryParse(amount, out var parsedAmount))
        {
            var data = await DisplayPromptAsync("Send SCX Tokens", "Enter additional data (optional):", "OK", "Skip");
            var success = await BlockchainHelper.SendNativeTokens(recipient, parsedAmount, data);
            await DisplayAlert(success ? "Success" : "Error",
                success ? "Tokens sent successfully." : "Token transfer failed.", "OK");
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
        var fileResult = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select C# smart contract",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, new[] { "text/plain", "application/octet-stream" } },
                { DevicePlatform.WinUI, new[] { ".cs" } },
                { DevicePlatform.MacCatalyst, new[] { "public.c-sharp-source" } },
                { DevicePlatform.iOS, new[] { "public.c-sharp-source" } }
            })
        });

        if (fileResult == null)
        {
            Logger.Log("Smart contract upload cancelled by user.");
            return;
        }

        await using var stream = await fileResult.OpenReadAsync();
        using var reader = new StreamReader(stream);
        var contractCode = await reader.ReadToEndAsync();

        var success = await BlockchainHelper.UploadSmartContract(fileResult.FileName, contractCode);
        await DisplayAlert("Upload Contract",
            success ? "Contract uploaded successfully." : "Failed to upload contract.", "OK");
    }

    private async void OnRunSmartContractDemoClicked(object sender, EventArgs e)
    {
        // Logic to run Smart Contract demo
        await BlockchainHelper.RunSmartContractDemoAsync();
        await DisplayAlert("Smart Contract Demo", "Smart contract demo ran successfully.", "OK");
    }

    private async void OnConfigButtonClicked(object? sender, EventArgs e)
    {
        var configContent = File.ReadAllText(FileSystem.ConfigFile);

        var editConfigPage = new EditConfigPage(configContent);
        editConfigPage.ConfigSaved += async editedConfig =>
        {
            if (!string.IsNullOrEmpty(editedConfig))
            {
                var directoryPath = Path.GetDirectoryName(FileSystem.ConfigFile);
                if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                    Directory.CreateDirectory(directoryPath);

                File.WriteAllText(FileSystem.ConfigFile, editedConfig);

                var restartConfirmed = await DisplayAlert("Restart Required",
                    "The application needs to restart to apply changes. Restart now?", "Yes", "No");
                if (restartConfirmed) await RestartApplicationAsync();
            }
        };

        await Navigation.PushModalAsync(editConfigPage);
    }

    private async Task RestartApplicationAsync()
    {
        if (DeviceInfo.Current.Platform == DevicePlatform.Android)
        {
            await DisplayAlert("Restart Required", "Please close and reopen the app to apply changes.", "OK");
            Application.Current?.Quit();
            return;
        }

#if WINDOWS
        var startupPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(startupPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = startupPath,
                UseShellExecute = true
            });
            await Task.Delay(500);
        }
#endif

        Application.Current?.Quit();
    }

    private void OnExtendedLoggingClicked(object? sender, EventArgs e)
    {
        Config.Default.SetProperty(Config.ConfigKey.Debug,
            (!Config.Default.Debug).ToString());
    }
}