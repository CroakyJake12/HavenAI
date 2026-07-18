/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/WindowsComputerToolService.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns WindowsComputerToolService, HavenForeground, HavenMouse, Rect, HavenClose, HavenWindow, Regexes. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text;
using System.Net;
using System.Text.RegularExpressions;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>
/// Represents windows computer tool service and keeps its related state and behavior together.
/// </summary>
public sealed partial class WindowsComputerToolService(IWorkspaceToolService processes) : IComputerToolService
{
    /// <summary>
    /// Performs snapshot asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<string> SnapshotAsync(CancellationToken cancellationToken) => RunPowerShellAsync(
        """
        Add-Type -AssemblyName UIAutomationClient
        Add-Type -AssemblyName UIAutomationTypes
        Add-Type @'
        using System;
        using System.Runtime.InteropServices;
        /// <summary>
        /// Represents haven foreground and keeps its related state and behavior together.
        /// </summary>
        public static class HavenForeground { [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); }
        '@
        $handle = [HavenForeground]::GetForegroundWindow()
        if ($handle -eq [IntPtr]::Zero) { throw 'No foreground window was available' }
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
        if (!$root) { throw 'The foreground window has no UI Automation root' }
        $items = $root.FindAll([System.Windows.Automation.TreeScope]::Subtree, [System.Windows.Automation.Condition]::TrueCondition)
        $out = [System.Collections.Generic.List[object]]::new()
        $usefulTypes = @('ControlType.Button','ControlType.Edit','ControlType.Hyperlink','ControlType.ListItem','ControlType.TabItem','ControlType.MenuItem','ControlType.CheckBox','ControlType.RadioButton','ControlType.ComboBox','ControlType.Document','ControlType.Text')
        for ($i = 0; $i -lt $items.Count -and $out.Count -lt 240; $i++) {
          $element = $items.Item($i)
          try {
            $name = $element.Current.Name
            $id = $element.Current.AutomationId
            $rect = $element.Current.BoundingRectangle
            $controlType = $element.Current.ControlType.ProgrammaticName
            if (!$element.Current.IsOffscreen -and ($name -or $id) -and $usefulTypes -contains $controlType -and $rect.Width -gt 0 -and $rect.Height -gt 0) {
              $out.Add([pscustomobject]@{ name=$name; automationId=$id; controlType=$controlType; left=[math]::Round($rect.Left); top=[math]::Round($rect.Top); width=[math]::Round($rect.Width); height=[math]::Round($rect.Height) })
            }
          } catch {}
        }
        [pscustomobject]@{ windowTitle=$root.Current.Name; elements=$out } | ConvertTo-Json -Depth 4 -Compress
        """, TimeSpan.FromSeconds(45), cancellationToken);

    /// <summary>
    /// Performs list windows asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<string> ListWindowsAsync(CancellationToken cancellationToken) => RunPowerShellAsync(
        "Get-Process | Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle } | Select-Object Id,ProcessName,MainWindowTitle | ConvertTo-Json -Depth 3 -Compress",
        TimeSpan.FromSeconds(20), cancellationToken);

    /// <summary>
    /// Performs launch app asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<string> LaunchAppAsync(string name, CancellationToken cancellationToken)
    {
        var script = Utf8Variable("wanted", name) +
            """
            $before = @{}
            Get-Process | ForEach-Object { $before[$_.Id] = $true }
            $app = Get-StartApps | Where-Object { $_.Name -like ('*' + $wanted + '*') } | Select-Object -First 1
            if ($app) { Start-Process ('shell:AppsFolder\' + $app.AppID) }
            else { try { Start-Process -FilePath $wanted } catch { throw ('Application not found: ' + $wanted) } }
            $deadline = (Get-Date).AddSeconds(12)
            $match = $null
            while ((Get-Date) -lt $deadline -and !$match) {
              Start-Sleep -Milliseconds 250
              $candidates = Get-Process | Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle }
              $match = $candidates | Where-Object { !$before.ContainsKey($_.Id) } | Select-Object -First 1
              if (!$match) { $match = $candidates | Where-Object { $_.MainWindowTitle -like ('*' + $wanted + '*') } | Select-Object -First 1 }
              if (!$match) {
                $wantedTokens = @([regex]::Matches($wanted, '[\p{L}\p{Nd}]+') | ForEach-Object { $_.Value } | Where-Object { $_.Length -ge 3 })
                foreach ($candidate in $candidates) {
                  $haystack = $candidate.MainWindowTitle + ' ' + $candidate.ProcessName
                  $allMatch = $wantedTokens.Count -gt 0
                  foreach ($token in $wantedTokens) { if ($haystack -notlike ('*' + $token + '*')) { $allMatch = $false; break } }
                  $lastTokenMatchesProcess = $wantedTokens.Count -gt 0 -and $candidate.ProcessName -like ('*' + $wantedTokens[-1] + '*')
                  if ($allMatch -or $lastTokenMatchesProcess) { $match = $candidate; break }
                }
              }
            }
            if (!$match) { throw 'Application launched but no visible window appeared' }
            [pscustomobject]@{ launched=$true; processId=$match.Id; processName=$match.ProcessName; windowTitle=$match.MainWindowTitle } | ConvertTo-Json -Compress
            """;
        return RunPowerShellAsync(script, TimeSpan.FromSeconds(20), cancellationToken);
    }

    /// <summary>
    /// Performs focus window asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<string> FocusWindowAsync(string title, CancellationToken cancellationToken)
    {
        var script = Utf8Variable("title", title) + TargetWindowPrelude +
            """
            $shell = New-Object -ComObject WScript.Shell
            $shell.AppActivate($target.Id) | Out-Null
            [HavenWindow]::ShowWindowAsync($target.MainWindowHandle, 9) | Out-Null
            $ok = $false
            for ($i = 0; $i -lt 8 -and !$ok; $i++) {
              [HavenWindow]::SetForegroundWindow($target.MainWindowHandle) | Out-Null
              Start-Sleep -Milliseconds 120
              $ok = [HavenWindow]::GetForegroundWindow() -eq $target.MainWindowHandle
            }
            if (!$ok) { throw 'Could not verify the requested window as foreground' }
            [pscustomobject]@{ processId=$target.Id; processName=$target.ProcessName; windowTitle=$target.MainWindowTitle; foreground=$true } | ConvertTo-Json -Compress
            """;
        return RunPowerShellAsync(script, TimeSpan.FromSeconds(20), cancellationToken);
    }

    /// <summary>
    /// Performs invoke asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<string> InvokeAsync(string windowTitle, string name, string automationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(automationId))
            throw new ArgumentException("name or automation_id is required.");
        var script = Utf8Variable("title", windowTitle) + Utf8Variable("name", name) + Utf8Variable("automationId", automationId) + TargetWindowPrelude +
            """
            Add-Type -AssemblyName UIAutomationClient
            Add-Type -AssemblyName UIAutomationTypes
            $root = [System.Windows.Automation.AutomationElement]::FromHandle($target.MainWindowHandle)
            if (!$root) { throw 'Target window has no automation root' }
            $items = $root.FindAll([System.Windows.Automation.TreeScope]::Subtree, [System.Windows.Automation.Condition]::TrueCondition)
            $match = $null
            for ($i = 0; $i -lt $items.Count; $i++) {
              $element = $items.Item($i)
              try {
                if (($automationId -and $element.Current.AutomationId -eq $automationId) -or ($name -and $element.Current.Name -like ('*' + $name + '*'))) { $match = $element; break }
              } catch {}
            }
            if (!$match) { throw 'UI element was not found inside the target window' }
            $pattern = $null
            if ($match.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit) {
              $match.SetFocus()
              $method = 'focus'
            } elseif ($match.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
              ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
              $method = 'invoke'
            } else {
              $match.SetFocus()
              Add-Type -AssemblyName System.Windows.Forms
              [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
              $method = 'enter'
            }
            [pscustomobject]@{ windowTitle=$target.MainWindowTitle; elementName=$match.Current.Name; automationId=$match.Current.AutomationId; method=$method } | ConvertTo-Json -Compress
            """;
        return RunPowerShellAsync(script, TimeSpan.FromSeconds(25), cancellationToken);
    }

    /// <summary>
    /// Performs click asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<string> ClickAsync(string windowTitle, int x, int y, string button, CancellationToken cancellationToken)
    {
        if (x < 0 || y < 0 || x > 100000 || y > 100000) throw new ArgumentOutOfRangeException(nameof(x), "Valid non-negative coordinates are required.");
        var normalized = button.Trim().ToLowerInvariant();
        var flags = normalized switch { "right" => "0x0008,0x0010", "middle" => "0x0020,0x0040", _ => "0x0002,0x0004" };
        var script = Utf8Variable("title", windowTitle) + TargetWindowPrelude + $"$x={x};$y={y};$flags=@({flags});" +
            """
            Add-Type @'
            using System;
            using System.Runtime.InteropServices;
            /// <summary>
            /// Represents haven mouse and keeps its related state and behavior together.
            /// </summary>
            public static class HavenMouse {
              /// <summary>
              /// Represents rect and keeps its related state and behavior together.
              /// </summary>
              [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
              [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
              [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle, out Rect rect);
              public struct Rect { public int Left, Top, Right, Bottom; }
            }
            '@
            $rect = New-Object HavenMouse+Rect
            [HavenMouse]::GetWindowRect($target.MainWindowHandle, [ref]$rect) | Out-Null
            if ($x -lt $rect.Left -or $x -gt $rect.Right -or $y -lt $rect.Top -or $y -gt $rect.Bottom) { throw 'Coordinates are outside the target window' }
            $shell = New-Object -ComObject WScript.Shell
            $shell.AppActivate($target.Id) | Out-Null
            [HavenWindow]::SetForegroundWindow($target.MainWindowHandle) | Out-Null
            Start-Sleep -Milliseconds 180
            if ([HavenWindow]::GetForegroundWindow() -ne $target.MainWindowHandle) { throw 'Target window was not foreground' }
            [HavenMouse]::SetCursorPos($x, $y) | Out-Null
            Start-Sleep -Milliseconds 100
            $flags | ForEach-Object { [HavenMouse]::mouse_event([uint32]$_, 0, 0, 0, [UIntPtr]::Zero) }
            [pscustomobject]@{ windowTitle=$target.MainWindowTitle; x=$x; y=$y } | ConvertTo-Json -Compress
            """;
        return RunPowerShellAsync(script, TimeSpan.FromSeconds(20), cancellationToken);
    }

    /// <summary>
    /// Performs type asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<string> TypeAsync(string windowTitle, string text, CancellationToken cancellationToken)
    {
        if (text.Length == 0) throw new ArgumentException("text is required.", nameof(text));
        var script = Utf8Variable("title", windowTitle) + Utf8Variable("inputText", text) + TargetWindowPrelude +
            """
            Add-Type -AssemblyName System.Windows.Forms
            $shell = New-Object -ComObject WScript.Shell
            $shell.AppActivate($target.Id) | Out-Null
            [HavenWindow]::SetForegroundWindow($target.MainWindowHandle) | Out-Null
            Start-Sleep -Milliseconds 220
            if ([HavenWindow]::GetForegroundWindow() -ne $target.MainWindowHandle) { throw 'Target window was not foreground' }
            Set-Clipboard -Value $inputText
            [System.Windows.Forms.SendKeys]::SendWait('^v')
            Start-Sleep -Milliseconds 150
            [pscustomobject]@{ windowTitle=$target.MainWindowTitle; characters=$inputText.Length; inputSent=$true } | ConvertTo-Json -Compress
            """;
        return RunPowerShellAsync(script, TimeSpan.FromSeconds(20), cancellationToken);
    }

    /// <summary>
    /// Performs press asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<string> PressAsync(string windowTitle, string keys, CancellationToken cancellationToken)
    {
        var sequence = ToSendKeys(keys);
        var script = Utf8Variable("title", windowTitle) + Utf8Variable("sequence", sequence) + Utf8Variable("keys", keys) + TargetWindowPrelude +
            """
            Add-Type -AssemblyName System.Windows.Forms
            $shell = New-Object -ComObject WScript.Shell
            $shell.AppActivate($target.Id) | Out-Null
            [HavenWindow]::SetForegroundWindow($target.MainWindowHandle) | Out-Null
            Start-Sleep -Milliseconds 220
            if ([HavenWindow]::GetForegroundWindow() -ne $target.MainWindowHandle) { throw 'Target window was not foreground' }
            [System.Windows.Forms.SendKeys]::SendWait($sequence)
            [pscustomobject]@{ windowTitle=$target.MainWindowTitle; keys=$keys; inputSent=$true } | ConvertTo-Json -Compress
            """;
        return RunPowerShellAsync(script, TimeSpan.FromSeconds(20), cancellationToken);
    }

    /// <summary>
    /// Performs close window asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<string> CloseWindowAsync(string title, CancellationToken cancellationToken)
    {
        var script = Utf8Variable("title", title) + TargetWindowPrelude +
            """
            if ($target.ProcessName -match '^(msedge|chrome|firefox|brave)$') { throw 'Closing browser windows through Computer Use is blocked' }
            Add-Type @'
            using System;
            using System.Runtime.InteropServices;
            /// <summary>
            /// Represents haven close and keeps its related state and behavior together.
            /// </summary>
            public static class HavenClose { [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam); }
            '@
            [HavenClose]::PostMessage($target.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
            Start-Sleep -Milliseconds 400
            [pscustomobject]@{ windowTitle=$target.MainWindowTitle; processName=$target.ProcessName; closeRequested=$true } | ConvertTo-Json -Compress
            """;
        return RunPowerShellAsync(script, TimeSpan.FromSeconds(20), cancellationToken);
    }

    /// <summary>
    /// Runs run power shell async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private async Task<string> RunPowerShellAsync(string script, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Computer Use currently requires Windows.");
        var prelude = "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';$InformationPreference='SilentlyContinue';[Console]::OutputEncoding=[Text.UTF8Encoding]::new();";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(prelude + script));
        var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = await processes.RunProcessAsync(new ProcessRequest(
            "powershell.exe",
            $"-NoLogo -NoProfile -STA -NonInteractive -OutputFormat Text -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            workingDirectory,
            timeout), cancellationToken).ConfigureAwait(false);
        if (result.TimedOut) throw new TimeoutException("The Windows desktop action timed out.");
        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail) ? "The Windows desktop action failed." : CleanPowerShellError(detail));
        }
        return result.StandardOutput.Trim();
    }

    /// <summary>
    /// Performs the clean power shell error step owned by this component.
    /// </summary>
    private static string CleanPowerShellError(string value)
    {
        if (!value.Contains("CLIXML", StringComparison.OrdinalIgnoreCase))
            return value.Trim();

        var lines = Regex.Matches(value, @"<S\s+S=""Error"">(.*?)</S>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
            .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))
            .Select(text => Regex.Replace(text, @"_?x000[Dd]_?|_?x000[Aa]_?", ""))
            .SelectMany(text => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .Select(text => text.Trim())
            .Where(text => text.Length > 0 && !text.StartsWith("At line:", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("+", StringComparison.Ordinal))
            .ToArray();
        return lines.FirstOrDefault(text => text.Contains("not found", StringComparison.OrdinalIgnoreCase) || text.Contains("failed", StringComparison.OrdinalIgnoreCase))
            ?? lines.FirstOrDefault()
            ?? "The Windows desktop action failed.";
    }

    /// <summary>
    /// Performs the utf8 variable step owned by this component.
    /// </summary>
    private static string Utf8Variable(string name, string value)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        return $"${name}=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encoded}'));";
    }

    /// <summary>
    /// Performs the to send keys step owned by this component.
    /// </summary>
    private static string ToSendKeys(string keys)
    {
        var normalized = keys.Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (normalized is "ALT+F4" or "CTRL+W" or "CTRL+SHIFT+W" or "ALT+SPACE")
            throw new InvalidOperationException("Window-closing shortcuts are blocked; use computer_close_window with an exact title.");
        var parts = normalized.Split('+', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new ArgumentException("keys is required.", nameof(keys));
        var prefix = string.Concat(parts[..^1].Select(modifier => modifier switch
        {
            "CTRL" or "CONTROL" => "^",
            "ALT" => "%",
            "SHIFT" => "+",
            _ => throw new ArgumentException($"Unsupported modifier '{modifier}'.", nameof(keys))
        }));
        var key = parts[^1] switch
        {
            "ENTER" => "{ENTER}",
            "TAB" => "{TAB}",
            "ESC" or "ESCAPE" => "{ESC}",
            "UP" => "{UP}",
            "DOWN" => "{DOWN}",
            "LEFT" => "{LEFT}",
            "RIGHT" => "{RIGHT}",
            "DELETE" => "{DELETE}",
            "BACKSPACE" => "{BACKSPACE}",
            "HOME" => "{HOME}",
            "END" => "{END}",
            "PGUP" => "{PGUP}",
            "PGDN" => "{PGDN}",
            "SPACE" => " ",
            var value when value.Length == 1 => value,
            var value when Regexes.FunctionKey().IsMatch(value) => "{" + value + "}",
            var value => throw new ArgumentException($"Unsupported key '{value}'.", nameof(keys))
        };
        return prefix + key;
    }

    /// <summary>
    /// Stores target window prelude locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const string TargetWindowPrelude = """
        Add-Type @'
        using System;
        using System.Runtime.InteropServices;
        /// <summary>
        /// Represents haven window and keeps its related state and behavior together.
        /// </summary>
        public static class HavenWindow {
          [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
          [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr handle, int command);
          [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        }
        '@
        $candidates = Get-Process | Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle }
        $target = $candidates | Where-Object { $_.MainWindowTitle -like ('*' + $title + '*') } | Select-Object -First 1
        if (!$target) {
          $titleTokens = @([regex]::Matches($title, '[\p{L}\p{Nd}]+') | ForEach-Object { $_.Value } | Where-Object { $_.Length -ge 3 })
          foreach ($candidate in $candidates) {
            $haystack = $candidate.MainWindowTitle + ' ' + $candidate.ProcessName
            $allMatch = $titleTokens.Count -gt 0
            foreach ($token in $titleTokens) { if ($haystack -notlike ('*' + $token + '*')) { $allMatch = $false; break } }
            if ($allMatch) { $target = $candidate; break }
          }
        }
        if (!$target) { throw 'Target window was not found' }
        """;

    /// <summary>
    /// Represents regexes and keeps its related state and behavior together.
    /// </summary>
    private static partial class Regexes
    {
        /// <summary>
        /// Performs the function key step owned by this component.
        /// </summary>
        [System.Text.RegularExpressions.GeneratedRegex(@"^F([1-9]|1[0-2])$")]
        public static partial System.Text.RegularExpressions.Regex FunctionKey();
    }
}
