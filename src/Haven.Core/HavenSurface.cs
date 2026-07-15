namespace Haven.Core;

/// <summary>
/// Identifies a top-level Haven product surface. Unlike <see cref="HavenMode"/>,
/// this value is UI navigation state and is deliberately not persisted in the
/// conversation database.
/// </summary>
public enum HavenSurface
{
    Home,
    Chat,
    Teach,
    Call,
    Do,
    Studio,
    Browse,
    Plan,
    Training
}

