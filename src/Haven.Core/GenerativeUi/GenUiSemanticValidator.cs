namespace Haven.Core;
public static class GenUiSemanticValidator
{
 public const int CurrentSchemaVersion = 1;
 public static GenUiSemanticValidationResult ValidateAndRepair(GenUiAppDefinition app)
 {
  var errors=GenerativeUiContractValidator.Validate(app.Document).ToList();
  var repairs=new List<string>();
  var state=app.StateSchema.Select(x=>x.Key).ToHashSet(StringComparer.Ordinal);
  if(state.Count!=app.StateSchema.Count||state.Contains("")) errors.Add("State field keys must be unique and non-empty.");
  var derived=app.DerivedState.Select(x=>x.Key).ToHashSet(StringComparer.Ordinal);
  if(derived.Count!=app.DerivedState.Count||derived.Overlaps(state)) errors.Add("Derived state keys must be unique.");
  foreach(var item in app.DerivedState)
  {
   if(string.IsNullOrWhiteSpace(item.Expression)) errors.Add($"Derived state '{item.Key}' requires an expression.");
   foreach(var dependency in item.Dependencies) if(!state.Contains(dependency)&&!derived.Contains(dependency)) errors.Add($"Derived state '{item.Key}' references unknown dependency '{dependency}'.");
  }
  var components=Flatten(app.Document.Root).Select(x=>x.ComponentId).ToHashSet(StringComparer.Ordinal);
  foreach(var binding in app.Bindings)
  {
   if(!components.Contains(binding.ComponentId)) errors.Add($"Binding references missing component '{binding.ComponentId}'.");
   if(!state.Contains(binding.StateKey)&&!derived.Contains(binding.StateKey)) errors.Add($"Binding references unknown state '{binding.StateKey}'.");
   if(binding.Mode==GenUiBindingMode.TwoWay&&derived.Contains(binding.StateKey)) errors.Add($"Derived state '{binding.StateKey}' cannot be two-way bound.");
  }
  var resultSchemaIds=app.ResultSchemas.Select(x=>x.SchemaId).ToHashSet(StringComparer.Ordinal);
  if(resultSchemaIds.Count!=app.ResultSchemas.Count||resultSchemaIds.Contains("")) errors.Add("Result schema IDs must be unique and non-empty.");
  var errorSchemaIds=app.ErrorSchemas.Select(x=>x.ErrorId).ToHashSet(StringComparer.Ordinal);
  if(errorSchemaIds.Count!=app.ErrorSchemas.Count||errorSchemaIds.Contains("")) errors.Add("Error schema IDs must be unique and non-empty.");
  foreach(var schema in app.ResultSchemas) if(schema.Fields.Select(x=>x.Key).Distinct(StringComparer.Ordinal).Count()!=schema.Fields.Count) errors.Add($"Result schema '{schema.SchemaId}' has duplicate field keys.");
  foreach(var schema in app.ErrorSchemas) if(schema.Details.Select(x=>x.Key).Distinct(StringComparer.Ordinal).Count()!=schema.Details.Count) errors.Add($"Error schema '{schema.ErrorId}' has duplicate detail keys.");
  var actionIds=app.Actions.Select(x=>x.ActionId).ToHashSet(StringComparer.Ordinal);
  if(actionIds.Count!=app.Actions.Count||actionIds.Contains("")) errors.Add("Action IDs must be unique and non-empty.");
  foreach(var action in app.Actions)
  {
   if(action.Inputs.Select(x=>x.Key).Distinct(StringComparer.Ordinal).Count()!=action.Inputs.Count) errors.Add($"Action '{action.ActionId}' has duplicate input keys.");
   if(action.ResultSchemaId is { } resultId&&!resultSchemaIds.Contains(resultId)) errors.Add($"Action '{action.ActionId}' references unknown result schema '{resultId}'.");
   foreach(var errorId in action.ErrorSchemaIds) if(!errorSchemaIds.Contains(errorId)) errors.Add($"Action '{action.ActionId}' references unknown error schema '{errorId}'.");
  }
  if(app.Rendering.AllowsExecutableCode) errors.Add("Generated executable code is not permitted by the current trusted GenUI runtime.");
  if(app.Rendering.Layer==GenUiRenderingLayer.GeneratedSandbox && app.Document.Origin.TemplateId is not null) errors.Add("GeneratedSandbox rendering is reserved for purpose-built generated definitions without a trusted template ID.");
  var routes=app.Routes.ToList();
  var routeIds=routes.Select(x=>x.RouteId).ToHashSet(StringComparer.Ordinal);
  if(routeIds.Count!=routes.Count) errors.Add("Navigation route IDs must be unique.");
  foreach(var route in routes) if(!components.Contains(route.PageComponentId)) errors.Add($"Route '{route.RouteId}' references missing page component '{route.PageComponentId}'.");
  if(routes.Count>0)
  {
   var starts=routes.Where(x=>x.IsStartRoute).ToArray();
   if(starts.Length==0)
   {
    var i=routes.FindIndex(x=>x.Kind==GenUiNavigationKind.Root); if(i<0)i=0; routes[i]=routes[i] with { IsStartRoute=true }; repairs.Add($"Marked route '{routes[i].RouteId}' as the start route.");
   }
   else if(starts.Length>1) errors.Add("Navigation requires exactly one start route.");
   for(var i=0;i<routes.Count;i++) if(routes[i].ParentRouteId is { } parent&&!routeIds.Contains(parent)) { routes[i]=routes[i] with { ParentRouteId=null }; repairs.Add($"Removed missing parent route '{parent}' from '{routes[i].RouteId}'."); }
   var start=routes.SingleOrDefault(x=>x.IsStartRoute)?.RouteId;
   if(start is not null)
   {
    var reachable=new HashSet<string>(StringComparer.Ordinal){start};
    for(var changed=true;changed;)
    {
     changed=false;
     foreach(var route in routes) if(!reachable.Contains(route.RouteId)&&route.ParentRouteId is { } p&&reachable.Contains(p)) { reachable.Add(route.RouteId); changed=true; }
    }
    foreach(var route in routes.Where(x=>!reachable.Contains(x.RouteId))) errors.Add($"Route '{route.RouteId}' is unreachable from start route '{start}'.");
   }
  }
  return new(errors,repairs,app with { Routes=routes });
 }
 private static IEnumerable<GenUiComponent> Flatten(GenUiComponent root)
 {
  yield return root; foreach(var child in root.Children) foreach(var item in Flatten(child)) yield return item;
 }
}