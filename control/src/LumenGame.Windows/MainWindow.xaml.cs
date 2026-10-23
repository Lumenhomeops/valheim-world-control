using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using LumenGame.Contracts;

namespace LumenGame.Windows;

public partial class MainWindow : Window
{
    private readonly ApiClient _api = new();
    private string _role = string.Empty;
    private string _serverAddress = string.Empty;

    public MainWindow() => InitializeComponent();

    protected override void OnClosed(EventArgs e)
    {
        _api.Dispose();
        base.OnClosed(e);
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        LoginMessage.Text = string.Empty;
        SignInButton.IsEnabled = false;
        try
        {
            _api.SetEndpoint(EndpointBox.Text.Trim());
            LoginResponse login = await _api.LoginAsync(UserNameBox.Text.Trim(), PasswordInput.Password, CancellationToken.None);
            _role = login.Role;
            RoleLabel.Text = $"{login.UserName} · {login.Role}";
            ActivateButton.IsEnabled = string.Equals(_role, "Admin", StringComparison.Ordinal);
            AdminUsersPanel.Visibility = ActivateButton.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
            ActionMessage.Text = ActivateButton.IsEnabled
                ? "Administrator changes are validated, audited, and rolled back on failure."
                : "User access is read-only. You can browse the library and copy the server address.";
            await LoadDashboardAsync();
            LoginView.Visibility = Visibility.Collapsed;
            DashboardView.Visibility = Visibility.Visible;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or ArgumentException or InvalidDataException or TaskCanceledException)
        {
            LoginMessage.Text = exception is TaskCanceledException ? "The control API did not respond in time." : exception.Message;
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }

    private async Task LoadDashboardAsync()
    {
        LibraryResponse library = await _api.GetLibraryAsync(CancellationToken.None);
        ServerStatusResponse status = await _api.GetStatusAsync(CancellationToken.None);
        WorldList.ItemsSource = library.Worlds;
        ModPackList.ItemsSource = library.ModPacks;
        WorldInfo? activeWorld = library.Worlds.FirstOrDefault(world => world.Id == status.ActiveWorldId);
        ModPackInfo? activeModPack = library.ModPacks.FirstOrDefault(pack => pack.Id == status.ActiveModPackId);
        WorldList.SelectedItem = activeWorld ?? library.Worlds.FirstOrDefault();
        if (activeWorld is not null && status.ActiveProfileId is not null)
        {
            ProfileList.SelectedItem = activeWorld.Profiles.FirstOrDefault(profile => profile.Id == status.ActiveProfileId)
                ?? ProfileList.SelectedItem;
        }
        ModPackList.SelectedItem = activeModPack ?? library.ModPacks.FirstOrDefault();
        PlayerBasedRaidsCheckBox.IsChecked = status.PlayerBasedRaids;
        PassiveEnemiesCheckBox.IsChecked = status.PassiveEnemies;
        _serverAddress = status.Address;
        AddressLabel.Text = $"{status.ServerName} · {status.Address} · {status.ConnectedPlayers} player(s)";
        string profileName = activeWorld?.Profiles.FirstOrDefault(profile => profile.Id == status.ActiveProfileId)?.DisplayName
            ?? status.ActiveProfileId
            ?? "unknown profile";
        string worldName = status.ActiveWorldName ?? activeWorld?.DisplayName ?? "unknown world";
        string modPackName = activeModPack?.DisplayName ?? status.ActiveModPackId ?? "unknown mod pack";
        List<string> modifiers = [];
        if (status.PlayerBasedRaids) modifiers.Add("Player-Based Raids");
        if (status.PassiveEnemies) modifiers.Add("Passive Enemies");
        string modifierSummary = modifiers.Count == 0 ? "no optional modifiers" : string.Join(" + ", modifiers);
        RunningSelectionLabel.Text = $"Currently running: {worldName} / {profileName} / {modPackName} / {modifierSummary} · checked {status.ObservedAt.ToLocalTime():g}";
        StateLabel.Text = string.Equals(status.State, "running", StringComparison.OrdinalIgnoreCase) && status.ActiveWorldId is not null
            ? "verified running"
            : status.State;
    }

    private void WorldList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorldList.SelectedItem is not WorldInfo world)
        {
            ProfileList.ItemsSource = null;
            return;
        }

        ProfileList.ItemsSource = world.Profiles;
        ProfileList.SelectedItem = world.Profiles.FirstOrDefault(profile => profile.IsOriginal) ?? world.Profiles.FirstOrDefault();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await LoadDashboardAsync();
            ActionMessage.Text = "Server status and library refreshed.";
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or InvalidDataException or TaskCanceledException)
        {
            ActionMessage.Text = exception.Message;
        }
    }

    private async void Activate_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(_role, "Admin", StringComparison.Ordinal) ||
            WorldList.SelectedItem is not WorldInfo world ||
            ProfileList.SelectedItem is not ProfileInfo profile ||
            ModPackList.SelectedItem is not ModPackInfo modPack)
        {
            return;
        }

        ActivateButton.IsEnabled = false;
        ActionMessage.Text = "Submitting the validated selection…";
        try
        {
            await _api.ActivateAsync(new ActivationRequest(
                world.Id,
                profile.Id,
                modPack.Id,
                PlayerBasedRaidsCheckBox.IsChecked == true,
                PassiveEnemiesCheckBox.IsChecked == true), CancellationToken.None);
            ActionMessage.Text = "Selection activated successfully.";
            await LoadDashboardAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            ActionMessage.Text = exception.Message;
        }
        finally
        {
            ActivateButton.IsEnabled = string.Equals(_role, "Admin", StringComparison.Ordinal);
        }
    }

    private void CopyAddress_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_serverAddress))
        {
            return;
        }

        Clipboard.SetText(_serverAddress);
        ActionMessage.Text = $"Copied {_serverAddress} to the clipboard.";
    }

    private async void SaveUser_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(_role, "Admin", StringComparison.Ordinal))
        {
            return;
        }

        SaveUserButton.IsEnabled = false;
        UserMessage.Text = "Saving User login…";
        try
        {
            UserSummary user = await _api.CreateOrUpdateUserAsync(
                new CreateUserRequest(NewUserNameBox.Text, NewUserPasswordBox.Password), CancellationToken.None);
            UserMessage.Text = $"{user.UserName} is ready with read-only User access.";
            NewUserPasswordBox.Clear();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or InvalidDataException or TaskCanceledException)
        {
            UserMessage.Text = exception.Message;
        }
        finally
        {
            SaveUserButton.IsEnabled = true;
        }
    }

}
