using Haven.Application;

namespace Haven.Core.Tests;

public sealed class ComputerUseApplicationPolicyTests
{
    [Theory]
    [InlineData("FortniteClient-Win64-Shipping", "Fortnite", ComputerUseApplicationClass.Fortnite)]
    [InlineData("UnrealEditorFortnite-Win64-Shipping", "Unreal Editor for Fortnite", ComputerUseApplicationClass.Uefn)]
    public void ExplicitHardBlocksDoNotDependOnStoreMetadata(string process, string title, ComputerUseApplicationClass expected)
    {
        var identity = new ComputerUseApplicationIdentity(process, title);
        Assert.Equal(expected, ComputerUseApplicationPolicy.Classify(identity));
        Assert.True(ComputerUseApplicationPolicy.IsHardBlocked(identity));
    }

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Example Game\game.exe")]
    [InlineData(@"D:\XboxGames\Example Game\Content\game.exe")]
    [InlineData(@"C:\Program Files\Epic Games\ExampleGame\game.exe")]
    public void StoreInstalledGamePathsAreProtected(string path)
    {
        var identity = new ComputerUseApplicationIdentity("game", ExecutablePath: path);
        Assert.Equal(ComputerUseApplicationClass.ProtectedGame, ComputerUseApplicationPolicy.Classify(identity));
        Assert.True(ComputerUseApplicationPolicy.IsHardBlocked(identity));
    }

    [Theory]
    [InlineData("steam")]
    [InlineData("EpicGamesLauncher")]
    [InlineData("GamingApp")]
    public void GameLaunchersRemainAvailableForNonGameUi(string process)
    {
        var identity = new ComputerUseApplicationIdentity(process);
        Assert.Equal(ComputerUseApplicationClass.GameLauncher, ComputerUseApplicationPolicy.Classify(identity));
        Assert.False(ComputerUseApplicationPolicy.IsHardBlocked(identity));
        Assert.False(ComputerUseApplicationPolicy.IsBlockedLauncherAction(identity, "Library"));
    }

    [Theory]
    [InlineData("Play")]
    [InlineData("Launch")]
    [InlineData("Play Fortnite")]
    [InlineData("Launch game")]
    public void LauncherPlayAndLaunchActionsAreBlocked(string label)
    {
        var launcher = new ComputerUseApplicationIdentity("EpicGamesLauncher");
        Assert.True(ComputerUseApplicationPolicy.IsBlockedLauncherAction(launcher, label));
    }

    [Theory]
    [InlineData("steam://run/1234")]
    [InlineData("steam://rungameid/1234")]
    [InlineData("com.epicgames.launcher://apps/Fortnite?action=launch")]
    [InlineData("xbox://game/1234")]
    [InlineData("Fortnite")]
    [InlineData("UEFN")]
    public void ProtectedGameLaunchRequestsAreBlocked(string request) =>
        Assert.True(ComputerUseApplicationPolicy.IsBlockedLaunchRequest(request));

    [Fact]
    public void NormalApplicationIsAllowed()
    {
        var identity = new ComputerUseApplicationIdentity("notepad", "Untitled - Notepad", @"C:\Windows\System32\notepad.exe", "Notepad");
        Assert.Equal(ComputerUseApplicationClass.Allowed, ComputerUseApplicationPolicy.Classify(identity));
        Assert.False(ComputerUseApplicationPolicy.IsHardBlocked(identity));
        Assert.False(ComputerUseApplicationPolicy.IsBlockedLaunchRequest("Notepad"));
    }
}
