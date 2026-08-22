namespace Haven.Core;

public enum GenUiValueType { String, Integer, Number, Boolean, DateTime, Object, Array }
public enum GenUiPersistenceScope { Transient, Instance, Thread, App, User }
public enum GenUiBindingMode { OneWay, TwoWay, Computed }
public enum GenUiNavigationKind { Root, Push, Tab, Modal, Sheet, WizardStep, Detail }
public sealed record GenUiStateFieldDefinition(string Key, GenUiValueType Type, GenUiPersistenceScope Persistence, bool Required, object? DefaultValue = null);
public sealed record GenUiDerivedStateDefinition(string Key, GenUiValueType Type, string Expression, IReadOnlyList<string> Dependencies);
public sealed record GenUiBindingDefinition(string ComponentId, string Property, string StateKey, GenUiBindingMode Mode);
public sealed record GenUiNavigationRoute(string RouteId, string PageComponentId, GenUiNavigationKind Kind, string? ParentRouteId, string? GroupKey, bool IsStartRoute);
public sealed record GenUiAppDefinition(string AppId, int SchemaVersion, GenUiDocument Document, IReadOnlyList<GenUiStateFieldDefinition> StateSchema, IReadOnlyList<GenUiDerivedStateDefinition> DerivedState, IReadOnlyList<GenUiBindingDefinition> Bindings, IReadOnlyList<GenUiNavigationRoute> Routes, string RuntimeVersion);
public sealed record GenUiSemanticValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Repairs, GenUiAppDefinition Definition) { public bool IsValid => Errors.Count == 0; }