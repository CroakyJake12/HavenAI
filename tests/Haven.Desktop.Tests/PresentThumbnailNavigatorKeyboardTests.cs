using Haven.Core;
using Haven.Desktop.Views.Pages.Present;
using Haven.UI;

namespace Haven.Desktop.Tests;

public sealed class PresentThumbnailNavigatorKeyboardTests
{
    [Fact]
    public void Keyboard_selects_and_reorders_slide_thumbnails()
    {
        var document = PresentDocument.Create("Keyboard deck");
        document.Slides.Add(PresentSlide.Create(1));
        document.Slides.Add(PresentSlide.Create(2));
        document.Normalize();
        var navigator = new PresentThumbnailNavigator();
        navigator.SetDocument(document, 0);
        var selected = -1;
        (int From, int To)? reorder = null;
        navigator.SlideSelected += index => selected = index;
        navigator.SlideReorderRequested += (from, to) => reorder = (from, to);

        Assert.True(navigator.KeyDown(new HavenKeyInput(HavenKey.Down, HavenKeyModifiers.None)));
        Assert.Equal(1, selected);
        Assert.Equal(1, navigator.SelectedIndex);
        Assert.True(navigator.KeyDown(new HavenKeyInput(HavenKey.Down, HavenKeyModifiers.Control)));
        Assert.Equal((1, 2), reorder);
        Assert.Equal(2, navigator.SelectedIndex);
    }
}
