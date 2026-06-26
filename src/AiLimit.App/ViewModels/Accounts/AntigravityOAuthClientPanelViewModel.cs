using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiLimit.Core.Providers;

namespace AiLimit.App.ViewModels.Accounts;

public sealed class AntigravityOAuthClientRowViewModel
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? ClientId { get; init; }
    public bool IsBuiltIn { get; init; }
    public bool IsActive { get; set; }
    public bool CanRemove => !IsBuiltIn;
}

public sealed class AntigravityOAuthClientPanelViewModel : INotifyPropertyChanged
{
    private readonly AntigravityOAuthClientRegistry _registry;

    public AntigravityOAuthClientPanelViewModel(AntigravityOAuthClientRegistry registry)
    {
        _registry = registry;
        Reload();
    }

    public ObservableCollection<AntigravityOAuthClientRowViewModel> Clients { get; } = new();

    private string _newLabel = "";
    private string _newClientId = "";
    private string _newClientSecret = "";

    public string NewLabel { get => _newLabel; set { _newLabel = value; OnPropertyChanged(); } }
    public string NewClientId { get => _newClientId; set { _newClientId = value; OnPropertyChanged(); } }
    public string NewClientSecret { get => _newClientSecret; set { _newClientSecret = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void AddClient()
    {
        if (string.IsNullOrWhiteSpace(NewClientId)) { return; }
        var entry = _registry.Add(
            string.IsNullOrWhiteSpace(NewLabel) ? NewClientId : NewLabel, NewClientId, NewClientSecret);
        _registry.SetActive(entry.Key);
        NewLabel = NewClientId = NewClientSecret = "";
        Reload();
    }

    public void SelectActive(string key) { _registry.SetActive(key); Reload(); }

    public void RemoveClient(string key) { _registry.Remove(key); Reload(); }

    private void Reload()
    {
        var active = _registry.GetActive()?.Key;
        Clients.Clear();
        foreach (var c in _registry.List())
        {
            Clients.Add(new AntigravityOAuthClientRowViewModel
            {
                Key = c.Key, Label = c.Label, ClientId = c.ClientId,
                IsBuiltIn = c.IsBuiltIn, IsActive = c.Key == active,
            });
        }
        OnPropertyChanged(nameof(Clients));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
