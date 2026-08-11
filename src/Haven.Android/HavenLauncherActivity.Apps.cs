using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private async void LoadAppsAsync()
    {
        if (_launcherStatus is not null)
            _launcherStatus.Text = "Loading apps…";
        try
        {
            var apps = await Task.Run(QueryApps);
            _apps.Clear();
            _apps.AddRange(ApplySavedOrder(apps));
            _page = Math.Clamp(_page, 0, Math.Max(0, PageCount - 1));
            if (_launcherStatus is not null)
                _launcherStatus.Text = _apps.Count == 0
                    ? "No launchable apps were returned by Android. Open launcher settings or retry."
                    : $"{_apps.Count} apps";
            if (_grid is not null)
                _grid.Post(RenderPage);
            else
                RenderPage();
        }
        catch (Exception ex)
        {
            if (_launcherStatus is not null)
                _launcherStatus.Text = "Could not load apps: " + ex.Message;
            Toast.MakeText(this, "Could not load apps", ToastLength.Long)?.Show();
        }
    }
    private IReadOnlyList<LauncherApp> QueryApps()
    {
        var manager = PackageManager;
        if (manager is null)
            return [];

        var intent = new Intent(Intent.ActionMain);
        intent.AddCategory(Intent.CategoryLauncher);

#pragma warning disable CA1422
        var results = manager.QueryIntentActivities(intent, PackageInfoFlags.MatchAll);
#pragma warning restore CA1422

        var apps = results
            .Where(result => result.ActivityInfo?.PackageName is { Length: > 0 }
                && result.ActivityInfo?.Name is { Length: > 0 })
            .Select(result =>
            {
                var info = result.ActivityInfo!;
                var packageName = info.PackageName!;
                var activityName = info.Name!;
                var label = result.LoadLabel(manager)?.ToString();
                return new LauncherApp(
                    string.IsNullOrWhiteSpace(label) ? packageName : label,
                    packageName,
                    activityName,
                    result.LoadIcon(manager));
            })
            .DistinctBy(app => app.Key)
            .OrderBy(app => app.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (!apps.Any(app => string.Equals(app.PackageName, PackageName, StringComparison.OrdinalIgnoreCase)))
        {
            apps.Insert(0, new LauncherApp(
                "Haven",
                PackageName!,
                typeof(AndroidBootstrapActivity).FullName!,
                ApplicationInfo?.LoadIcon(manager)));
        }

        return apps;
    }

    private IReadOnlyList<LauncherApp> ApplySavedOrder(IReadOnlyList<LauncherApp> apps)
    {
        var order = (Preferences.GetString(OrderKey, string.Empty) ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select((key, index) => (key, index))
            .ToDictionary(item => item.key, item => item.index, StringComparer.Ordinal);

        return apps
            .OrderBy(app => order.TryGetValue(app.Key, out var index) ? index : int.MaxValue)
            .ThenBy(app => app.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private void RenderPage()
    {
        if (_grid is null || _pageIndicator is null)
            return;

        var rows = Math.Clamp(Preferences.GetInt(RowsKey, 5), 3, 8);
        var columns = Math.Clamp(Preferences.GetInt(ColumnsKey, 4), 3, 7);
        var perPage = rows * columns;
        var visible = _apps.Skip(_page * perPage).Take(perPage).ToArray();

        _grid.RemoveAllViews();
        if (_apps.Count == 0)
        {
            _pageIndicator.Text = "Tap All apps to retry";
            return;
        }
        _grid.RowCount = rows;
        _grid.ColumnCount = columns;

        var displayWidth = Resources?.DisplayMetrics?.WidthPixels ?? 1080;
        var cellWidth = Math.Max(Dp(64), (displayWidth - Dp(24)) / columns);
        var cellHeight = Math.Max(Dp(78), ((_grid.Height > 0 ? _grid.Height : Dp(540))) / rows);

        foreach (var app in visible)
            _grid.AddView(BuildAppTile(app, cellWidth, cellHeight));

        _pageIndicator.Text = PageCount <= 1
            ? "Swipe up for apps"
            : $"{_page + 1} / {PageCount}  •  Swipe up for apps";
        AndroidTypography.ApplyTree(_grid);
    }

    private View BuildAppTile(LauncherApp app, int width, int height)
    {
        var tile = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            ContentDescription = $"{app.Label}, {app.PackageName}",
            LayoutParameters = new ViewGroup.LayoutParams(width, height)
        };
        tile.SetGravity(GravityFlags.Center);
        tile.SetPadding(Dp(4), Dp(4), Dp(4), Dp(4));

        var icon = new ImageView(this)
        {
            LayoutParameters = new LinearLayout.LayoutParams(Dp(52), Dp(52))
        };
        icon.SetImageDrawable(app.Icon);
        tile.AddView(icon);

        if (Preferences.GetBoolean(LabelsKey, true))
        {
            var label = new TextView(this)
            {
                Text = app.Label,
                Gravity = GravityFlags.Center,
                TextSize = 12
            };
            label.SetMaxLines(1);
            label.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
            label.SetTextColor(Color.White);
            tile.AddView(label);
        }

        if (Preferences.GetBoolean(PackagesKey, false))
        {
            var package = new TextView(this)
            {
                Text = app.PackageName,
                Gravity = GravityFlags.Center,
                TextSize = 8
            };
            package.SetMaxLines(1);
            package.Ellipsize = global::Android.Text.TextUtils.TruncateAt.Middle;
            package.SetTextColor(Color.Argb(210, 230, 220, 255));
            tile.AddView(package);
        }

        tile.LongClick += (_, args) =>
        {
            _movingKey = app.Key;
            tile.Background = RoundedBackground(Color.Argb(120, 176, 116, 255), Dp(18));
            Toast.MakeText(this, "Tap another app to move it here", ToastLength.Short)?.Show();
            args.Handled = true;
        };
        tile.Click += (_, _) =>
        {
            if (_movingKey is not null)
            {
                MoveApp(_movingKey, app.Key);
                _movingKey = null;
                return;
            }

            LaunchApp(app);
        };
        return tile;
    }

    private void MoveApp(string sourceKey, string destinationKey)
    {
        var source = _apps.FindIndex(app => app.Key == sourceKey);
        var destination = _apps.FindIndex(app => app.Key == destinationKey);
        if (source < 0 || destination < 0 || source == destination)
            return;

        var moving = _apps[source];
        _apps.RemoveAt(source);
        if (source < destination)
            destination--;
        _apps.Insert(destination, moving);
        SaveOrder();
        RenderPage();
    }

    private void SaveOrder()
    {
        Preferences.Edit()?
            .PutString(OrderKey, string.Join('|', _apps.Select(app => app.Key)))?
            .Apply();
    }

    private int PageCount
    {
        get
        {
            var rows = Math.Clamp(Preferences.GetInt(RowsKey, 5), 3, 8);
            var columns = Math.Clamp(Preferences.GetInt(ColumnsKey, 4), 3, 7);
            return Math.Max(1, (int)Math.Ceiling(_apps.Count / (double)(rows * columns)));
        }
    }

    private void ChangePage(int delta)
    {
        var next = Math.Clamp(_page + delta, 0, Math.Max(0, PageCount - 1));
        if (next == _page)
            return;
        _page = next;
        RenderPage();
    }

    private void ShowAppDrawer()
    {
        var dialog = new Dialog(this);
        var shell = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
        };
        shell.SetPadding(Dp(12), Dp(16), Dp(12), Dp(12));
        shell.Background = HavenNativeSurface.Page();

        var header = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };
        header.SetGravity(GravityFlags.CenterVertical);
        var title = new TextView(this)
        {
            Text = "All apps",
            TextSize = 22,
            Typeface = Typeface.DefaultBold,
            LayoutParameters = new LinearLayout.LayoutParams(0, Dp(52), 1f),
            Gravity = GravityFlags.CenterVertical
        };
        title.SetTextColor(Color.White);
        header.AddView(title);
        header.AddView(IconButton(
            SystemDrawable("ic_menu_preferences"),
            "Launcher settings",
            () =>
            {
                dialog.Dismiss();
                ShowLauncherSettings();
            }));
        header.AddView(IconButton(
            SystemDrawable("ic_menu_close_clear_cancel"),
            "Close app drawer",
            dialog.Dismiss));
        shell.AddView(header);

        var search = new EditText(this)
        {
            Hint = "Search installed apps",
            TextSize = 15,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                Dp(48))
        };
        search.SetSingleLine(true);
        search.SetTextColor(Color.White);
        search.SetHintTextColor(Color.Argb(180, 235, 225, 255));
        search.Background = RoundedBackground(Color.Argb(70, 255, 255, 255), Dp(18));
        search.SetPadding(Dp(16), 0, Dp(16), 0);
        shell.AddView(search);

        var scroll = new ScrollView(this)
        {
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                0,
                1f)
        };
        var grid = new GridLayout(this)
        {
            ColumnCount = Math.Clamp(Preferences.GetInt(ColumnsKey, 4), 3, 7)
        };
        var width = (Resources?.DisplayMetrics?.WidthPixels ?? 1080) / grid.ColumnCount;
        void RenderMatches(string? query)
        {
            grid.RemoveAllViews();
            var normalized = query?.Trim() ?? string.Empty;
            var matches = string.IsNullOrWhiteSpace(normalized)
                ? _apps.ToArray()
                : _apps.Where(app => app.Label.Contains(normalized, StringComparison.CurrentCultureIgnoreCase)
                    || app.PackageName.Contains(normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var app in matches)
                grid.AddView(BuildAppTile(app, width, Dp(96)));
            if (matches.Length == 0)
            {
                var empty = new TextView(this)
                {
                    Text = "No installed apps match this search.",
                    Gravity = GravityFlags.Center,
                    LayoutParameters = new ViewGroup.LayoutParams(
                        ViewGroup.LayoutParams.MatchParent,
                        Dp(72))
                };
                empty.SetTextColor(Color.Argb(220, 235, 225, 255));
                grid.AddView(empty);
            }
        }

        search.TextChanged += (_, args) => RenderMatches(args.Text?.ToString());
        RenderMatches(string.Empty);
        scroll.AddView(grid);
        shell.AddView(scroll);

        dialog.SetContentView(shell);
        AndroidTypography.ApplyTree(shell);
        dialog.Show();
        dialog.Window?.SetLayout(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent);
    }

    private void LaunchApp(LauncherApp app)
    {
        try
        {
            var intent = new Intent(Intent.ActionMain);
            intent.AddCategory(Intent.CategoryLauncher);
            intent.SetClassName(app.PackageName, app.ActivityName);
            intent.AddFlags(ActivityFlags.NewTask);
            StartActivity(intent);
        }
        catch (Exception exception)
        {
            Toast.MakeText(this, $"Could not open {app.Label}: {exception.Message}", ToastLength.Long)?.Show();
        }
    }
}
