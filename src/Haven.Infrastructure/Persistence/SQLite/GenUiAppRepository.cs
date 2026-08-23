using System.Text.Json;
using Haven.Application;
using Haven.Core;
namespace Haven.Infrastructure;
public sealed class GenUiAppRepository(ISqliteConnectionFactory factory):IGenUiAppRepository
{
 public async Task UpsertAsync(GenUiAppDefinition definition,CancellationToken ct)
 {
  var checkedApp=GenUiSemanticValidator.ValidateAndRepair(definition);
  if(!checkedApp.IsValid) throw new InvalidOperationException(string.Join(" ",checkedApp.Errors));
  var app=checkedApp.Definition; await using var c=await factory.OpenAsync(ct); await using var cmd=c.CreateCommand();
  cmd.CommandText="INSERT INTO genui_apps(instance_id,app_id,thread_id,definition_json,updated_at) VALUES($instance,$app,$thread,$json,$updated) ON CONFLICT(instance_id) DO UPDATE SET app_id=excluded.app_id,thread_id=excluded.thread_id,definition_json=excluded.definition_json,updated_at=excluded.updated_at;";
  cmd.Parameters.AddWithValue("$instance",app.Document.Origin.InstanceId.ToString()); cmd.Parameters.AddWithValue("$app",app.AppId); cmd.Parameters.AddWithValue("$thread",app.Document.Origin.ThreadId.ToString()); cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(app)); cmd.Parameters.AddWithValue("$updated",app.Document.UpdatedAt.ToString("O")); await cmd.ExecuteNonQueryAsync(ct);
 }
 public async Task<GenUiAppDefinition?> GetAsync(Guid instanceId,CancellationToken ct)
 {
  await using var c=await factory.OpenAsync(ct); await using var cmd=c.CreateCommand(); cmd.CommandText="SELECT definition_json FROM genui_apps WHERE instance_id=$id LIMIT 1;"; cmd.Parameters.AddWithValue("$id",instanceId.ToString()); var value=await cmd.ExecuteScalarAsync(ct); return value is string json?Read(json):null;
 }
 public async Task<IReadOnlyList<GenUiAppDefinition>> GetRecentAsync(int limit,CancellationToken ct)
 {
  await using var c=await factory.OpenAsync(ct); await using var cmd=c.CreateCommand(); cmd.CommandText="SELECT definition_json FROM genui_apps ORDER BY updated_at DESC LIMIT $limit;"; cmd.Parameters.AddWithValue("$limit",Math.Clamp(limit,1,100)); var result=new List<GenUiAppDefinition>(); await using var r=await cmd.ExecuteReaderAsync(ct); while(await r.ReadAsync(ct)) result.Add(Read(r.GetString(0))); return result;
 }
 public async Task<IReadOnlyList<GenUiAppDefinition>> GetPinnedAsync(int limit,CancellationToken ct)
 {
  await using var c=await factory.OpenAsync(ct); await using var cmd=c.CreateCommand(); cmd.CommandText="SELECT definition_json FROM genui_apps WHERE is_pinned=1 ORDER BY updated_at DESC LIMIT $limit;"; cmd.Parameters.AddWithValue("$limit",Math.Clamp(limit,1,100)); var result=new List<GenUiAppDefinition>(); await using var r=await cmd.ExecuteReaderAsync(ct); while(await r.ReadAsync(ct)) result.Add(Read(r.GetString(0))); return result;
 }
 public async Task SetPinnedAsync(Guid instanceId,bool pinned,CancellationToken ct)
 {
  await using var c=await factory.OpenAsync(ct); await using var cmd=c.CreateCommand(); cmd.CommandText="UPDATE genui_apps SET is_pinned=$pinned WHERE instance_id=$id;"; cmd.Parameters.AddWithValue("$pinned",pinned?1:0); cmd.Parameters.AddWithValue("$id",instanceId.ToString()); await cmd.ExecuteNonQueryAsync(ct);
 }
 public async Task DeleteAsync(Guid instanceId,CancellationToken ct)
 {
  await using var c=await factory.OpenAsync(ct); await using var cmd=c.CreateCommand(); cmd.CommandText="DELETE FROM genui_apps WHERE instance_id=$id;"; cmd.Parameters.AddWithValue("$id",instanceId.ToString()); await cmd.ExecuteNonQueryAsync(ct);
 }
 private static GenUiAppDefinition Read(string json)
 {
  var app=JsonSerializer.Deserialize<GenUiAppDefinition>(json)??throw new InvalidDataException("Stored GenUI app is invalid."); var checkedApp=GenUiSemanticValidator.ValidateAndRepair(app); if(!checkedApp.IsValid) throw new InvalidDataException(string.Join(" ",checkedApp.Errors)); return checkedApp.Definition;
 }
}