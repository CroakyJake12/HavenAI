using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace Haven.Android;

[Activity(
    Label = "Haven Launcher",
    Theme = "@style/Theme.AppCompat.Light.NoActionBar",
    Icon = "@drawable/haven_icon",
    Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    ExcludeFromRecents = true,
    ConfigurationChanges =
        ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.ScreenLayout
        | ConfigChanges.UiMode
        | ConfigChanges.Density)]
[IntentFilter(
    new[] { Intent.ActionMain },
    Categories = new[] { Intent.CategoryHome, Intent.CategoryDefault })]
public sealed partial class HavenLauncherActivity : Activity
{
    private const string PreferenceName = "haven_launcher";
    private const string OrderKey = "app_order";
    private const string RowsKey = "rows";
    private const string ColumnsKey = "columns";
    private const string LabelsKey = "labels";
    private const string PackagesKey = "packages";
    private const string HavenWidgetKey = "haven_widget";
    private const string WidgetIdsKey = "widget_ids";
    private const int WidgetHostId = 0x48415645;
    private const int PickWidgetRequest = 8101;
    private const int ConfigureWidgetRequest = 8102;

    private readonly List<LauncherApp> _apps = [];
    private LinearLayout? _root;
    private LinearLayout? _widgetStrip;
    private GridLayout? _grid;
    private TextView? _pageIndicator;
    private TextView? _launcherStatus;
    private AppWidgetHost? _widgetHost;
    private AppWidgetManager? _widgetManager;
    private int _page;
    private string? _movingKey;
    private int _pendingWidgetId = AppWidgetManager.InvalidAppwidgetId;

    private ISharedPreferences Preferences
        => GetSharedPreferences(PreferenceName, FileCreationMode.Private)!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (!OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            Window?.SetStatusBarColor(Color.Transparent);
            Window?.SetNavigationBarColor(Color.Rgb(24, 18, 38));
        }

        _widgetHost = new AppWidgetHost(this, WidgetHostId);
        _widgetManager = AppWidgetManager.GetInstance(this);

        BuildSurface();
        _root?.Post(LoadAppsAsync);
    }

    protected override void OnStart()
    {
        base.OnStart();
        try
        {
            _widgetHost?.StartListening();
        }
        catch
        {
            // Keep the launcher usable when a widget provider rejects hosting.
        }
    }

    protected override void OnStop()
    {
        try
        {
            _widgetHost?.StopListening();
        }
        finally
        {
            base.OnStop();
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        ApplyWallpaper();
        RenderWidgets();
    }

    public override void OnBackPressed()
    {
        if (_page != 0)
        {
            _page = 0;
            RenderPage();
            return;
        }

        // The launcher owns the root back destination.
        return;
    }

    private void BuildSurface()
    {
        _root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
        };
        _root.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
        _root.Focusable = true;
        _root.Clickable = true;
        _root.SetOnTouchListener(new SwipeTouchListener(
            onSwipeUp: ShowAppDrawer,
            onSwipeLeft: () => ChangePage(1),
            onSwipeRight: () => ChangePage(-1)));

        _widgetStrip = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
        };
        var widgetScroll = new HorizontalScrollView(this)
        {
            HorizontalScrollBarEnabled = false,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
        };
        widgetScroll.AddView(_widgetStrip);
        _root.AddView(widgetScroll);

        _pageIndicator = new TextView(this)
        {
            Gravity = GravityFlags.Center,
            TextSize = 12,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                Dp(28))
        };
        _pageIndicator.SetTextColor(Color.White);
        _root.AddView(_pageIndicator);
        _launcherStatus = new TextView(this)
        {
            Text = "Loading apps…",
            TextSize = 12,
            Gravity = GravityFlags.Center,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
        };
        _launcherStatus.SetTextColor(Color.Argb(220, 235, 225, 255));
        _launcherStatus.SetPadding(0, 0, 0, Dp(4));
        _root.AddView(_launcherStatus);

        _grid = new GridLayout(this)
        {
            UseDefaultMargins = false,
            AlignmentMode = GridAlign.Bounds,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                0,
                1f)
        };
        _root.AddView(_grid);

        _root.AddView(BuildBottomBar());
        SetContentView(_root);
        ApplyWallpaper();
        RenderWidgets();
    }

    private View BuildBottomBar()
    {
        var row = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                Dp(66))
        };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(Dp(7), Dp(7), Dp(7), Dp(7));
        row.Background = MagicalBackground(Dp(28));

        row.AddView(IconButton(
            SystemDrawable("ic_menu_view"),
            "All apps",
            ShowAppDrawer));
        row.AddView(IconButton(
            Android.Resource.Drawable.IcMenuManage,
            "Open Haven Go",
            OpenHavenDashboard));

        row.AddView(IconButton(
            SystemDrawable("ic_menu_preferences"),
            "Launcher settings",
            ShowLauncherSettings));

        var go = new EditText(this)
        {
            Hint = "Go — ask Haven",
            TextSize = 15,
            LayoutParameters = new LinearLayout.LayoutParams(0, Dp(50), 1f)
            {
                LeftMargin = Dp(6),
                RightMargin = Dp(6)
            }
        };
        go.SetSingleLine(true);
        go.SetTextColor(Color.White);
        go.SetHintTextColor(Color.Argb(190, 255, 255, 255));
        go.SetPadding(Dp(16), 0, Dp(12), 0);
        go.Background = RoundedBackground(Color.Argb(90, 255, 255, 255), Dp(24));
        go.EditorAction += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(go.Text))
            {
                OpenHavenChat(go.Text!);
                go.Text = string.Empty;
                args.Handled = true;
            }
        };
        row.AddView(go);

        row.AddView(IconButton(
            SystemDrawable("ic_menu_send"),
            "Send to Haven",
            () =>
            {
                if (!string.IsNullOrWhiteSpace(go.Text))
                {
                    OpenHavenChat(go.Text!);
                    go.Text = string.Empty;
                }
            }));

        return row;
    }

    private int SystemDrawable(string name)
        => Resources?.GetIdentifier(name, "drawable", "android") ?? 0;

    private ImageButton IconButton(int resource, string description, Action action)
    {
        var button = new ImageButton(this)
        {
            ContentDescription = description,
            LayoutParameters = new LinearLayout.LayoutParams(Dp(48), Dp(48))
            {
                LeftMargin = Dp(2),
                RightMargin = Dp(2)
            }
        };
        button.SetImageResource(resource);
        button.SetColorFilter(Color.White);
        button.SetBackgroundColor(Color.Transparent);
        button.Click += (_, _) => action();
        return button;
    }
}
