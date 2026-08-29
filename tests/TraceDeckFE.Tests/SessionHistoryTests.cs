using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TraceDeckFE.Models;
using TraceDeckFE.Services;

namespace TraceDeckFE.Tests;

public sealed class SessionHistoryTests
{
    private sealed class Editor
    {
        public ReferenceState Reference { get; } = new();
        public GuideState Guides { get; } = new();
        public ColorState Colors { get; } = new();
        public PaletteState Palette { get; } = new();
        public ProjectEditSnapshot Capture() => ProjectEditSnapshot.Capture(Reference,Guides,Colors,Palette);
        public void Apply(ProjectEditSnapshot state) => state.Apply(Reference,Guides,Colors,Palette,1920,1080);
        public Editor() => Reference.SetImage(Source(),1920,1080);
    }
    private static ReferenceImageSource Source()
    {
        var bitmap = BitmapSource.Create(640,360,96,96,PixelFormats.Bgra32,null,new byte[640*360*4],640*4); bitmap.Freeze();
        return new(Guid.NewGuid(),"tiny.png",null,"PNG",640,360,true,false,[1,2,3,4],bitmap);
    }
    [Theory]
    [InlineData("move")][InlineData("scale")][InlineData("rotation")][InlineData("flipH")][InlineData("flipV")]
    [InlineData("opacity")][InlineData("contrast")][InlineData("grayscale")][InlineData("visible")]
    [InlineData("grid")][InlineData("spacing")][InlineData("guideOpacity")][InlineData("horizontal")][InlineData("vertical")]
    public void ProjectEditing_UndoRedo(string action)
    {
        var e = new Editor(); var before = e.Capture(); var h = new SessionHistory<ProjectEditSnapshot>(before,ProjectEditSnapshot.Equivalent);
        switch(action)
        {
            case "move": e.Reference.MoveBy(37,-21); break;
            case "scale": e.Reference.ZoomAt(new(420,240),1.3); break;
            case "rotation": e.Reference.Rotation = 37; break;
            case "flipH": e.Reference.FlipHorizontal = true; break;
            case "flipV": e.Reference.FlipVertical = true; break;
            case "opacity": e.Reference.Opacity = .4; break;
            case "contrast": e.Reference.Contrast = 43; break;
            case "grayscale": e.Reference.IsGrayscale = true; break;
            case "visible": e.Reference.IsVisible = false; break;
            case "grid": e.Guides.IsGridVisible = true; break;
            case "spacing": e.Guides.GridSpacing = 48; break;
            case "guideOpacity": e.Guides.Opacity = .8; break;
            case "horizontal": e.Guides.IsHorizontalCenterVisible = true; break;
            case "vertical": e.Guides.IsVerticalCenterVisible = true; break;
        }
        var after = e.Capture(); h.Observe(after); Assert.True(h.IsDirty);
        e.Apply(h.Undo()); Assert.True(ProjectEditSnapshot.Equivalent(before,e.Capture())); Assert.False(h.IsDirty);
        e.Apply(h.Redo()); Assert.True(ProjectEditSnapshot.Equivalent(after,e.Capture()));
    }
    [Theory][InlineData("drag")][InlineData("contrast")][InlineData("opacity")]
    public void ContinuousGesture_IsOneAction(string kind)
    {
        var e = new Editor(); var before=e.Capture(); var h=new SessionHistory<ProjectEditSnapshot>(before,ProjectEditSnapshot.Equivalent);
        h.BeginGesture();
        for(var i=0;i<80;i++) { if(kind=="drag") e.Reference.MoveBy(1,1); else if(kind=="contrast") e.Reference.Contrast=i; else e.Reference.Opacity=.62+i*.002;
            h.Observe(e.Capture()); }
        h.EndGesture(); Assert.Equal(1,h.UndoCount); Assert.True(ProjectEditSnapshot.Equivalent(before,h.Undo()));
    }
    [Theory][InlineData("wheel")][InlineData("arrows")]
    public void RepeatedInput_CommitsAfterFourHundredMilliseconds(string key)
    {
        var h=new SessionHistory<int>(0); var time=DateTimeOffset.UtcNow;
        for(var i=1;i<=20;i++) { h.TouchBurst(key,time.AddMilliseconds(i*20)); h.Observe(i); }
        h.CompleteBurst(time.AddMilliseconds(750)); Assert.True(h.IsGrouping);
        h.CompleteBurst(time.AddMilliseconds(800)); Assert.False(h.IsGrouping); Assert.Equal(1,h.UndoCount); Assert.Equal(0,h.Undo());
    }
    [Fact] public void NewEditAfterUndo_InvalidatesRedo()
    { var h=new SessionHistory<int>(0); h.Observe(1);h.Observe(2);h.Undo();h.Observe(3);Assert.False(h.CanRedo);Assert.Equal(1,h.Undo()); }
    [Fact] public void SavedState_Edit_Undo_IsClean()
    { var h=new SessionHistory<int>(0); h.Observe(5);h.MarkSaved(5);h.Observe(8);Assert.True(h.IsDirty);h.Undo();Assert.False(h.IsDirty); }
    [Fact] public void SaveDuringNewerEdits_KeepsDirty()
    { var h=new SessionHistory<int>(0); h.Observe(1);var captured=h.Current;h.Observe(2);h.MarkSaved(captured);Assert.True(h.IsDirty);h.Undo();Assert.False(h.IsDirty); }
    [Fact] public void History_IsBounded()
    { var h=new SessionHistory<int>(0);for(var i=1;i<=200;i++)h.Observe(i);Assert.Equal(100,h.UndoCount);for(var i=0;i<100;i++)h.Undo();Assert.Equal(100,h.Current);Assert.False(h.CanUndo); }
    [Fact] public void Replacement_ReusesImmutableSources()
    {
        var e=new Editor();var initial=e.Capture();var h=new SessionHistory<ProjectEditSnapshot>(initial,ProjectEditSnapshot.Equivalent);
        e.Reference.SetImage(Source(),1920,1080);var replacement=e.Capture();h.Observe(replacement);
        Assert.Same(initial.Source,h.Undo().Source);Assert.Same(replacement.Source,h.Redo().Source);
        Assert.Same(replacement.Source!.OriginalBytes,h.Current.Source!.OriginalBytes);
    }
    [Fact] public void ResetTransform_UndoRestoresCombinedTransform()
    {
        var e=new Editor();e.Reference.Rotation=35;e.Reference.FlipHorizontal=true;e.Reference.ZoomAt(new(100,100),1.4);
        var before=e.Capture();var h=new SessionHistory<ProjectEditSnapshot>(before,ProjectEditSnapshot.Equivalent);
        e.Reference.ResetTransform();h.Observe(e.Capture());e.Apply(h.Undo());Assert.True(ProjectEditSnapshot.Equivalent(before,e.Capture()));
    }
    [Fact] public void ResizeAndMagnifierPreference_DoNotCreateEdits()
    {
        var e=new Editor();var h=new SessionHistory<ProjectEditSnapshot>(e.Capture(),ProjectEditSnapshot.Equivalent);
        e.Reference.UpdateViewport(900,500);e.Colors.MagnifierEnabled=false;h.Observe(e.Capture());Assert.False(h.CanUndo);Assert.False(h.IsDirty);
    }
    [Fact]
    public void TenThousandResizes_PreserveCanonicalPlacementAndBothHistoryStacks()
    {
        var editor = new Editor();
        editor.Reference.Rotation = 73;
        editor.Reference.FlipHorizontal = true;
        editor.Reference.FlipVertical = true;
        editor.Reference.ZoomAt(new(930, 520), .83);
        var history = new SessionHistory<ProjectEditSnapshot>(editor.Capture(), ProjectEditSnapshot.Equivalent);
        editor.Reference.MoveBy(81.25, -47.5);
        history.Observe(editor.Capture());
        editor.Reference.MoveBy(19, 23);
        var redoState = editor.Capture();
        history.Observe(redoState);
        editor.Apply(history.Undo());
        history.MarkSaved(history.Current);
        var transform = editor.Reference.VisualTransform;
        var canonical = editor.Reference.NormalizedTransform;
        var source = editor.Reference.Source;
        (int Width, int Height)[] viewports = [(960, 540), (1366, 768), (3440, 1440), (900, 700), (1920, 1080)];
        for (var iteration = 0; iteration < 10000; iteration++)
        {
            var viewport = viewports[iteration % viewports.Length];
            editor.Reference.UpdateViewport(viewport.Width, viewport.Height);
            history.Observe(editor.Capture());
            Assert.Equal(canonical, editor.Reference.NormalizedTransform);
            Assert.Equal(1, history.UndoCount);
            Assert.Equal(1, history.RedoCount);
            Assert.False(history.IsDirty);
        }
        Assert.Equal(transform.X, editor.Reference.X, precision: 9);
        Assert.Equal(transform.Y, editor.Reference.Y, precision: 9);
        Assert.Equal(transform.Scale, editor.Reference.Scale, precision: 9);
        Assert.Equal(transform.RotationDegrees, editor.Reference.Rotation);
        Assert.True(editor.Reference.FlipHorizontal && editor.Reference.FlipVertical);
        Assert.Same(source, editor.Reference.Source);
        editor.Apply(history.Redo());
        Assert.True(ProjectEditSnapshot.Equivalent(redoState, editor.Capture()));
    }
    [Theory][InlineData("add")][InlineData("delete")][InlineData("rename")][InlineData("reorder")]
    public void PaletteEdits_AreUndoable(string operation)
    {
        var e=new Editor();var one=e.Palette.Add(new(1,2,3),"One");e.Palette.Add(new(4,5,6),"Two");
        var before=e.Capture();var h=new SessionHistory<ProjectEditSnapshot>(before,ProjectEditSnapshot.Equivalent);
        switch(operation) {case "add":e.Palette.Add(new(7,8,9));break;case "delete":e.Palette.Delete(one);break;case "rename":one.Name="Renamed";break;case "reorder":e.Palette.Move(one,1);break;}
        h.Observe(e.Capture());e.Apply(h.Undo());Assert.True(ProjectEditSnapshot.Equivalent(before,e.Capture()));
    }
    [Fact] public void ReopenAndRecovery_ClearBothStacks()
    { var h=new SessionHistory<int>(0);h.Observe(1);h.Undo();h.Reset(9,true);Assert.False(h.CanUndo);Assert.False(h.CanRedo);Assert.True(h.IsDirty); }
    [Fact] public void SerializedState_HasNoHistoryOrRuntimeHandles()
    {
        var json=JsonSerializer.Serialize(new Editor().Capture().State);
        Assert.DoesNotContain("Undo",json);Assert.DoesNotContain("History",json);Assert.DoesNotContain("HWND",json);
    }
}
