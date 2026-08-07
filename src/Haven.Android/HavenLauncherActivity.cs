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
            // A rejected widget provider must not take the launcher down.
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

        // Re-query launchable activities whenever the launcher comes to the
        // foreground so installs/uninstalls and a failed initial query recover.
        _root?.Post(LoadAppAsync);
    }

    public override void OnBackPressed()
    {
        if (_page != 0)
        {
            _page = 0;
            RenderPage();
        }
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
        AttachLauncherSwipes(_root);

        var top = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                Dp(54))
        };
        top.SetGravity(GravityFlags.CenterVertical);

        var title = new TextView(this)
        {
            Text = "Haven",
            TextSize = 20,
            Typeface = Typeface.DefaultBold,
            Gravity = GravityFlags.CenterVertical,
            LayoutParameters = new LinearLayout.LayoutParams(0, Dp(54), 1f)
        };
        title.SetTextColor(Color.White);
        top.AddView(title);
        top.AddView(LauncherTextButton("All apps", ShowAppDrawer));
        top.AddView(LauncherTextButton("Go", OpenHavenGo));
        _root.AddView(top);

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
            Text = "Loading aps…",
            Clickable = true,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                Dp(30))
        };
        _pageIndicator.SetTextColor(Color.White);
        _pageIndicator.Click += (_, _) => ShowAppDrawer();
        AttachLauncherSwipes(_pageIndicator);
        _root.AddView(_pageIndicator);

        _launcherStatus = new TextView(this)
        {
            Text = "Loading apps…",
            TextSize = 12,
            Gravity = GravityFlags.Center,
            Clickable = true,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
        };
        _launcherStatus.SetTextColor(Color.Argb(220, 235, 225, 255));
        _launcherStatus.SetTPadding(0, 0, 0, Dp(4));
        _launcherStatus.Click += (_, _) => LoadAppsAsync();
        _root.AddView(_launcherStatus);

        _grid = new GridLayout(this)
        {
            UseDefaultMargins = false,
            AlignmentMode = GridAlign.Bounds,
            Clickable = true,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                0,
                1f)
        };
        AttachLauncherSwipes(_grid);
        _root.AddView(_grid);

        _root.AddView(BuildBottomBar());
        SetContentView(_root);
        ApplyWallpaper();
        RenderWidgets();
    }

    private void AttachLauncherSwipes(View view)
        => view.SetOnTouchListener(new SwipeTouchListener(
            onSwipeUp: ShowAppDrawer,
            onSwipeLeft: () => ChangePage(1),
            onSwipeRight: () => ChangePage(-1)));

    private View BuildBottomBar()
    {
        var shell = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
        };
        shell.SetPadding(Dp(7), Dp(7), Dp(7), Dp(7));
        shell.Background = MagicalBackground(Dp(28));

        var controls = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                Dp(46))
        };
        controls.SetGravity(GravityFlags.CenterVertical);
        controls.AddView(LauncherTextButton("Apps", ShowAppDrawer));
        controls.AddView(LauncherTextButton("Go", OpenHavenGo));
        controls.AddView(LauncherTextButton("Settings", ShowLauncherSettings));
        shell.AddView(controls);

        var goRow = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                Dp(54))
        };
        goRow.SetGravity(GravityFlags.CenterVertical);

        var go = new EditText(this)
        {
            Hint = "Go — ask Haven",
            TextSize = 15,
            LayoutParameters = new LinearLayout.LayoutParams(0, Dp(48), 1f)
            {
                LeftMargin = Dp(4),
                RightMargin = Dp(4)
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
        goRow.AddView(go);
        goRow.AddView(LauncherTextButton("Send", () =>
        {
            if (!string.IsNullOrWhiteSpace(go.Text))
            {
                OpenHavenChat(go.Text!);
                go.Text = string.Empty;
            }
        }));
        shell.AddView(goRow);

        return shell;
    }

    private void OpenHavenGo()
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.PutExtra("haven_surface", "go");
        StartActivity(intent);
    }

    private Button LauncherTextButton(string text, Action action)
    {
        var button = new Button(this)
        {
            Text = text,
            TextSize = 12,
            AllCaps = false,
            MinWidth = 0,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                Dp(42))
            {
                LeftMargin = Dp(2),
                RightMargin = Dp(2)
            }
        };
        button.SetTextColor(Color.White);
        button.SetBackgroundColor(Color.Transparent);
        button.Click += (_, _) => action();
        return button;
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
        if (resource != 0)
            button.SetImageResource(resource);
        button.SetColorFilter(Color.White);
        button.SetBackgroundColor(Color.Transparent);
        button.Click += (_, _) => action();
        return button;
    }
}
