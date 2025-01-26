namespace SmartXapp;

public partial class EditConfigPage : ContentPage
{
    private string _initialConfig;
    public event Action<string> ConfigSaved;

    public EditConfigPage(string initialConfig)
    {
        InitializeComponent();
        _initialConfig = initialConfig;
        ConfigEditor.Text = initialConfig;
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        ConfigSaved?.Invoke(ConfigEditor.Text);
        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();  
    }
}
