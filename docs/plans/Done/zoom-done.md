# Zoom of the session text (2026-11-16) (done items)

> Split from [zoom.md](../zoom.md) on 2026-11-21. These items are checked in the original; the open items stay there. The headings above each item are its place in that plan.

## Steps
- [x] Core `ZoomLevel` (steps, nearest, up and down, slider position, scale) and `ZoomKeys.FromVirtualKey`, with tests (`tests/.../Zoom/ZoomLevelTests.cs`).
- [x] `UiFile` and `ZoomStore`: `ui.json` with the zoom; the theme keeps the zoom.
- [x] Bottom-right control (`SessionWindow.xaml`, `SessionWindow.Zoom.cs`); the slider, the buttons and the percentage show the zoom.
- [x] Transcript zoom (`TranscriptView.ApplyZoom`). Checked on screen at 200%: the transcript text and the turn lines double in size; the rail stays the same.
- [x] Terminal zoom (`TerminalColours`, `ApplyZoomTo`). Checked on screen: the colours are right at 100%, and the text grew with four Ctrl+= presses.
- [x] Keys in the window (`OnPanePreviewKeyDown`). Checked as above.
