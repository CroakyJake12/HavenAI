using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private void ShowLauncherSettings()
    {
        var container = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        container.SetPadding(Dp(18), Dp(8), Dp(18), 0);

        var rows = new NumberPicker(this)
        {
            MinValue = 3,
            MaxValue = 8,
            Value = Math.Clamp(Preferences.GetInt(RowsKey, 5), 3, 8)
        };
        var columns = new NumberPicker(this)
        {
            MinValue = 3,
            MaxValue = 7,
            Value = Math.Clamp(Preferences.GetInt(ColumnsKey, 4), 3, 7)
        };
        var labels = new HavenNativeCheckBox(this)
        {
            Text = "Show app labels",
            Checked = Preferences.GetBoolean(LabelsKey, true)
        };
        var packages = new HavenNativeCheckBox(this)
        {
            Text = "Show package names",
            Checked = Preferences.GetBoolean(PackagesKey, false)
        };
        container.AddView(LabeledControl("Rows", rows));
        container.AddView(LabeledControl("Columns", columns));
        container.AddView(labels);
        container.AddView(packages);

        var dialog = new AlertDialog.Builder(this);
        dialog.SetTitle("Haven Launcher");
        dialog.SetView(container);
        dialog.SetPositiveButton("Save", (_, _) =>
        {
            var editor = Preferences.Edit();
            if (editor is not null)
            {
                editor.PutInt(RowsKey, rows.Value);
                editor.PutInt(ColumnsKey, columns.Value);
                editor.PutBoolean(LabelsKey, labels.Checked);
                editor.PutBoolean(PackagesKey, packages.Checked);
                editor.Apply();
            }

            _page = 0;
            RenderPage();
        });
        dialog.SetNeutralButton("Wallpaper", (_, _) => ChooseWallpaper());
        dialog.SetNegativeButton("Widgets", (_, _) => ShowWidgetMenu());
        dialog.Show();
    }

    private View LabeledControl(string label, View control)
    {
        var row = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };
        row.SetGravity(GravityFlags.CenterVertical);
        var text = new TextView(this)
        {
            Text = label,
            LayoutParameters = new LinearLayout.LayoutParams(0, Dp(56), 1f),
            Gravity = GravityFlags.CenterVertical
        };
        row.AddView(text);
        row.AddView(control);
        return row;
    }

    private void ChooseWallpaper()
    {
        var intent = new Intent(Intent.ActionSetWallpaper);
        StartActivity(Intent.CreateChooser(intent, "Choose launcher wallpaper"));
    }

    private void ShowWidgetMenu()
    {
        var dialog = new AlertDialog.Builder(this);
        dialog.SetTitle("Add widget");
        dialog.SetItems(
            new[] { "Android widget", "Haven clock widget" },
            (_, args) =>
            {
                if (args.Which == 0)
                    PickAndroidWidget();
                else
                    AddHavenWidget();
            });
        dialog.Show();
    }

    private void PickAndroidWidget()
    {
        var widgetHost = _widgetHost;
        if (widgetHost is null)
        {
            Toast.MakeText(this, "Android widgets are unavailable right now.", ToastLength.Long)?.Show();
            return;
        }

        try
        {
            _pendingWidgetId = widgetHost.AllocateAppWidgetId();
            var intent = new Intent(AppWidgetManager.ActionAppwidgetPick);
            intent.PutExtra(AppWidgetManager.ExtraAppwidgetId, _pendingWidgetId);
            StartActivityForResult(intent, PickWidgetRequest);
        }
        catch (Exception exception)
        {
            var failedWidgetId = _pendingWidgetId;
            _pendingWidgetId = AppWidgetManager.InvalidAppwidgetId;
            DeleteWidgetId(failedWidgetId);
            global::Android.Util.Log.Warn(
                "HavenLauncher",
                "Could not open the Android widget picker: " + exception.Message);
            Toast.MakeText(this, "Could not open the Android widget picker.", ToastLength.Long)?.Show();
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        var widgetId = data?.GetIntExtra(
            AppWidgetManager.ExtraAppwidgetId,
            _pendingWidgetId) ?? _pendingWidgetId;

        if (requestCode == PickWidgetRequest)
        {
            if (resultCode != Result.Ok || widgetId == AppWidgetManager.InvalidAppwidgetId)
            {
                DeleteWidgetId(widgetId);
                return;
            }

            var info = _widgetManager?.GetAppWidgetInfo(widgetId);
            if (info?.Configure is not null)
            {
                var configure = new Intent(AppWidgetManager.ActionAppwidgetConfigure);
                configure.SetComponent(info.Configure);
                configure.PutExtra(AppWidgetManager.ExtraAppwidgetId, widgetId);
                StartActivityForResult(configure, ConfigureWidgetRequest);
                return;
            }

            SaveWidgetId(widgetId);
            RenderWidgets();
        }
        else if (requestCode == ConfigureWidgetRequest)
        {
            if (resultCode == Result.Ok)
            {
                SaveWidgetId(widgetId);
                RenderWidgets();
            }
            else
            {
                DeleteWidgetId(widgetId);
            }
        }
    }

    private void AddHavenWidget()
    {
        Preferences.Edit()?.PutBoolean(HavenWidgetKey, true)?.Apply();
        RenderWidgets();
    }

    private void RenderBaseWidgets()
    {
        var widgetStrip = _widgetStrip;
        if (widgetStrip is null)
            return;

        widgetStrip.RemoveAllViews();

        if (Preferences.GetBoolean(HavenWidgetKey, false))
        {
            var clock = new TextClock(this)
            {
                Format12Hour = "EEE, MMM d  •  h:mm a",
                Format24Hour = "EEE, MMM d  •  HH:mm",
                TextSize = 18,
                Gravity = GravityFlags.Center,
                LayoutParameters = new LinearLayout.LayoutParams(Dp(260), Dp(70))
                {
                    RightMargin = Dp(8)
                }
            };
            clock.SetTextColor(Color.White);
            clock.Background = MagicalBackground(Dp(20));
            clock.LongClick += (_, args) =>
            {
                Preferences.Edit()?.PutBoolean(HavenWidgetKey, false)?.Apply();
                RenderWidgets();
                if (args is not null)
                    if (args is not null)
                    args.Handled = true;
            };
            widgetStrip.AddView(clock);
        }

        var widgetHost = _widgetHost;
        var widgetManager = _widgetManager;
        if (widgetHost is null || widgetManager is null)
            return;

        foreach (var widgetId in ReadWidgetIds().ToArray())
        {
            var info = widgetManager.GetAppWidgetInfo(widgetId);
            if (info is null)
            {
                DeleteWidgetId(widgetId);
                continue;
            }

            var hostView = widgetHost.CreateView(this, widgetId, info);
            if (hostView is null)
            {
                DeleteWidgetId(widgetId);
                continue;
            }
            hostView.SetAppWidget(widgetId, info);
            hostView.LayoutParameters = new LinearLayout.LayoutParams(Dp(300), Dp(160))
            {
                RightMargin = Dp(8)
            };
            hostView.LongClick += (_, args) =>
            {
                var dialog = new AlertDialog.Builder(this);
                dialog.SetMessage("Remove this widget?");
                dialog.SetPositiveButton("Remove", (_, _) =>
                {
                    DeleteWidgetId(widgetId);
                    RenderWidgets();
                });
                dialog.SetNegativeButton("Cancel", (_, _) => { });
                dialog.Show();
                if (args is not null)
                    if (args is not null)
                    args.Handled = true;
            };
            widgetStrip.AddView(hostView);
        }
    }

    private HashSet<int> ReadWidgetIds()
        => (Preferences!.GetString(WidgetIdsKey, string.Empty) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out var id)
                ? id
                : AppWidgetManager.InvalidAppwidgetId)
            .Where(id => id != AppWidgetManager.InvalidAppwidgetId)
            .ToHashSet();

    private void SaveWidgetId(int widgetId)
    {
        var ids = ReadWidgetIds();
        ids.Add(widgetId);
        Preferences.Edit()?
            .PutString(WidgetIdsKey, string.Join(',', ids.OrderBy(id => id)))?
            .Apply();
    }

    private void DeleteWidgetId(int widgetId)
    {
        if (widgetId == AppWidgetManager.InvalidAppwidgetId)
            return;

        var ids = ReadWidgetIds();
        ids.Remove(widgetId);
        Preferences.Edit()?
            .PutString(WidgetIdsKey, string.Join(',', ids.OrderBy(id => id)))?
            .Apply();

        try
        {
            _widgetHost?.DeleteAppWidgetId(widgetId);
        }
        catch
        {
        }
    }

    private void OpenHavenDashboard()
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        intent.PutExtra("haven_surface", "dashboard");
        StartActivity(intent);
    }

    private void OpenHavenChat(string prompt)
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        intent.PutExtra("haven_prompt", prompt);
        StartActivity(intent);
    }

    private void ApplyWallpaper()
    {
        if (_root is null)
            return;

        try
        {
            var drawable = WallpaperManager.GetInstance(this)?.Drawable;
            _root.Background = drawable ?? RoundedBackground(Color.Rgb(31, 24, 45), 0);
        }
        catch
        {
            _root.Background = HavenNativeSurface.Page();
        }
    }
}
