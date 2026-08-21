using System.Text.Json;
using Haven.Application;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class SpaceEditPlannerTests
{
    [Fact]
    public void Valid_plan_returns_only_reviewable_draft_fields()
    {
        var response = JsonSerializer.Serialize(new
        {
            name = "Exam Research",
            description = "Keep sources together",
            modelName = "qwen",
            instructions = "Separate facts from inference.",
            thinkingMode = "deep",
            surfaceTemplate = "checklist",
            surfaceInputs = new { title = "Evidence" }
        });

        var result = SpaceEditPlanner.ParseAndValidate(response);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Patch);
        Assert.Equal("Exam Research", result.Patch!.Name);
        Assert.Equal(SpaceThinkingMode.Deep, result.Patch.ThinkingMode);
        Assert.Equal("checklist", result.Patch.SurfaceTemplate);
        Assert.Contains("Evidence", result.Patch.SurfaceInputsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_fields_are_rejected_instead_of_ignored()
    {
        var response = JsonSerializer.Serialize(new { name = "Safe", files = new[] { "C:/secret.txt" } });
        var result = SpaceEditPlanner.ParseAndValidate(response);

        Assert.False(result.Succeeded);
        Assert.Contains("malformed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unsupported_thinking_mode_is_rejected()
    {
        var result = SpaceEditPlanner.ParseAndValidate(JsonSerializer.Serialize(new { thinkingMode = "unbounded" }));

        Assert.False(result.Succeeded);
        Assert.Contains("thinking mode", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unsupported_surface_template_is_rejected()
    {
        var result = SpaceEditPlanner.ParseAndValidate(JsonSerializer.Serialize(new { surfaceTemplate = "arbitrary-code" }));

        Assert.False(result.Succeeded);
        Assert.Contains("surface", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Surface_inputs_must_be_a_json_object()
    {
        var result = SpaceEditPlanner.ParseAndValidate(JsonSerializer.Serialize(new { surfaceInputs = new[] { 1, 2, 3 } }));

        Assert.False(result.Succeeded);
        Assert.Contains("JSON object", result.Message, StringComparison.Ordinal);
    }
}
