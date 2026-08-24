namespace Haven.Core;

public enum GenUiValueType { String, Integer, Number, Boolean, DateTime, Object, Array }
public enum GenUiPersistenceScope { Transient, Instance, Thread, App, User }
public enum GenUiBindingMode { OneWay, TwoWay, Computed }
public enum GenUiNavigationKind { Root, Push, Tab, Modal, Sheet, WizardStep, Detail }
public enum GenUiRenderingLayer { Native, Composite, Scene, GeneratedSandbox }
public sealed record GenUiRenderingDecision(GenUiRenderingLayer Layer, string Reason, bool AllowsExecutableCode = false);
public enum GenUiActionExecutionKind { Local, App, Capability, External }
public sealed record GenUiStateFieldDefinition(string Key, GenUiValueType Type, GenUiPersistenceScope Persistence, bool Required, object? DefaultValue = null);
public sealed record GenUiDerivedStateDefinition(string Key, GenUiValueType Type, string Expression, IReadOnlyList<string> Dependencies);
public sealed record GenUiBindingDefinition(string ComponentId, string Property, string StateKey, GenUiBindingMode Mode);
public sealed record GenUiNavigationRoute(string RouteId, string PageComponentId, GenUiNavigationKind Kind, string? ParentRouteId, string? GroupKey, bool IsStartRoute);
public sealed record GenUiSchemaField(string Key, GenUiValueType Type, bool Required);
public sealed record GenUiActionDefinition(string ActionId, GenUiActionExecutionKind ExecutionKind, IReadOnlyList<GenUiSchemaField> Inputs, string? ResultSchemaId, IReadOnlyList<string> ErrorSchemaIds);
public sealed record GenUiResultSchemaDefinition(string SchemaId, IReadOnlyList<GenUiSchemaField> Fields);
public sealed record GenUiErrorSchemaDefinition(string ErrorId, string Code, string Message, bool Recoverable, IReadOnlyList<GenUiSchemaField> Details);
public sealed record GenUiAppDefinition(string AppId, int SchemaVersion, GenUiDocument Document, IReadOnlyList<GenUiStateFieldDefinition> StateSchema, IReadOnlyList<GenUiDerivedStateDefinition> DerivedState, IReadOnlyList<GenUiBindingDefinition> Bindings, IReadOnlyList<GenUiNavigationRoute> Routes, string RuntimeVersion)
{
    public IReadOnlyList<GenUiActionDefinition> Actions { get; init; } = [];
    public IReadOnlyList<GenUiResultSchemaDefinition> ResultSchemas { get; init; } = [];
    public IReadOnlyList<GenUiErrorSchemaDefinition> ErrorSchemas { get; init; } = [];
    public GenUiRenderingDecision Rendering { get; init; } = new(GenUiRenderingLayer.Native, "Default trusted native rendering.");
}
public sealed record GenUiSemanticValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Repairs, GenUiAppDefinition Definition) { public bool IsValid => Errors.Count == 0; }