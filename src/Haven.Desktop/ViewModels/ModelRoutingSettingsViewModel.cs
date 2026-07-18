/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/ModelRoutingSettingsViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns ModelRoutingSettingsViewModel, ProviderRoutingSettingsItemViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using System.Globalization;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents model routing settings view model and keeps its related state and behavior together.
/// </summary>
public sealed class ModelRoutingSettingsViewModel : ObservableObject
{
    /// <summary>
    /// Stores configurations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IProviderConfigurationStore _configurations;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Loading routing policies…";
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isBusy;

    public ModelRoutingSettingsViewModel(IProviderConfigurationStore configurations)
    {
        _configurations = configurations;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        SaveCommand = new AsyncRelayCommand<ProviderRoutingSettingsItemViewModel>(SaveAsync);
        _ = RefreshAsync();
    }

    /// <summary>
    /// Gets or updates items, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ProviderRoutingSettingsItemViewModel> Items { get; } = [];
    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>
    /// Gets or updates save command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<ProviderRoutingSettingsItemViewModel> SaveCommand { get; }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Reports whether busy applies to the current state.
    /// </summary>
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    /// <summary>
    /// Performs refresh asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RefreshAsync()
    {
        try
        {
            IsBusy = true;
            var configurations = await _configurations.GetAllAsync(CancellationToken.None);
            Items.Clear();
            foreach (var configuration in configurations.OrderByDescending(item => item.IsLocal).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
                Items.Add(new ProviderRoutingSettingsItemViewModel(configuration));
            Status = $"{Items.Count} provider routing polic{(Items.Count == 1 ? "y" : "ies")} available.";
        }
        catch (Exception ex)
        {
            Status = "Could not load routing policies: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Performs save asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SaveAsync(ProviderRoutingSettingsItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            item.IsBusy = true;
            item.Status = "Saving…";
            var metadata = new Dictionary<string, string>(item.Configuration.Metadata, StringComparer.OrdinalIgnoreCase);
            SetOrRemove(metadata, "fallback-chain", item.FallbackChain);
            SetPrice(metadata, "input-price-per-million", item.InputPricePerMillion);
            SetPrice(metadata, "output-price-per-million", item.OutputPricePerMillion);
            SetPrice(metadata, "cached-price-per-million", item.CachedPricePerMillion);
            SetPrice(metadata, "reasoning-price-per-million", item.ReasoningPricePerMillion);
            var currency = item.PricingCurrency.Trim().ToUpperInvariant();
            if (currency.Length is not (0 or 3) || currency.Any(character => !char.IsLetter(character)))
                throw new InvalidOperationException("Pricing currency must be a three-letter code such as GBP or USD.");
            SetOrRemove(metadata, "pricing-currency", currency);
            var saved = item.Configuration with
            {
                AllowCloudFallback = item.AllowCloudFallback,
                Metadata = metadata,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _configurations.UpsertAsync(saved, CancellationToken.None);
            item.Configuration = saved;
            item.Status = item.AllowCloudFallback
                ? "Saved. Cloud fallback is permitted only before output or tool side effects."
                : "Saved. This provider will not fall back from local to cloud.";
            Status = $"Saved routing and pricing for {item.DisplayName}.";
        }
        catch (Exception ex)
        {
            item.Status = "Save failed: " + ex.Message;
            Status = $"Could not save {item.DisplayName}.";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    /// <summary>
    /// Performs the set price step owned by this component.
    /// </summary>
    private static void SetPrice(IDictionary<string, string> metadata, string key, string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            metadata.Remove(key);
            return;
        }
        if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price < 0)
            throw new InvalidOperationException("Token prices must be non-negative numbers using a dot for decimals.");
        metadata[key] = price.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Performs the set or remove step owned by this component.
    /// </summary>
    private static void SetOrRemove(IDictionary<string, string> metadata, string key, string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) metadata.Remove(key);
        else metadata[key] = trimmed;
    }
}

/// <summary>
/// Represents provider routing settings item view model and keeps its related state and behavior together.
/// </summary>
public sealed class ProviderRoutingSettingsItemViewModel : ObservableObject
{
    /// <summary>
    /// Stores configuration locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ProviderConfiguration _configuration;
    /// <summary>
    /// Stores allow cloud fallback locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _allowCloudFallback;
    /// <summary>
    /// Stores fallback chain locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _fallbackChain;
    /// <summary>
    /// Stores input price locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _inputPrice;
    /// <summary>
    /// Stores output price locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _outputPrice;
    /// <summary>
    /// Stores cached price locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _cachedPrice;
    /// <summary>
    /// Stores reasoning price locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _reasoningPrice;
    /// <summary>
    /// Stores currency locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _currency;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status;
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isBusy;

    public ProviderRoutingSettingsItemViewModel(ProviderConfiguration configuration)
    {
        _configuration = configuration;
        _allowCloudFallback = configuration.AllowCloudFallback;
        _fallbackChain = Read(configuration, "fallback-chain");
        _inputPrice = Read(configuration, "input-price-per-million");
        _outputPrice = Read(configuration, "output-price-per-million");
        _cachedPrice = Read(configuration, "cached-price-per-million");
        _reasoningPrice = Read(configuration, "reasoning-price-per-million");
        _currency = Read(configuration, "pricing-currency");
        _status = configuration.IsLocal ? "Local provider. Cost remains blank unless you configure a rate." : "Cloud provider policy.";
    }

    /// <summary>
    /// Gets or updates configuration, the bindable or domain state represented by this property.
    /// </summary>
    public ProviderConfiguration Configuration { get => _configuration; set => SetProperty(ref _configuration, value); }
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public string Id => Configuration.Id;
    /// <summary>
    /// Gets or updates display name, the bindable or domain state represented by this property.
    /// </summary>
    public string DisplayName => Configuration.DisplayName;
    /// <summary>
    /// Gets or updates provider type, the bindable or domain state represented by this property.
    /// </summary>
    public string ProviderType => Configuration.Kind.ToString();
    /// <summary>
    /// Reports whether local applies to the current state.
    /// </summary>
    public bool IsLocal => Configuration.IsLocal;
    /// <summary>
    /// Gets or updates allow cloud fallback, the bindable or domain state represented by this property.
    /// </summary>
    public bool AllowCloudFallback { get => _allowCloudFallback; set => SetProperty(ref _allowCloudFallback, value); }
    /// <summary>
    /// Gets or updates fallback chain, the bindable or domain state represented by this property.
    /// </summary>
    public string FallbackChain { get => _fallbackChain; set => SetProperty(ref _fallbackChain, value); }
    /// <summary>
    /// Gets or updates input price per million, the bindable or domain state represented by this property.
    /// </summary>
    public string InputPricePerMillion { get => _inputPrice; set => SetProperty(ref _inputPrice, value); }
    /// <summary>
    /// Gets or updates output price per million, the bindable or domain state represented by this property.
    /// </summary>
    public string OutputPricePerMillion { get => _outputPrice; set => SetProperty(ref _outputPrice, value); }
    /// <summary>
    /// Gets or updates cached price per million, the bindable or domain state represented by this property.
    /// </summary>
    public string CachedPricePerMillion { get => _cachedPrice; set => SetProperty(ref _cachedPrice, value); }
    /// <summary>
    /// Gets or updates reasoning price per million, the bindable or domain state represented by this property.
    /// </summary>
    public string ReasoningPricePerMillion { get => _reasoningPrice; set => SetProperty(ref _reasoningPrice, value); }
    /// <summary>
    /// Gets or updates pricing currency, the bindable or domain state represented by this property.
    /// </summary>
    public string PricingCurrency { get => _currency; set => SetProperty(ref _currency, value); }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    /// <summary>
    /// Reports whether busy applies to the current state.
    /// </summary>
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    /// <summary>
    /// Performs the read step owned by this component.
    /// </summary>
    private static string Read(ProviderConfiguration configuration, string key) => configuration.Metadata.TryGetValue(key, out var value) ? value : string.Empty;
}
