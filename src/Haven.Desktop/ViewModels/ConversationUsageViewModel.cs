using System.Globalization;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class ConversationUsageViewModel : ObservableObject
{
    private readonly IModelUsageRepository _usage;
    private Guid _conversationId;
    private string _tokenSummary = "No recorded responses yet.";
    private string _measurementSummary = "Usage will be labelled as provider-confirmed or estimated.";
    private string _costSummary = "No configured cost data.";
    private string _status = string.Empty;
    private bool _isBusy;

    public ConversationUsageViewModel(IModelUsageRepository usage)
    {
        _usage = usage;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public string TokenSummary { get => _tokenSummary; private set => SetProperty(ref _tokenSummary, value); }
    public string MeasurementSummary { get => _measurementSummary; private set => SetProperty(ref _measurementSummary, value); }
    public string CostSummary { get => _costSummary; private set => SetProperty(ref _costSummary, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    public async Task LoadAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        _conversationId = conversationId;
        await RefreshAsync(cancellationToken);
    }

    private Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_conversationId == Guid.Empty) return;
        try
        {
            IsBusy = true;
            var summary = await _usage.GetSummaryAsync(_conversationId, cancellationToken);
            TokenSummary = summary.Responses == 0
                ? "No recorded responses yet."
                : $"{summary.InputTokens:N0} input · {summary.OutputTokens:N0} output · {summary.CachedTokens:N0} cached · {summary.ReasoningTokens:N0} reasoning";
            MeasurementSummary = summary.Measurements.Count == 0
                ? "No usage measurements yet."
                : "Measurement: " + string.Join(", ", summary.Measurements.OrderBy(item => item).Select(MeasurementLabel));
            CostSummary = summary.Cost is null
                ? "Cost unavailable — no provider pricing is configured, the model is local, or multiple currencies are present."
                : $"Configured cost: {summary.Cost.Value.ToString("0.########", CultureInfo.InvariantCulture)} {summary.Currency}";
            Status = summary.Responses == 1 ? "1 response recorded." : $"{summary.Responses} responses recorded.";
        }
        catch (Exception ex)
        {
            Status = "Usage summary unavailable: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string MeasurementLabel(UsageMeasurementKind measurement) => measurement switch
    {
        UsageMeasurementKind.ProviderConfirmed => "provider-confirmed",
        UsageMeasurementKind.LocallyCalculated => "locally calculated",
        _ => "estimated"
    };
}
