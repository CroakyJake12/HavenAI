# Haven Desktop Architecture

## Overview

Haven Desktop is a local-first AI assistant built with Avalonia UI. It uses raw C# code-behind (no MVVM bindings) with a central event bus for inter-component communication.

## Folder Structure

```
src/Haven.Desktop/
├── Views/
│   ├── Shell/                    # Application shell
│   │   ├── MainView.axaml        # Root shell (TopRail + SideRail + Sidebar + content)
│   │   ├── MainView.axaml.cs     # Navigation, keyboard shortcuts, event wiring
│   │   ├── TopRail/              # Header bar (logo, menus, status)
│   │   ├── SideRail/             # Left icon rail (mode navigation)
│   │   └── Sidebar/              # Right sidebar (conversations, containers, pins)
│   ├── Pages/                    # All page implementations
│   │   ├── Home/                 # Dashboard
│   │   ├── Chat/                 # Conversation interface
│   │   ├── Call/                 # Voice calls
│   │   ├── Plan/                 # Planner/scheduler
│   │   ├── Browser/              # Embedded browser
│   │   ├── Training/             # Training sessions
│   │   ├── Notes/                # Notes workspace
│   │   ├── Settings/             # Application settings
│   │   ├── Catalog/              # Agents/plugins/prompts
│   │   ├── Automations/          # Scheduled actions
│   │   ├── Tasks/                # Reusable and automatic tasks
│   │   ├── Archive/              # Archived items
│   │   ├── ActivityLog/          # Conversation history
│   │   ├── ModeLibrary/          # Mode discovery
│   │   ├── ProjectCreator/       # New project wizard
│   │   ├── StudioProject/        # Project workspace
│   │   ├── WorkspaceEditor/      # File editor
│   │   ├── WorkspaceHome/        # Studio/Tasks home
│   │   ├── ChatGroup/            # Chat group management
│   │   ├── ContainerSettings/    # Container configuration
│   │   └── LessonSettings/       # Lesson configuration
│   ├── [Old Views]               # Legacy views (still used by adapter pages)
│   └── [Controls]                # Reusable controls
├── Events/                       # Event system
│   ├── HavenEventBus.cs          # Central event bus
│   ├── Subscribe.cs              # Subscribe function with cooldown
│   ├── EventToken.cs             # Event reference token
│   ├── ElementProxy.cs           # Static proxy classes for element tree
│   └── EventRegistration.cs      # Extension methods for registration
├── Services/                     # Desktop-specific services
├── Controls/                     # Custom controls
├── Information/                  # Documentation
│   ├── README.md                 # This file
│   └── ObjectDigest.md           # Auto-generated API reference
└── [Other files]
```

## Event System

### HavenEventBus

The central event bus registers named UI elements and exposes pointer events with the naming convention `Page.Section.Name.Event()`.

```csharp
// Register an element
bus.RegisterElement("Home.Dashboard.Tile0", tileBorder);
bus.WirePointerEvents("Home.Dashboard.Tile0", tileBorder);

// Fire an event
bus.Fire("Home.Dashboard.Tile0.Click");
```

### Subscribe Function

Listen for events with optional cooldown:

```csharp
Subscribe.To(TopRail.Actions.Hover(), () =>
{
    Console.WriteLine("Hovered over Actions");
    Subscribe.Cooldown(3); // 3 second cooldown
});
```

### Element Proxies

Static proxy classes expose the element tree with type-safe event tokens:

```csharp
// Generate event tokens
var token = Home.Dashboard.Tile(0).Click();
var token = TopRail.Actions.Hover();
var token = Chat.Composer.SendClick();

// Subscribe to events
Subscribe.To(token, () => { ... });
```

## Page Architecture

### New Pages (Code-Behind)

Pages migrated to the new system use raw C# code-behind:

```xml
<!-- HomePage.axaml -->
<UserControl x:Class="Haven.Desktop.Views.Pages.Home.HomePage">
    <StackPanel x:Name="TilesPanel" />
</UserControl>
```

```csharp
// HomePage.axaml.cs
public partial class HomePage : UserControl
{
    private readonly HavenEventBus _bus;
    private readonly IDashboardRepository _dashboard;
    
    public HomePage(HavenEventBus bus, IDashboardRepository dashboard)
    {
        _bus = bus;
        _dashboard = dashboard;
        InitializeComponent();
        WireEvents();
        _ = LoadTilesAsync();
    }
    
    private void WireEvents()
    {
        _bus.RegisterElement("Home.Header.RefreshClick", RefreshButton);
        _bus.WirePointerEvents("Home.Header.RefreshClick", RefreshButton);
        RefreshButton.Click += async (_, _) =>
        {
            _bus.Fire("Home.Header.RefreshClick");
            await LoadTilesAsync();
        };
    }
}
```

### Adapter Pages (ViewModel Wrapper)

Complex pages that haven't been fully migrated use an adapter pattern:

```csharp
// NotesPage.axaml.cs
public partial class NotesPage : UserControl
{
    private readonly NotesWorkspaceView _workspace;
    
    public NotesPage(HavenEventBus bus, INotesRepository repository, ...)
    {
        // Create ViewModel internally
        var viewModel = new NotesPage(repository, ...);
        
        // Wrap existing view
        _workspace = new NotesWorkspaceView(viewModel);
        Content = _workspace;
        
        // Wire events
        bus.RegisterElement("Notes.View", _workspace);
        bus.WirePointerEvents("Notes.View", _workspace);
    }
}
```

## DI Container

Services are registered in `App.axaml.cs`:

```csharp
collection.AddSingleton<HavenEventBus>();
collection.AddSingleton<INotesRepository, NotesRepository>();
collection.AddSingleton<IOllamaClient, OllamaClient>();
// ... etc
```

Pages receive dependencies via constructor injection:

```csharp
public HomePage(HavenEventBus bus, IDashboardRepository dashboard) { ... }
```

## Naming Conventions

### Element Names
Format: `Page.Section.Name.Event`

Examples:
- `Home.Dashboard.Tile0.Click`
- `TopRail.Actions.Hover`
- `Chat.Composer.SendClick`
- `Plan.Tasks.Item0.Complete`

### Event Tokens
Generated by proxy classes:
```csharp
Home.Dashboard.Tile(0).Click()  // Returns EventToken
TopRail.Actions.Hover()          // Returns EventToken
Chat.Messages.Message(1).Copy()  // Returns EventToken
```

## Key Patterns

### 1. No Bindings
All UI is built programmatically or via AXAML with `x:Name`. No `{Binding}` expressions.

### 2. Event-Driven Communication
Components communicate through the event bus, not direct references.

### 3. Constructor Injection
All dependencies are injected via constructors. No service locator pattern (except for legacy code).

### 4. Code-Behind Logic
Business logic lives in `.axaml.cs` files, not in separate ViewModel classes.

### 5. Named Elements
All interactive elements have `x:Name` attributes for event bus registration.
