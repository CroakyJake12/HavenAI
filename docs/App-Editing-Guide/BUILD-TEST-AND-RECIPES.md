# Build, Test and Editing Recipes

## Prerequisites

- Windows 10/11 x64
- .NET 10 SDK
- Ollama for model-backed chat
- Microsoft Edge WebView2 Runtime for Browse
- Git for project Git features

## Build and test

```powershell
dotnet restore Haven.sln
dotnet build Haven.sln -c Debug
dotnet test Haven.sln -c Debug
dotnet build .\src\Haven.AutomationWorker\Haven.AutomationWorker.csproj -c Debug
```

Use `scripts/build-windows.ps1` only when you deliberately want a published Windows package. Routine source edits should not leave publish or verification directories behind.

To refresh the editing-guide archive after changing this folder, replace the one root archive rather than creating numbered copies:

```powershell
Compress-Archive -Path .\docs\App-Editing-Guide\* `
  -DestinationPath .\Haven-App-Editing-Guide.zip -CompressionLevel Optimal -Force
```

## Add a page

1. Create `Views/MyPageView.axaml` and a small code-behind only if the view needs pickers/clipboard/native handles.
2. Create `MyPageViewModel` with observable state and commands.
3. Add a DataTemplate in `App.axaml`.
4. Open it from `MainWindowViewModel` with a stable tab key.
5. Verify minimum window size, high DPI, long text and keyboard access.

## Add or change a dropdown

For a simple enum, use ComboBox and inherit the global style. For a searchable selector or an option with description/RAM/capability metadata, use a Button plus Flyout panel. Keep 16px presenter radius, 7px presenter padding, 10px item radius and selected/hover states consistent.

## Add a project action

1. Put the operating-system/process behavior on `IProjectIntelligenceService` or another Application contract.
2. Implement it in Infrastructure with a fixed executable and bounded arguments/timeouts.
3. Validate all user-controlled paths, names and URLs.
4. Expose a command from `StudioProjectPageViewModel` or a typed model-facing runtime.
5. Report real output and failure details.
6. Add tests for validation and failure cleanup.

Never form a shell command from unvalidated user text when a direct process invocation works.

## Add a database field

1. Add a new migration version in `SqliteDatabase.cs`.
2. Update the domain record if needed.
3. Update repository reads, writes and SQL parameters together.
4. Test both a fresh database and upgrading an older schema.
5. Keep column defaults compatible with existing rows.

## Add Markdown or math syntax

Block parsing and Avalonia control creation are in `MarkdownView`. Inline tokens are handled by `InlinePattern` and `AddInlines`. Math command/script conversion is in `LatexFormatter`. Preserve the original message and only change rendering.

Test at least streaming partial input, unclosed delimiters, nested emphasis, long code, Unicode, common formula scripts and malformed text. The renderer must fail as readable text, not crash chat layout.

## Add a browser feature

Separate transient UI state from persistent `BrowserDataService` state. Update JSON atomically through the service. Keep private data exclusions and credential boundaries intact. If the feature needs the native WebView, expose honest attached/unattached availability.

## Verification checklist

- Solution build has zero errors and preferably zero warnings.
- Core and infrastructure test suites pass.
- Automation worker builds.
- New project, NuGet package and existing-folder paths work in a disposable location.
- Project chat can list/read/edit a file and run a typed test under the expected permission.
- Switching between project home, chat and file keeps the project sidebar.
- Markdown, lists, code blocks, tables and `$` / `$$` math render without raw markers.
- Browse starts, navigates, switches tab layout and persists bookmarks.
- Unsupported plugins are absent from the picker in the wrong mode/platform/runtime.
- Command palette still opens with Ctrl+K.
- Temporary verification outputs are removed; the editing-guide ZIP is the only intentional guide archive.

## Troubleshooting

### Metadata DLL could not be found

Fix the first project compilation error. Missing reference assemblies are normally downstream symptoms.

### Avalonia native child window failure

Confirm `app.manifest` is assigned, WebView2 is installed, and only one native WebView host is mounted. Do not repeatedly recreate it while changing tab layout.

### Build output is locked

Close the running Haven instance or build to an absolute isolated `ArtifactsPath`. Do not delete a path until its resolved absolute location has been checked to be inside the intended workspace.

### Tool is visible but cannot run

Check `ToolAvailabilityContext`, the plugin's allowed modes, selected root existence, permission, platform and runtime availability. Both picker and `ChatSessionService` must consume the same plan.

### Data looks stale

Start with a clean `HAVEN_DATA_DIR` test profile. Do not delete the normal profile while diagnosing a migration.
