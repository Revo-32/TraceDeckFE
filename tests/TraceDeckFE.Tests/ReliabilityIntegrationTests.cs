using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TraceDeckFE.Models;
using TraceDeckFE.Overlay;
using TraceDeckFE.Services;
using TraceDeckFE.ViewModels;

namespace TraceDeckFE.Tests;

[Collection("Wpf UI")]
public sealed class ReliabilityIntegrationTests
{
    private sealed class Logger:ITraceLogger
    { public void Info(string message){} public void Warning(string message){} public void Error(string message,Exception? exception=null){} }

    [Fact]
    public async Task DispatcherWorkflow_UndoSaveRecoveryAndReopen_StayConsistent()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                MainViewModel? vm = null;
                try
                {
                    var app = new TraceDeckFE.App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    using var data = new TempData();
                    var logger = new Logger(); var reference = new ReferenceState(); var guides = new GuideState(); var colors = new ColorState(); var palette = new PaletteState();
                    var images = new ReferenceImageService(logger); var colorService = new ReferenceColorService(images); var recovery = new RecoveryService(data,logger);
                    var overlay = new OverlayWindow(reference,guides,colors,colorService,logger);
                    vm = new(reference,guides,new(),colors,palette,new(),new(),new WindowCatalog(logger),new ForzaWindowTracker(logger),images,
                        new AutoPaletteService(colorService),new ProjectArchiveService(),overlay,logger);
                    vm.ConfigureReliability(new ApplicationSettings(),new SettingsService(data),recovery);
                    var pixels = new byte[64*36*4];
                    for(var i=0;i<pixels.Length;i+=4) { pixels[i]=(byte)(i%256); pixels[i+1]=(byte)(i/256%256); pixels[i+2]=220; pixels[i+3]=255; }
                    var bitmap = BitmapSource.Create(64,36,96,96,PixelFormats.Bgra32,null,pixels,64*4); bitmap.Freeze();
                    await vm.OpenClipboardBitmapAsync(bitmap);
                    Assert.Equal(1,vm.History.UndoCount);
                    var path = Path.Combine(data.DataDirectory,"Integration.TDFE"); Directory.CreateDirectory(data.DataDirectory);
                    Assert.True(await vm.SaveProjectAsync(path)); Assert.False(vm.Project.IsDirty);
                    var initial = reference.NormalizedTransform;
                    vm.Nudge(20,10);vm.FlushEdits();Assert.True(vm.Project.IsDirty);vm.Undo();Assert.False(vm.Project.IsDirty);Assert.Equal(initial,reference.NormalizedTransform);
                    vm.Redo();Assert.True(vm.Project.IsDirty);
                    vm.BeginGesture();for(var i=0;i<20;i++){reference.Contrast=i;vm.FlushEdits();}vm.EndGesture();
                    var steps = vm.History.UndoCount;vm.Undo();Assert.Equal(0,reference.Contrast);Assert.Equal(steps-1,vm.History.UndoCount);
                    vm.Redo();Assert.Equal(19,reference.Contrast);
                    var oldCount=vm.History.UndoCount;vm.UiState.ColorExpanded=false;vm.UiState.ControllerWidth=301;
                    vm.Settings.HsbDecimalPlaces=2;vm.Settings.ConfirmReferenceReplacement=false;vm.FlushEdits();Assert.Equal(oldCount,vm.History.UndoCount);
                    Assert.False(vm.InputSettings.ConfirmReplacement);Assert.Equal(2,vm.Colors.Precision);
                    await vm.FlushSettingsAsync();
                    Assert.True(await vm.AutosaveAsync());Assert.False(await vm.AutosaveAsync());
                    var savedBytes=File.ReadAllBytes(path);var candidate=Assert.Single(await vm.FindRecoveryAsync());
                    vm.NewProject();Assert.False(vm.CanUndo);Assert.False(vm.Reference.HasImage);
                    Assert.True(await vm.RestoreRecoveryAsync(candidate));Assert.True(vm.Project.IsDirty);Assert.False(vm.CanUndo);Assert.False(vm.CanRedo);
                    Assert.Equal(19,vm.Reference.Contrast);Assert.Equal(savedBytes,File.ReadAllBytes(path));
                    Assert.True(await vm.SaveProjectAsync(path));Assert.Empty(await vm.FindRecoveryAsync());
                    Assert.True(await vm.OpenProjectAsync(path));Assert.False(vm.Project.IsDirty);Assert.False(vm.CanUndo);
                    Assert.False(vm.UiState.ColorExpanded);Assert.Equal(301,vm.UiState.ControllerWidth);
                    var current=vm.Reference.Source;Assert.False(await vm.OpenProjectAsync(Path.Combine(data.DataDirectory,"Missing.TDFE")));Assert.Same(current,vm.Reference.Source);
                    // Optional deterministic fixture for desktop smoke; never enabled in ordinary test runs.
                    if (Environment.GetEnvironmentVariable("TRACEDECK_SMOKE_DIRECTORY") is { Length: > 0 } smokeDirectory)
                    {
                        Directory.CreateDirectory(smokeDirectory);
                        var smokePath=Path.Combine(smokeDirectory,"M4Smoke.TDFE");
                        Assert.True(await vm.SaveProjectAsync(smokePath));
                        var smokeSettings=new ApplicationSettings { Layout=LayoutMode.Compact,CompactWidth=312,AutosaveIntervalSeconds=10,LastProjectPath=smokePath,
                            FoldedCards=new(){ ProjectExpanded=true,OverlayExpanded=true,TransformExpanded=true,PositionExpanded=false,ImageAssistExpanded=false,ColorExpanded=false,PaletteExpanded=false } };
                        await new SettingsService(new PortableApplicationPaths(smokeDirectory)).SaveAsync(smokeSettings);
                    }
                    RcUiVerification.Run(vm, app);
                    await RcLifecycleVerification.RunAsync(vm);
                    vm.Dispose(); vm=null; app.Shutdown();
                    completion.SetResult();
                }
                catch(Exception e) { completion.SetException(e); }
                finally { vm?.Dispose(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
