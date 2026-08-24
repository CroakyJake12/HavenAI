namespace Haven.Infrastructure;

public sealed partial class WindowsComputerToolService
{
    private const string GameSafetyPrelude = """
        Add-Type @'
        using System;
        using System.Runtime.InteropServices;
        public static class HavenGameSafetyNative {
          [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
          [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        }
        '@
        function Get-HavenProcessClass([System.Diagnostics.Process]$process) {
          if (!$process) { return 'unknown' }
          $path='';$product='';$package=''
          try { $path=$process.Path } catch {}
          try { $product=$process.MainModule.FileVersionInfo.ProductName } catch {}
          $text=($process.ProcessName+' '+$process.MainWindowTitle+' '+$path+' '+$product+' '+$package)
          if ($text -match '(?i)UnrealEditorFortnite|Unreal Editor for Fortnite|\bUEFN\b') { return 'uefn' }
          if ($text -match '(?i)FortniteClient|FortniteLauncher|\bFortnite\b') { return 'fortnite' }
          if ($process.ProcessName -match '(?i)^(steam|steamwebhelper|EpicGamesLauncher|XboxPcApp|GamingApp)$') { return 'launcher' }
          if ($path -match '(?i)\\steamapps\\common\\|\\XboxGames\\|\\Epic Games\\') { return 'game' }
          return 'allowed'
        }
        function Assert-HavenTargetAllowed([System.Diagnostics.Process]$process) {
          $class=Get-HavenProcessClass $process
          if ($class -in @('game','fortnite','uefn')) { throw ('Blocked by Haven game interaction policy: '+$class) }
          return $class
        }
        function Assert-HavenLauncherControlAllowed([System.Diagnostics.Process]$process,[string]$label) {
          if ((Get-HavenProcessClass $process) -eq 'launcher' -and $label -match '(?i)^\s*(Play|Launch)(\s|$)') {
            throw 'Blocked by Haven game interaction policy: Computer Use cannot activate a launcher Play/Launch control.'
          }
        }
        function Get-HavenProcessForWindow([IntPtr]$handle) {
          if ($handle -eq [IntPtr]::Zero) { return $null }
          [uint32]$pid=0
          [HavenGameSafetyNative]::GetWindowThreadProcessId($handle,[ref]$pid) | Out-Null
          if ($pid -eq 0) { return $null }
          try { return Get-Process -Id $pid -ErrorAction Stop } catch { return $null }
        }
        function Assert-HavenForegroundNotProtected() {
          $foreground=[HavenGameSafetyNative]::GetForegroundWindow()
          $foregroundProcess=Get-HavenProcessForWindow $foreground
          if ($foregroundProcess) { Assert-HavenTargetAllowed $foregroundProcess | Out-Null }
        }
        function Assert-HavenForegroundSafe([IntPtr]$expectedHandle) {
          $foreground=[HavenGameSafetyNative]::GetForegroundWindow()
          $foregroundProcess=Get-HavenProcessForWindow $foreground
          if ($foregroundProcess) { Assert-HavenTargetAllowed $foregroundProcess | Out-Null }
          if ($foreground -ne $expectedHandle) { throw 'Computer Use stopped because the target window lost foreground before input dispatch.' }
        }
        function Test-HavenInstalledGameName([string]$wanted) {
          $needle=$wanted.Trim()
          if (!$needle) { return $false }
          $steamRoots=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
          try { $steam=(Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath; if($steam){$steamRoots.Add($steam)|Out-Null} } catch {}
          $defaultSteam=Join-Path ${env:ProgramFiles(x86)} 'Steam'; if(Test-Path $defaultSteam){$steamRoots.Add($defaultSteam)|Out-Null}
          foreach($root in @($steamRoots)) {
            $libraryFile=Join-Path $root 'steamapps\libraryfolders.vdf'
            $libraries=[System.Collections.Generic.List[string]]::new();$libraries.Add($root)
            if(Test-Path $libraryFile){
              foreach($m in [regex]::Matches((Get-Content $libraryFile -Raw -ErrorAction SilentlyContinue),'(?im)\"path\"\s+\"([^\"]+)\"')){
                $library=$m.Groups[1].Value -replace '\\\\','\'; if($library){$libraries.Add($library)}
              }
            }
            foreach($library in $libraries){
              foreach($manifest in Get-ChildItem (Join-Path $library 'steamapps\appmanifest_*.acf') -ErrorAction SilentlyContinue){
                $raw=Get-Content $manifest.FullName -Raw -ErrorAction SilentlyContinue
                $m=[regex]::Match($raw,'(?im)\"name\"\s+\"([^\"]+)\"')
                if($m.Success -and $m.Groups[1].Value.Equals($needle,[StringComparison]::OrdinalIgnoreCase)){return $true}
              }
            }
          }
          $epicRoot=Join-Path $env:ProgramData 'Epic\EpicGamesLauncher\Data\Manifests'
          foreach($manifest in Get-ChildItem $epicRoot -Filter '*.item' -ErrorAction SilentlyContinue){
            try{$item=Get-Content $manifest.FullName -Raw|ConvertFrom-Json;if($item.DisplayName -and $item.DisplayName.Equals($needle,[StringComparison]::OrdinalIgnoreCase)){return $true}}catch{}
          }
          $xboxRoot=Join-Path ${env:SystemDrive} 'XboxGames'
          foreach($game in Get-ChildItem $xboxRoot -Directory -ErrorAction SilentlyContinue){if($game.Name.Equals($needle,[StringComparison]::OrdinalIgnoreCase)){return $true}}
          return $false
        }
        function Assert-HavenLaunchAllowed([string]$wanted) {
          if ($wanted -match '(?i)Fortnite|UnrealEditorFortnite|Unreal Editor for Fortnite|\bUEFN\b') { throw 'Blocked by Haven game interaction policy: Fortnite and UEFN cannot be started through Computer Use.' }
          if ($wanted -match '(?i)^(steam://(run|rungameid)/|com\.epicgames\.launcher://apps/|xbox://)') { throw 'Blocked by Haven game interaction policy: game launch deep links are not available to Computer Use.' }
          if ($wanted -match '(?i)\\steamapps\\common\\|\\XboxGames\\|\\Epic Games\\') { throw 'Blocked by Haven game interaction policy: protected game executable path.' }
          if (Test-HavenInstalledGameName $wanted) { throw 'Blocked by Haven game interaction policy: the requested application is registered as a Steam, Epic, or Xbox game.' }
        }
        """;
}
