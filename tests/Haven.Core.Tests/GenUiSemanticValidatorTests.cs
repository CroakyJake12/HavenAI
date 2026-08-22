using System.Text.Json;
using Haven.Core;
namespace Haven.Core.Tests;
public sealed class GenUiSemanticValidatorTests
{
 [Fact] public void RepairsMissingStartAndMissingParent()
 {
  var app=App() with { Routes=[new("home","root",GenUiNavigationKind.Root,null,null,false),new("details","details",GenUiNavigationKind.Detail,"missing",null,false)] };
  var result=GenUiSemanticValidator.ValidateAndRepair(app);
  Assert.True(result.Definition.Routes[0].IsStartRoute);
  Assert.Null(result.Definition.Routes[1].ParentRouteId);
  Assert.Equal(2,result.Repairs.Count);
  Assert.Contains(result.Errors,e=>e.Contains("unreachable",StringComparison.Ordinal));
 }
 [Fact] public void RejectsDeadBindingAndTwoWayDerivedState()
 {
  var app=App() with { DerivedState=[new("total",GenUiValueType.Number,"price * quantity",["price","quantity"])], Bindings=[new("missing","text","price",GenUiBindingMode.OneWay),new("root","text","total",GenUiBindingMode.TwoWay)] };
  var result=GenUiSemanticValidator.ValidateAndRepair(app);
  Assert.Contains(result.Errors,e=>e.Contains("missing component",StringComparison.Ordinal));
  Assert.Contains(result.Errors,e=>e.Contains("cannot be two-way bound",StringComparison.Ordinal));
 }
 [Fact] public void AcceptsTypedReachableDefinition()
 {
  var result=GenUiSemanticValidator.ValidateAndRepair(App());
  Assert.True(result.IsValid,string.Join(Environment.NewLine,result.Errors));
  Assert.Empty(result.Repairs);
 }
 private static GenUiAppDefinition App()
 {
  var origin=new GenUiOrigin(Guid.NewGuid(),"genui",null,Guid.NewGuid());
  var details=new GenUiComponent("details","HavenStack",Empty(),[],[]);
  var root=new GenUiComponent("root","HavenWorkspace",Empty(),[],[details]);
  var doc=new GenUiDocument(Guid.NewGuid(),GenerativeUiContractValidator.CurrentContractVersion,origin,"Test","genui",root,new Dictionary<string,JsonElement>{{"price",JsonSerializer.SerializeToElement(0d)},{"quantity",JsonSerializer.SerializeToElement(1)}},DateTimeOffset.UtcNow);
  return new("meal-planner",1,doc,[new("price",GenUiValueType.Number,GenUiPersistenceScope.Instance,true,0d),new("quantity",GenUiValueType.Integer,GenUiPersistenceScope.Instance,true,1)],[],[new("root","text","price",GenUiBindingMode.OneWay)],[new("home","root",GenUiNavigationKind.Root,null,null,true)],"haven-genui-runtime/1");
 }
 private static IReadOnlyDictionary<string,JsonElement> Empty()=>new Dictionary<string,JsonElement>();
}