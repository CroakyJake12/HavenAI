using Haven.Application;
using Haven.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.Settings;

public sealed partial class SettingsHavenPage
{
    private IModelFallbackOrderStore? _fallbackOrders;
    private IModelPersonalisationStore? _personalisation;
    private IModelPermissionStore? _modelPermissions;
    private IDefaultProviderStore? _defaultProviders;
    private ModelPermissionPolicy _governancePolicy = ModelPermissionPolicy.Empty;
    private bool _loadingGovernance;

    private void InitializeGovernance()
    {
        _fallbackOrders = App.Services?.GetService<IModelFallbackOrderStore>();
        _personalisation = App.Services?.GetService<IModelPersonalisationStore>();
        _modelPermissions = App.Services?.GetService<IModelPermissionStore>();
        _defaultProviders = App.Services?.GetService<IDefaultProviderStore>();

        _route.SaveFallbackOrderButton.Invoked += async (_, _) => await SaveFallbackOrderAsync();
        _route.SaveSharedPersonalityButton.Invoked += async (_, _) => await SaveSharedPersonalityAsync();
        _route.SavePersonalisationEntryButton.Invoked += async (_, _) => await SavePersonalisationEntryAsync();
        _route.RemovePersonalisationEntryButton.Invoked += async (_, _) => await RemovePersonalisationEntryAsync();
        _route.AddPermissionRuleButton.Invoked += async (_, _) => await AddPermissionRuleAsync();
        _route.RemovePermissionRuleButton.Invoked += async (_, _) => await RemoveSelectedPermissionRuleAsync();
        foreach (var (category, select) in _route.DefaultProviderSelects)
        {
            var captured = category;
            select.SelectionChanged += async (_, _) => await SaveDefaultProviderAsync(captured);
        }
        _ = RefreshGovernanceAsync();
    }

    private async Task RefreshGovernanceAsync()
    {
        if (_disposed) return;
        var unavailable = new List<string>();
        if (_fallbackOrders is null) unavailable.Add("priority order");
        if (_personalisation is null) unavailable.Add("personality");
        if (_modelPermissions is null) unavailable.Add("permissions");
        if (_defaultProviders is null) unavailable.Add("Default Apps");
        if (unavailable.Count == 4)
        {
            _route.GovernanceStatus.Content = "Model governance services are unavailable in this build.";
            return;
        }

        _loadingGovernance = true;
        try
        {
            if (_fallbackOrders is not null)
            {
                var order = await _fallbackOrders.GetOrderAsync(_lifetime.Token);
                if (!_disposed) _route.SetFallbackOrder(order);
            }
            if (_personalisation is not null)
            {
                var shared = await _personalisation.GetSharedDefaultsAsync(_lifetime.Token);
                if (!_disposed) _route.SetSharedPersonality(shared);
                var entries = await _personalisation.GetEntriesAsync(_lifetime.Token);
                if (!_disposed) _route.SetPersonalisationEntries(entries);
            }
            if (_modelPermissions is not null)
            {
                _governancePolicy = await _modelPermissions.GetPolicyAsync(_lifetime.Token);
                if (!_disposed) RenderPermissionRules();
            }
            if (_defaultProviders is not null)
            {
                var assignments = await _defaultProviders.GetAllAsync(_lifetime.Token);
                foreach (var category in Enum.GetValues<ProviderCategory>())
                {
                    assignments.TryGetValue(DefaultCategoryCatalog.Key(category), out var assignment);
                    if (!_disposed) _route.SetDefaultProviderAssignment(category, assignment);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed) _route.GovernanceStatus.Content = $"Could not load model governance settings: {ex.Message}";
        }
        finally
        {
            _loadingGovernance = false;
        }
    }

    private async Task SaveFallbackOrderAsync()
    {
        if (_disposed || _fallbackOrders is null) return;
        var lines = _route.FallbackOrderInput.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        try
        {
            await _fallbackOrders.SetOrderAsync(lines, _lifetime.Token);
            if (_disposed) return;
            _route.SetStatus(lines.Count == 0
                ? "Saved an empty priority order; Haven will choose models automatically."
                : $"Saved model priority order with {lines.Count} entr{(lines.Count == 1 ? "y" : "ies")}.");
            _bus.Fire("Settings.Models.FallbackOrderSaved");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed) _route.SetStatus($"Could not save the priority order: {ex.Message}");
        }
    }

    private async Task SaveSharedPersonalityAsync()
    {
        if (_disposed || _personalisation is null) return;
        try
        {
            await _personalisation.SetSharedDefaultsAsync(_route.ReadSharedPersonality(), _lifetime.Token);
            if (_disposed) return;
            _route.SetStatus("Saved shared model personality.");
            _bus.Fire("Settings.Models.PersonalitySaved");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed) _route.SetStatus($"Could not save the shared personality: {ex.Message}");
        }
    }

    private async Task SavePersonalisationEntryAsync()
    {
        if (_disposed || _personalisation is null) return;
        var key = _route.OverrideModelKeyInput.Text.Trim();
        if (key.Length == 0)
        {
            _route.SetStatus("Enter a model key to personalise first.");
            return;
        }
        try
        {
            var shared = await _personalisation.GetSharedDefaultsAsync(_lifetime.Token);
            var personality = _route.ReadOverridePersonality(shared);
            var nickname = _route.OverrideNicknameInput.Text.Trim();
            await _personalisation.SaveEntryAsync(new ModelPersonalisationEntry(
                key,
                nickname.Length == 0 ? null : nickname,
                personality), _lifetime.Token);
            if (_disposed) return;
            await ReloadPersonalisationEntriesAsync();
            _route.SetStatus($"Saved personalisation for {key}.");
            _bus.Fire("Settings.Models.PersonalisationSaved");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed) _route.SetStatus($"Could not save the personalisation: {ex.Message}");
        }
    }

    private async Task RemovePersonalisationEntryAsync()
    {
        if (_disposed || _personalisation is null) return;
        var key = _route.OverrideModelKeyInput.Text.Trim();
        if (key.Length == 0)
        {
            _route.SetStatus("Enter the model key whose override should be removed.");
            return;
        }
        try
        {
            await _personalisation.RemoveEntryAsync(key, _lifetime.Token);
            if (_disposed) return;
            await ReloadPersonalisationEntriesAsync();
            _route.SetStatus($"Removed personalisation for {key}.");
            _bus.Fire("Settings.Models.PersonalisationRemoved");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed) _route.SetStatus($"Could not remove the personalisation: {ex.Message}");
        }
    }

    private async Task ReloadPersonalisationEntriesAsync()
    {
        if (_personalisation is null) return;
        var entries = await _personalisation.GetEntriesAsync(_lifetime.Token);
        if (!_disposed) _route.SetPersonalisationEntries(entries);
    }

    private void RenderPermissionRules()
        => _route.SetPermissionRules(_governancePolicy.Rules
            .Select(SettingsHavenScene.DescribePermissionRule)
            .ToArray());

    private async Task AddPermissionRuleAsync()
    {
        if (_disposed || _modelPermissions is null) return;
        var target = _route.ReadPermissionTarget();
        if (target is null) return;
        var kind = target.Value;
        var match = _route.PermissionMatchInput.Text.Trim();
        double? maxBillions = null;
        if (kind == ModelPermissionTargetKind.ParameterSizeBelow)
        {
            if (!_route.TryReadMaxParameterBillions(out var parsed))
            {
                _route.SetStatus("Enter the maximum parameter size in billions, for example 27.");
                return;
            }
            maxBillions = parsed;
        }
        else if (kind is ModelPermissionTargetKind.ExactModel
                 or ModelPermissionTargetKind.ModelFamily
                 or ModelPermissionTargetKind.Provider)
        {
            if (match.Length == 0)
            {
                _route.SetStatus("Enter the text this rule should match.");
                return;
            }
        }
        var scope = _route.ReadPermissionScope() ?? ModelPermissionScope.ThisDevice;
        var denied = _route.ReadDeniedCapabilities();
        if (denied.Count == 0)
        {
            _route.SetStatus("Choose at least one capability to deny.");
            return;
        }

        var rule = new ModelPermissionRule(
            Guid.NewGuid(), kind, match, maxBillions, scope, denied.ToHashSet());
        _governancePolicy = new ModelPermissionPolicy([.. _governancePolicy.Rules, rule]);
        try
        {
            await _modelPermissions.SavePolicyAsync(_governancePolicy, _lifetime.Token);
            if (_disposed) return;
            RenderPermissionRules();
            _route.ResetPermissionDraft();
            _route.SetStatus("Saved model permission rule.");
            _bus.Fire("Settings.Models.PermissionRuleAdded");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed) _route.SetStatus($"Could not save the permission rule: {ex.Message}");
        }
    }

    private async Task RemoveSelectedPermissionRuleAsync()
    {
        if (_disposed || _modelPermissions is null) return;
        var index = _route.PermissionRulesSelect.SelectedIndex;
        if (index < 0 || index >= _governancePolicy.Rules.Count)
        {
            _route.SetStatus("Choose a saved rule to remove.");
            return;
        }
        _governancePolicy = new ModelPermissionPolicy(
            _governancePolicy.Rules.Where((_, position) => position != index).ToList());
        try
        {
            await _modelPermissions.SavePolicyAsync(_governancePolicy, _lifetime.Token);
            if (_disposed) return;
            RenderPermissionRules();
            _route.SetStatus("Removed model permission rule.");
            _bus.Fire("Settings.Models.PermissionRuleRemoved");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed) _route.SetStatus($"Could not remove the permission rule: {ex.Message}");
        }
    }

    private async Task SaveDefaultProviderAsync(ProviderCategory category)
    {
        if (_loadingGovernance || _disposed || _defaultProviders is null) return;
        if (!_route.DefaultProviderSelects.TryGetValue(category, out var select) || select.SelectedIndex < 0) return;
        var assignment = select.SelectedIndex == 0
            ? DefaultProviderAssignments.AlwaysAsk
            : DefaultCategoryCatalog.ProvidersFor(category)[select.SelectedIndex - 1];
        try
        {
            await _defaultProviders.SetAsync(DefaultCategoryCatalog.Key(category), assignment, _lifetime.Token);
            if (_disposed) return;
            _route.SetStatus($"Saved default provider for {ProviderCategoryNames.For(category)}.");
            _bus.Fire("Settings.Models.DefaultProviderSaved");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed) _route.SetStatus($"Could not save the default provider: {ex.Message}");
        }
    }
}
