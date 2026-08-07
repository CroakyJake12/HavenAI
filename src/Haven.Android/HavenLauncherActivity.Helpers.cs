using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private GradientDrawable MagicalBackground(int radius)
    {
        var background = new GradientDrawable();
        background.SetColor(Color.Argb(225, 89, 48, 145));
        background.SetStroke(Dp(1), Color.Argb(210, 213, 165, 255));
        background.SetCornerRadius(radius);
        return background;
    }

    private static GradientDrawable RoundedBackground(Color color, int radius)
    {
        var background = new GradientDrawable();
        background.SetColor(color);
        background.SetCornerRadius(radius);
        return background;
    }

    private int Dp(int value)
        => (int)Math.Round(value * (Resources?.DisplayMetrics?.Density ?? 1f));

    private sealed record LauncherApp(
        string Label,
        string PackageName,
        string ActivityName,
        Drawable? Icon)
    {
        public string Key => PackageName + "/" + ActivityName;
    }

    private sealed class SwipeTouchListener(
        Action onSwipeUp,
        Action onSwipeLeft,
        Action onSwipeRight) : Java.Lang.Object, View.IOnTouchListener
    {
        private float _downX;
        private float _downY;

        public bool OnTouch(View? view, MotionEvent? e)
        {
            if (e is null)
                return false;

            if (e.Action == MotionEventActions.Down)
            {
                _downX = e.RawX;
                _downY = e.RawY;
                return true;
            }

            if (e.Action != MotionEventActions.Up)
                return true;

            var deltaX = e.RawX - _downX;
            var deltaY = e.RawY - _downY;
            if (Math.Abs(deltaY) > Math.Abs(deltaX) && deltaY < -80)
                onSwipeUp();
            else if (Math.Abs(deltaX) > 80)
            {
                if (deltaX < 0)
                    onSwipeLeft();
                else
                    onSwipeRight();
            }
            else
            {
                view?.PerformClick();
            }

            return true;
        }
    }
}
