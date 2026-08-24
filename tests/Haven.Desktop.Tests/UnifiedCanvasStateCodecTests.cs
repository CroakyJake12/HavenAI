using System.Text.Json;
using Haven.Desktop.HavenUI.Creative;

namespace Haven.Desktop.Tests;

public sealed class UnifiedCanvasStateCodecTests
{
    [Fact]
    public void Legacy_v2_whiteboard_state_migrates_and_round_trips_through_canonical_canvas_state()
    {
        var textId = Guid.NewGuid();
        var legacy = JsonSerializer.SerializeToElement(new
        {
            Version = 2,
            Tool = "Rectangle",
            Color = "#E53935",
            Thickness = 9d,
            Zoom = 1.5,
            OffsetX = 24d,
            OffsetY = -12d,
            ShowGrid = true,
            Elements = new object[]
            {
                new
                {
                    Id = textId.ToString("N"), Kind = "Text", Color = "#1E88E5", Thickness = 3d, Opacity = 1d,
                    Effect = "Solid", IsEraser = false, AgentGenerated = false, Text = "Study plan",
                    Points = new[] { new { X = 90d, Y = 170d, Pressure = .5 }, new { X = 330d, Y = 240d, Pressure = .5 } }
                },
                new
                {
                    Id = Guid.NewGuid().ToString("N"), Kind = "Rectangle", Color = "#43A047", Thickness = 4d, Opacity = .8d,
                    Effect = "Glow", IsEraser = false, AgentGenerated = true, Text = "",
                    Points = new[] { new { X = 60d, Y = 70d, Pressure = .5 }, new { X = 180d, Y = 150d, Pressure = .5 } }
                },
                new
                {
                    Id = Guid.NewGuid().ToString("N"), Kind = "Stroke", Color = "#8E24AA", Thickness = 7d, Opacity = .34d,
                    Effect = "Solid", IsEraser = false, AgentGenerated = false, Text = "",
                    Points = new[] { new { X = 10d, Y = 10d, Pressure = .25 }, new { X = 40d, Y = 45d, Pressure = .9 } }
                }
            }
        });

        var restored = UnifiedCanvasStateCodec.Restore(legacy);

        Assert.Equal(UnifiedCanvasTool.Rectangle, restored.Tool);
        Assert.True(restored.ShowGrid);
        Assert.Equal(1.5, restored.Controller.Board.Zoom, 3);
        Assert.Equal(24, restored.Controller.Board.OffsetX, 3);
        Assert.Equal(-12, restored.Controller.Board.OffsetY, 3);
        Assert.Equal(2, restored.Controller.Board.Objects.Count);
        Assert.Single(restored.Controller.Board.Strokes);
        var text = restored.Controller.Board.Objects.Single(value => value.Id == textId);
        Assert.Equal("Study plan", text.Text);
        Assert.Contains("rectangle", restored.Controller.Board.Objects.Single(value => value.Id != textId).StyleJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("highlighter", restored.Controller.Board.Strokes[0].Tool);
        Assert.Equal(.9, restored.Controller.Board.Strokes[0].Points[1].Pressure, 3);

        var canonical = UnifiedCanvasStateCodec.ToJson(restored.Controller, restored.Tool, restored.ShowGrid);
        var roundTrip = UnifiedCanvasStateCodec.Restore(canonical);
        Assert.Equal(2, roundTrip.Controller.Board.Objects.Count);
        Assert.Single(roundTrip.Controller.Board.Strokes);
        Assert.Equal(UnifiedCanvasTool.Rectangle, roundTrip.Tool);
        Assert.True(roundTrip.ShowGrid);
        Assert.Equal("#E53935", roundTrip.Controller.PenColour);
        Assert.Equal(9, roundTrip.Controller.PenWidth, 3);
    }
}
