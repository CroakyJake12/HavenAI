/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/ConversationUsageViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns ConversationUsageViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Globalization;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents conversation usage view model and keeps its related state and behavior together.
/// </summary>
public sealed class ConversationUsageViewModel : ObservableObject
{
    /// <summary>
    /// Stores usage locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IModelUsageRepository _usage;
    /// <summary>
    /// Stores conversation id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Guid _conversationId;
    /// <summary>
    /// Stores token summary locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _tokenSummary = "No recorded responses yet.";
    /// <summary>
    /// Stores measurement summary locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _measurementSummary = "Usage will be labelled as provider-confirmed or estimated.";
    /// <summary>
    /// Stores cost summary locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _costSummary = "No configured cost data.";
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = string.Empty;
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isBusy;

    public ConversationUsageViewModel(IModelUsageRepository usage)
    {
        _usage = usage;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>
    /// Gets or updates token summary, the bindable or domain state represented by this property.
    /// </summary>
    public string TokenSummary { get => _tokenSummary; private set => SetProperty(ref _tokenSummary, value); }
    /// <summary>
    /// Gets or updates measurement summary, the bindable or domain state represented by this property.
    /// </summary>
    public string MeasurementSummary { get => _measurementSummary; private set => SetProperty(ref _measurementSummary, value); }
    /// <summary>
    /// Gets or updates cost summary, the bindable or domain state represented by this property.
    /// </summary>
    public string CostSummary { get => _costSummary; private set => SetProperty(ref _costSummary, value); }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Reports whether busy applies to the current state.
    /// </summary>
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    /// <summary>
    /// Performs load asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task LoadAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        _conversationId = conversationId;
        await RefreshAsync(cancellationToken);
    }

    /// <summary>
    /// Performs refresh asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    /// <summary>
    /// Performs refresh asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the measurement label step owned by this component.
    /// </summary>
    private static string MeasurementLabel(UsageMeasurementKind measurement) => measurement switch
    {
        UsageMeasurementKind.ProviderConfirmed => "provider-confirmed",
        UsageMeasurementKind.LocallyCalculated => "locally calculated",
        _ => "estimated"
    };
}
