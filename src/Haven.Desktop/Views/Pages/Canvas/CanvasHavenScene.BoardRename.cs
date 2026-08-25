using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Pages.Canvas;

internal sealed partial class CanvasHavenScene
{
    private void BeginBoardRename(int index)
    {
        if (index < 0 || index >= _boardTitles.Count) return;
        EnsureBoardRenamePanel();
        _openPopup?.Dismiss();
        HidePanels();
        _renameBoardIndex = index;
        _renameBoardInput.Text = _boardTitles[index];
        _renameBoardPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
    }

    private void EnsureBoardRenamePanel()
    {
        if (_renameBoardPanel is not null) return;

        _renameBoardPanel = FloatingCard("Canvas.Release.BoardRename");
        _renameBoardPanel.Layout = HavenLayout.Vertical;
        _renameBoardPanel.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Start);
        _renameBoardPanel.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.End);
        _renameBoardPanel.SetValue(HavenProperties.Margin, HavenThickness.Parse("12px 0px 0px 78px"));
        _renameBoardPanel.SetValue(HavenProperties.Width, HavenLength.Px(320));
        _renameBoardPanel.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        _renameBoardPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        _renameBoardPanel.Add(Caption("Rename board"));

        _renameBoardInput = NewInput("Canvas.Release.BoardRenameInput", "Board name", "Board name");
        _renameBoardPanel.Add(_renameBoardInput);

        var actions = new Container { Name = "Canvas.Release.BoardRenameActions", Layout = HavenLayout.Horizontal };
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        var save = CompactButton("Canvas.Release.BoardRenameSave", "Rename", "Rename board");
        save.Invoked += (_, _) => CommitBoardRename();
        var cancel = CompactButton("Canvas.Release.BoardRenameCancel", "Cancel", "Cancel board rename");
        cancel.Invoked += (_, _) => CloseBoardRename();
        actions.Add(save);
        actions.Add(cancel);
        _renameBoardPanel.Add(actions);
        _releaseChrome.Add(_renameBoardPanel);
    }

    private void CommitBoardRename()
    {
        if (_renameBoardIndex < 0) return;
        var title = _renameBoardInput.Text.Trim();
        if (title.Length == 0) return;
        var index = _renameBoardIndex;
        CloseBoardRename();
        BoardRenameRequested?.Invoke(index, title);
    }

    private void CloseBoardRename()
    {
        _renameBoardIndex = -1;
        if (_renameBoardPanel is not null)
            _renameBoardPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
    }
}
