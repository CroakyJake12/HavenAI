namespace Haven.Application;

public enum ComputerUseApplicationClass { Allowed = 0, GameLauncher = 1, ProtectedGame = 2, Fortnite = 3, Uefn = 4 }

public sealed record ComputerUseApplicationIdentity(
    string ProcessName,
    string WindowTitle = "",
    string ExecutablePath = "",
    string ProductName = "",
    string PackageIdentity = "",
    string InstallSource = "");

/// <summary>Product-level Computer Use classification. Official APIs such as MCP do not pass through this policy.</summary>
public static class ComputerUseApplicationPolicy
{
    public const string BlockedMessage = "Blocked by Haven game interaction policy";

    public static ComputerUseApplicationClass Classify(ComputerUseApplicationIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var process = identity.ProcessName.Trim();
        var haystack = string.Join(' ', process, identity.WindowTitle, identity.ExecutablePath, identity.ProductName, identity.PackageIdentity);
        if (ContainsAny(haystack, "UnrealEditorFortnite", "Unreal Editor for Fortnite", "UEFN")) return ComputerUseApplicationClass.Uefn;
        if (ContainsAny(haystack, "FortniteClient", "FortniteLauncher", "Fortnite")) return ComputerUseApplicationClass.Fortnite;
        if (IsLauncherProcess(process, identity.PackageIdentity)) return ComputerUseApplicationClass.GameLauncher;
        var source = identity.InstallSource.Trim().ToLowerInvariant();
        if (source is "steam" or "epic" or "xbox") return ComputerUseApplicationClass.ProtectedGame;
        var path = identity.ExecutablePath.Replace('/', '\\');
        if (path.Contains("\\steamapps\\common\\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("\\XboxGames\\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("\\Epic Games\\", StringComparison.OrdinalIgnoreCase)) return ComputerUseApplicationClass.ProtectedGame;
        return ComputerUseApplicationClass.Allowed;
    }

    public static bool IsHardBlocked(ComputerUseApplicationIdentity identity) => Classify(identity) is ComputerUseApplicationClass.ProtectedGame or ComputerUseApplicationClass.Fortnite or ComputerUseApplicationClass.Uefn;

    public static bool IsBlockedLauncherAction(ComputerUseApplicationIdentity identity, string? actionName) =>
        Classify(identity) == ComputerUseApplicationClass.GameLauncher &&
        !string.IsNullOrWhiteSpace(actionName) &&
        (actionName.Equals("Play", StringComparison.OrdinalIgnoreCase) || actionName.Equals("Launch", StringComparison.OrdinalIgnoreCase) ||
         actionName.StartsWith("Play ", StringComparison.OrdinalIgnoreCase) || actionName.StartsWith("Launch ", StringComparison.OrdinalIgnoreCase));

    public static bool IsBlockedLaunchRequest(string nameOrPath)
    {
        var value = nameOrPath.Trim();
        if (ContainsAny(value, "Fortnite", "UnrealEditorFortnite", "Unreal Editor for Fortnite", "UEFN")) return true;
        if (value.StartsWith("steam://run/", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("steam://rungameid/", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("com.epicgames.launcher://apps/", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("xbox://", StringComparison.OrdinalIgnoreCase)) return true;
        var path = value.Replace('/', '\\');
        return path.Contains("\\steamapps\\common\\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("\\XboxGames\\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("\\Epic Games\\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLauncherProcess(string process, string packageIdentity) =>
        process.Equals("steam", StringComparison.OrdinalIgnoreCase) || process.Equals("steamwebhelper", StringComparison.OrdinalIgnoreCase) ||
        process.Equals("EpicGamesLauncher", StringComparison.OrdinalIgnoreCase) || process.Equals("XboxPcApp", StringComparison.OrdinalIgnoreCase) ||
        process.Equals("GamingApp", StringComparison.OrdinalIgnoreCase) || packageIdentity.Contains("Microsoft.GamingApp", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string value, params string[] needles) => needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
