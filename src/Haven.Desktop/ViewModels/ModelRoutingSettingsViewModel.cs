using System.Collections.ObjectModel;
using System.Globalization;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class ModelRoutingSettingsViewModel : ObservableObject
{
    private readonly IProviderConfigurationStore _configurations;
    private string _status = "Loading routing policies…";
    private bool _isBusy;

    public ModelRoutingSettingsViewModel(IProviderConfigurationStore configurations)
    {
        _configurations = configurations;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        SaveCommand = new AsyncRelayCommand<ProviderRoutingSettingsItemViewModel>(SaveAsync);
        _ = RefreshAsync();
    }

    public ObservableCollection<ProviderRoutingSettingsItemViewModel> Items { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand<ProviderRoutingSettingsItemViewModel> SaveCommand { get; }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

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

    private static void SetOrRemove(IDictionary<string, string> metadata, string key, string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) metadata.Remove(key);
        else metadata[key] = trimmed;
    }
}

public sealed class ProviderRoutingSettingsItemViewModel : ObservableObject
{
    private ProviderConfiguration _configuration;
    private bool _allowCloudFallback;
    private string _fallbackChain;
    private string _inputPrice;
    private string _outputPrice;
    private string _cachedPrice;
    private string _reasoningPrice;
    private string _currency;
    private string _status;
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

    public ProviderConfiguration Configuration { get => _configuration; set => SetProperty(ref _configuration, value); }
    public string Id => Configuration.Id;
    public string DisplayName => Configuration.DisplayName;
    public string ProviderType => Configuration.Kind.ToString();
    public bool IsLocal => Configuration.IsLocal;
    public bool AllowCloudFallback { get => _allowCloudFallback; set => SetProperty(ref _allowCloudFallback, value); }
    public string FallbackChain { get => _fallbackChain; set => SetProperty(ref _fallbackChain, value); }
    public string InputPricePerMillion { get => _inputPrice; set => SetProperty(ref _inputPrice, value); }
    public string OutputPricePerMillion { get => _outputPrice; set => SetProperty(ref _outputPrice, value); }
    public string CachedPricePerMillion { get => _cachedPrice; set => SetProperty(ref _cachedPrice, value); }
    public string ReasoningPricePerMillion { get => _reasoningPrice; set => SetProperty(ref _reasoningPrice, value); }
    public string PricingCurrency { get => _currency; set => SetProperty(ref _currency, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    private static string Read(ProviderConfiguration configuration, string key) => configuration.Metadata.TryGetValue(key, out var value) ? value : string.Empty;
}
