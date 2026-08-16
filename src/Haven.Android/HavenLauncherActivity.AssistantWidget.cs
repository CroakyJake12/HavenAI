using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private const string AssistantWidgetPreferenceKey = "assistant_widget";

    private void RenderWidgets()
    {
        RenderBaseWidgets();
        RenderPinnedAssistantWidget();
    }

    private void ToggleHavenAssistantWidgetPin()
    {
        var pinned = !Preferences.GetBoolean(AssistantWidgetPreferenceKey, false);
        Preferences.Edit()?.PutBoolean(AssistantWidgetPreferenceKey, pinned)?.Apply();
        RenderWidgets();
        Toast.MakeText(
            this,
            pinned ? "Haven Assistant pinned to the launcher." : "Haven Assistant unpinned.",
            ToastLength.Short)?.Show();
    }

    private void RenderPinnedAssistantWidget()
    {
        var widgetStrip = _widgetStrip;
        if (widgetStrip is null || !Preferences.GetBoolean(AssistantWidgetPreferenceKey, false))
            return;

        var assistant = new TextView(this)
        {
            Text = "Haven Assistant\nTap to open",
            TextSize = 18,
            Gravity = GravityFlags.Center,
            ContentDescription = "Open Haven Assistant",
            LayoutParameters = new LinearLayout.LayoutParams(Dp(260), Dp(90))
            {
                RightMargin = Dp(8)
            }
        };
        assistant.SetTextColor(Color.White);
        assistant.Background = MagicalBackground(Dp(20));
        assistant.Click += (_, _) => OpenHavenAssistant();
        assistant.LongClick += (_, args) =>
        {
            Preferences.Edit()?.PutBoolean(AssistantWidgetPreferenceKey, false)?.Apply();
            RenderWidgets();
            if (args is not null)
                args.Handled = true;
        };
        widgetStrip.AddView(assistant);
    }

    private void OpenHavenAssistant()
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        intent.PutExtra("haven_surface", "assistant");
        StartActivity(intent);
    }
}
