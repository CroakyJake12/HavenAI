namespace Haven.UI;

/// <summary>Backend-neutral hook for Haven elements that contribute their own draw commands.</summary>
public interface IHavenDrawCommandSource
{
    void Draw(HavenDrawingContext context, double opacity);
}
