namespace Haven.Core;

public sealed class DataNamedRange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Range";
    /// <summary>A formula reference such as Sheet1!$A$1:$B$10.</summary>
    public string RefersTo { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);

    public void Normalize()
    {
        if (Id == Guid.Empty) Id = Guid.NewGuid();
        Name = string.IsNullOrWhiteSpace(Name) ? "Range" : Name.Trim();
        RefersTo = RefersTo?.Trim() ?? string.Empty;
        if (RefersTo.StartsWith('=') && RefersTo.Length > 1) RefersTo = RefersTo[1..].Trim();
        Comment ??= string.Empty;
        Metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
