using Haven.UI;
using Xunit;

namespace Haven.UI.Tests;

public sealed class HavenDirectManipulationTests
{
    [Fact]
    public void Move_preview_preserves_size_and_clamps_to_constraint_bounds()
    {
        var session = HavenDirectManipulationSession.Move(
            new HavenRect(20, 20, 40, 30),
            new HavenPoint(30, 30));

        var result = session.BoundsAt(
            new HavenPoint(160, 150),
            new HavenManipulationConstraints(Bounds: new HavenRect(0, 0, 120, 100)));

        Assert.Equal(new HavenRect(80, 70, 40, 30), result);
    }

    [Fact]
    public void Resize_preview_honours_active_edges_and_minimum_size()
    {
        var session = HavenDirectManipulationSession.Resize(
            new HavenRect(20, 20, 100, 80),
            new HavenPoint(20, 20),
            HavenResizeEdges.Left | HavenResizeEdges.Top);

        var result = session.BoundsAt(
            new HavenPoint(115, 90),
            new HavenManipulationConstraints(MinWidth: 40, MinHeight: 30));

        Assert.Equal(new HavenRect(80, 70, 40, 30), result);
    }

    [Fact]
    public void Resize_preview_clamps_outward_edges_to_constraint_bounds()
    {
        var session = HavenDirectManipulationSession.Resize(
            new HavenRect(20, 20, 100, 80),
            new HavenPoint(120, 100),
            HavenResizeEdges.Right | HavenResizeEdges.Bottom);

        var result = session.BoundsAt(
            new HavenPoint(300, 300),
            new HavenManipulationConstraints(MinWidth: 20, MinHeight: 20, Bounds: new HavenRect(0, 0, 150, 130)));

        Assert.Equal(new HavenRect(20, 20, 130, 110), result);
    }

    [Fact]
    public void Rotate_preview_uses_pointer_angle_delta_without_mutating_bounds()
    {
        var bounds = new HavenRect(0, 0, 100, 100);
        var session = HavenDirectManipulationSession.Rotate(bounds, new HavenPoint(50, 0), 10);

        Assert.Equal(bounds, session.BoundsAt(new HavenPoint(100, 50)));
        Assert.Equal(100d, session.RotationAt(new HavenPoint(100, 50)), 6);
    }

    [Fact]
    public void Resize_requires_an_explicit_edge()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HavenDirectManipulationSession.Resize(
            new HavenRect(0, 0, 10, 10),
            new HavenPoint(0, 0),
            HavenResizeEdges.None));
    }
}
