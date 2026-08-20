using System.Text.Json;
using Haven.Application;
namespace Haven.Infrastructure;
public sealed partial class WindowsComputerToolService
{
    public async Task<ComputerSelectionSnapshot?> GetSelectionSnapshotAsync(CancellationToken cancellationToken)
    {
        var json = await RunPowerShellAsync(
            """
            Add-Type -AssemblyName UIAutomationClient
            Add-Type -AssemblyName UIAutomationTypes
            Add-Type @'
            using System; using System.Runtime.InteropServices;
            public static class HavenSelectionNative { [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); }
            '@
            $h=[HavenSelectionNative]::GetForegroundWindow()
            if($h -eq [IntPtr]::Zero){return}
            $root=[System.Windows.Automation.AutomationElement]::FromHandle($h)
            $f=[System.Windows.Automation.AutomationElement]::FocusedElement
            if(!$root -or !$f){return}
            $text=$null;$truncated=$false
            try {
                $textSource=$f;$tp=$null;$walker=[System.Windows.Automation.TreeWalker]::ControlViewWalker
                for($depth=0;$textSource -and $depth -lt 8 -and !$tp;$depth++){
                    try{$tp=$textSource.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)}catch{}
                    if(!$tp){$textSource=$walker.GetParent($textSource)}
                }
                if($tp){
                    $parts=[System.Collections.Generic.List[string]]::new()
                    foreach($r in @($tp.GetSelection())){
                        $v=$r.GetText(-1)
                        if(![string]::IsNullOrWhiteSpace($v)){$parts.Add($v)}
                    }
                    if($parts.Count -gt 0){
                        $text=[string]::Join([Environment]::NewLine,$parts)
                        if($text.Length -gt 8192){$text=$text.Substring(0,8192);$truncated=$true}
                    }
                }
            } catch {}
            $b=$f.Current.BoundingRectangle;$selected=$null
            try{$si=$f.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern);if($si){$selected=$si.Current.IsSelected}}catch{}
            $processId=$root.Current.ProcessId;$app=$null
            if($processId -gt 0){try{$app=(Get-Process -Id $processId -ErrorAction Stop).ProcessName}catch{}}
            [pscustomobject]@{
                SelectedText=$text;SourceApplication=$app;SourceWindow=$root.Current.Name
                X=$b.X;Y=$b.Y;Width=$b.Width;Height=$b.Height
                AccessibleName=$f.Current.Name;AutomationId=$f.Current.AutomationId
                ControlType=$f.Current.ControlType.ProgrammaticName;IsEnabled=$f.Current.IsEnabled
                IsSelected=$selected;CapturedAt=[DateTimeOffset]::UtcNow.ToString("O");WasTruncated=$truncated
            }|ConvertTo-Json -Compress -Depth 3
            """,
            TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<ComputerSelectionSnapshot>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}
