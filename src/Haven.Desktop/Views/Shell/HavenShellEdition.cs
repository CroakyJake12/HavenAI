namespace Haven.Desktop.Views.Shell;

/// <summary>
/// Identifies the current Haven shell and its temporary legacy compatibility path.
/// Startup always selects New; Classic remains only until migration parity permits deletion.
/// </summary>
public enum HavenShellEdition
{
    New,
    Classic
}
