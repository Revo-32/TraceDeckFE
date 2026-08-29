using System.IO;
using System.Windows.Input;
using TraceDeckFE.Models;
using TraceDeckFE.Services;

namespace TraceDeckFE.Tests;

public sealed class SettingsAndHotkeyTests
{
    [Fact] public void Defaults_MatchM4()
    {
        var s=new ApplicationSettings();Assert.Equal(LayoutMode.Auto,s.Layout);Assert.Equal(312,s.CompactWidth);Assert.Equal(448,s.WideWidth);
        Assert.True(s.RememberLastProject && s.RestorePreviousSession && s.AutomaticallyDetectForza && s.RememberFoldedCards && s.RememberWidthPerLayout);
        Assert.Equal(10,s.ZoomStepPercent);Assert.True(s.ZoomTowardCursor && s.ConfirmReferenceReplacement && s.Magnifier && s.AutosaveEnabled);
        Assert.Equal(3,s.HsbDecimalPlaces);Assert.Equal(300,s.AutosaveIntervalSeconds);
    }
    [Theory][InlineData(LayoutMode.Auto,1920,1080,LayoutMode.Compact)][InlineData(LayoutMode.Auto,3440,1440,LayoutMode.Wide)]
    [InlineData(LayoutMode.Auto,2560,1440,LayoutMode.Compact)][InlineData(LayoutMode.Compact,3440,1440,LayoutMode.Compact)]
    [InlineData(LayoutMode.Wide,1920,1080,LayoutMode.Wide)]
    public void LayoutResolution(LayoutMode selected,double width,double height,LayoutMode expected) => Assert.Equal(expected,LayoutPolicy.Resolve(selected,width,height));
    [Theory][InlineData(100,280)][InlineData(999,520)][InlineData(390,390)]
    public void WidthClamps(double requested,double expected) => Assert.Equal(expected,LayoutPolicy.ClampWidth(requested));
    [Fact] public void LayoutWidths_PersistIndependently()
    { var s=new ApplicationSettings();s.RememberWidth(LayoutMode.Compact,301);s.RememberWidth(LayoutMode.Wide,471);Assert.Equal(301,s.WidthFor(LayoutMode.Compact));Assert.Equal(471,s.WidthFor(LayoutMode.Wide)); }
    [Fact] public async Task SettingsRoundTrip_PreservesAllPreferences()
    {
        using var f=new TempData();var service=new SettingsService(f);
        var s=new ApplicationSettings { Layout=LayoutMode.Wide,CompactWidth=301,WideWidth=470,HsbDecimalPlaces=2,ConfirmReferenceReplacement=false,AutosaveIntervalSeconds=10,
            FoldedCards=new(){PaletteExpanded=false,TransformExpanded=false,ColorExpanded=true},LastProjectPath="C:\\work\\Example.TDFE" };
        await service.SaveAsync(s);var read=service.Load(out var warning);Assert.Null(warning);
        Assert.Equal(s.Layout,read.Layout);Assert.Equal(301,read.CompactWidth);Assert.Equal(470,read.WideWidth);Assert.Equal(2,read.HsbDecimalPlaces);
        Assert.False(read.ConfirmReferenceReplacement);Assert.Equal(10,read.AutosaveIntervalSeconds);Assert.False(read.FoldedCards.PaletteExpanded);Assert.False(read.FoldedCards.TransformExpanded);
        Assert.Equal(s.Shortcuts,read.Shortcuts);Assert.Equal(s.LastProjectPath,read.LastProjectPath);
    }
    [Fact] public async Task UnknownAndInvalidFields_KeepKnownSettings()
    {
        using var f=new TempData();var service=new SettingsService(f);
        await AtomicFile.WriteAsync(service.SettingsPath,System.Text.Encoding.UTF8.GetBytes("{\"futureField\":123,\"wideWidth\":490,\"hsbDecimalPlaces\":\"broken\",\"confirmReferenceReplacement\":false}"));
        var s=service.Load(out var warning);Assert.NotNull(warning);Assert.Equal(490,s.WideWidth);Assert.Equal(3,s.HsbDecimalPlaces);Assert.False(s.ConfirmReferenceReplacement);
    }
    [Theory][InlineData("{")][InlineData("null")][InlineData("[]")]
    public async Task CorruptSettings_SafeDefaults(string content)
    { using var f=new TempData();var service=new SettingsService(f);await AtomicFile.WriteAsync(service.SettingsPath,System.Text.Encoding.UTF8.GetBytes(content));var s=service.Load(out var warning);Assert.NotNull(warning);Assert.Equal(300,s.AutosaveIntervalSeconds); }
    [Theory][InlineData(10)][InlineData(30)][InlineData(60)][InlineData(300)][InlineData(600)]
    public void AutosaveIntervals(int interval) { var s=new ApplicationSettings {AutosaveIntervalSeconds=interval};Assert.Equal(interval,s.AutosaveIntervalSeconds);s.AutosaveIntervalSeconds=11;Assert.Equal(300,s.AutosaveIntervalSeconds); }
    [Fact] public void Precision_OnlyChangesDisplay()
    { var color=new ColorState();color.SetColor(new(37,89,145,70));var before=color.Hsb;color.Precision=2;Assert.Equal(before,color.Hsb);Assert.Equal(4,color.HueText.Length);color.Precision=3;Assert.Equal(5,color.HueText.Length); }
    [Fact] public void DefaultShortcuts_AreValidAndComplete()
    { var defaults=ShortcutCatalog.Defaults();Assert.Equal(26,defaults.Count);foreach(var b in defaults)Assert.Null(ShortcutCatalog.Validate(b,defaults));Assert.Equal(7,defaults.Count(b=>b.IsGlobal)); }
    [Fact] public void HotkeyContext_ReleasesReservationsAndPreservesOtherBindingsOnConflict()
    {
        var native=new FakeRegistrar { Fail=ShortcutAction.ToggleGrid };using var service=new HotkeyService(native);service.Configure(ShortcutCatalog.Defaults());Assert.Empty(native.Registered);
        service.SetContext(true);Assert.Equal(6,native.Registered.Count);Assert.Single(service.Conflicts);service.SetContext(false);Assert.Empty(native.Registered);Assert.Null(service.Resolve(0x6000+(int)ShortcutAction.ToggleVisible));
    }
    [Fact] public void RestoreDefaults_ReplacesCustomBindings()
    { var s=new ApplicationSettings();s.Shortcuts=s.Shortcuts.Select(b=>b.Action==ShortcutAction.Save?b with {Key=Key.F6}:b).ToList();Assert.Equal(Key.F6,s.Shortcuts.Single(b=>b.Action==ShortcutAction.Save).Key);s.Shortcuts=ShortcutCatalog.Defaults();Assert.Equal(Key.S,s.Shortcuts.Single(b=>b.Action==ShortcutAction.Save).Key); }
    [Theory][InlineData(Key.G,ModifierKeys.Windows)][InlineData(Key.Enter,ModifierKeys.Alt)][InlineData(Key.Z,ModifierKeys.Alt)][InlineData(Key.None,ModifierKeys.Control)]
    public void DangerousOrInvalidBindings_AreRejected(Key key,ModifierKeys modifiers) => Assert.NotNull(ShortcutCatalog.Validate(new(ShortcutAction.Save,key,modifiers),[]));
    private sealed class FakeRegistrar:IHotkeyRegistrar
    { public ShortcutAction? Fail;public HashSet<int> Registered {get;}=[];public bool Register(int id,ShortcutBinding b) {if(b.Action==Fail)return false;Registered.Add(id);return true;}public void Unregister(int id)=>Registered.Remove(id); }
}

internal sealed class TempData:IApplicationPaths,IDisposable
{
    public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"TraceDeckFE-M4-"+Guid.NewGuid().ToString("N"));
    public void Dispose() {if(Directory.Exists(DataDirectory))Directory.Delete(DataDirectory,true);}
}
