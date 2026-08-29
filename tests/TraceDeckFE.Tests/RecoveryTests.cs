using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TraceDeckFE.Models;
using TraceDeckFE.Services;

namespace TraceDeckFE.Tests;

public sealed class RecoveryTests
{
    private sealed class Logger:ITraceLogger
    { public void Info(string message){} public void Warning(string message){} public void Error(string message,Exception? exception=null){} }
    private static TdfProjectPackage Package(DateTimeOffset? time=null,Guid? id=null,byte[]? bytes=null)
    {
        bytes ??= [12,34,56,78];var now=time??DateTimeOffset.UtcNow;
        return new(new(){ProjectId=id??Guid.NewGuid(),CreatedUtc=now.AddMinutes(-10),ModifiedUtc=now,ReferenceEntry="reference/source.png",ReferenceSha256=ProjectArchiveService.ComputeSha256(bytes)},
            new(){Reference=new(){OriginalFilename="Clipboard.png",SourceFormat="PNG",SourceKind=ReferenceSourceKind.Clipboard,PixelWidth=2,PixelHeight=2},
                Palette=[new(){Id=Guid.NewGuid(),Name="Accent",Color=new(10,20,30,90)}]},bytes);
    }
    [Fact] public async Task DirtySnapshot_OnlyChangedStateWrites()
    {
        using var f=new TempData();var r=new RecoveryService(f,new Logger());var p=Package();
        Assert.False(await r.WriteSnapshotAsync(p,null,false,"A"));Assert.False(Directory.Exists(r.Root));
        Assert.True(await r.WriteSnapshotAsync(p,null,true,"A"));Assert.False(await r.WriteSnapshotAsync(p,null,true,"A"));
        Assert.Single(Directory.GetFiles(r.Root,"snapshot-*.json",SearchOption.AllDirectories));
    }
    [Fact] public async Task RetainsThreeNewestSnapshots_AndOneSharedBinary()
    {
        using var f=new TempData();var r=new RecoveryService(f,new Logger());var p=Package(DateTimeOffset.UtcNow.AddHours(-1));
        for(var i=0;i<5;i++)Assert.True(await r.WriteSnapshotAsync(p with {Manifest=p.Manifest with {ModifiedUtc=p.Manifest.ModifiedUtc.AddMinutes(i)}},null,true,i.ToString()));
        var snapshots=Directory.GetFiles(r.Root,"snapshot-*.json",SearchOption.AllDirectories);Assert.Equal(3,snapshots.Length);
        Assert.Single(Directory.GetFiles(r.Root,"*.bin",SearchOption.AllDirectories));
        var candidate=Assert.Single(await r.FindCandidatesAsync());Assert.Equal(p.Manifest.ModifiedUtc.AddMinutes(4),candidate.Snapshot.CapturedUtc);
    }
    [Fact] public async Task OverlappingWrites_AreSingleFlight()
    {
        using var f=new TempData();var r=new RecoveryService(f,new Logger());var p=Package(bytes:new byte[1024*1024]);
        var first=r.WriteSnapshotAsync(p,null,true,"A");var second=r.WriteSnapshotAsync(p,null,true,"B");
        var results=await Task.WhenAll(first,second);Assert.True(results[0]);Assert.False(results[1]);Assert.False(r.IsWriting);
    }
    [Fact]
    public async Task HundredEdits_KeepStorageBoundedAndLatestStateReadableAfterRestart()
    {
        using var data = new TempData();
        var recovery = new RecoveryService(data, new Logger());
        var original = Package(DateTimeOffset.UtcNow.AddHours(-1), bytes: new byte[1024 * 1024]);
        var latest = original;
        for (var edit = 0; edit < 100; edit++)
        {
            latest = original with
            {
                Manifest = original.Manifest with { ModifiedUtc = original.Manifest.ModifiedUtc.AddSeconds(edit) },
                State = original.State with { Overlay = original.State.Overlay with { Opacity = (edit + 1) / 100d } }
            };
            Assert.True(await recovery.WriteSnapshotAsync(latest, null, true, $"edit-{edit}"));
            Assert.InRange(Directory.GetFiles(recovery.Root, "snapshot-*.json", SearchOption.AllDirectories).Length, 1, 3);
            Assert.Single(Directory.GetFiles(recovery.Root, "*.bin", SearchOption.AllDirectories));
        }
        var restarted = new RecoveryService(data, new Logger());
        var restored = Assert.Single(await restarted.FindCandidatesAsync());
        Assert.Equal(latest.Manifest.ModifiedUtc, restored.Snapshot.CapturedUtc);
        Assert.Equal(latest.State.Overlay, restored.Package.State.Overlay);
        Assert.Equal(original.ReferenceBytes, restored.Package.ReferenceBytes);
        Assert.Equal(3, Directory.GetFiles(recovery.Root, "snapshot-*.json", SearchOption.AllDirectories).Length);
        Assert.Empty(Directory.GetFiles(recovery.Root, "*.tmp", SearchOption.AllDirectories));
        Assert.False(recovery.IsWriting);
    }
    [Fact] public async Task RecoveryNewerThanManualSave_IsDetected_WithoutOverwritingIt()
    {
        using var f=new TempData();Directory.CreateDirectory(f.DataDirectory);var path=Path.Combine(f.DataDirectory,"Manual.TDFE");var p=Package();
        await new ProjectArchiveService().SaveAsync(path,p);File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddHours(-1));var original=File.ReadAllBytes(path);
        var r=new RecoveryService(f,new Logger());await r.WriteSnapshotAsync(p,path,true,"newer");
        var candidate=Assert.Single(await r.FindCandidatesAsync());Assert.Equal(path,candidate.Snapshot.ManualPath);Assert.Equal(original,File.ReadAllBytes(path));
        Assert.Equal(p.ReferenceBytes,candidate.Package.ReferenceBytes);Assert.Equal(p.State.Palette,candidate.Package.State.Palette);
    }
    [Fact] public async Task ManualSaveNewer_NoRecoveryPrompt()
    {
        using var f=new TempData();Directory.CreateDirectory(f.DataDirectory);var path=Path.Combine(f.DataDirectory,"Manual.TDFE");var p=Package(DateTimeOffset.UtcNow.AddHours(-1));
        var r=new RecoveryService(f,new Logger());await r.WriteSnapshotAsync(p,path,true,"A");await new ProjectArchiveService().SaveAsync(path,p);
        Assert.Empty(await r.FindCandidatesAsync());
    }
    [Fact] public async Task SuccessfulManualSave_CleansOlderSnapshotsOnly()
    {
        using var f=new TempData();var r=new RecoveryService(f,new Logger());var p=Package();await r.WriteSnapshotAsync(p,null,true,"A");
        await r.WriteSnapshotAsync(p with {Manifest=p.Manifest with {ModifiedUtc=p.Manifest.ModifiedUtc.AddMinutes(1)}},null,true,"B");
        await r.ManualSaveSucceededAsync(p.Manifest.ProjectId,p.Manifest.ModifiedUtc);
        Assert.Single(Directory.GetFiles(r.Root,"snapshot-*.json",SearchOption.AllDirectories));Assert.Single(await r.FindCandidatesAsync());
    }
    [Fact] public async Task DontSave_DismissalSurvivesRestart()
    {
        using var f=new TempData();var r=new RecoveryService(f,new Logger());var p=Package();await r.WriteSnapshotAsync(p,null,true,"A");
        await r.DismissAsync(p.Manifest.ProjectId,DateTimeOffset.UtcNow);var restarted=new RecoveryService(f,new Logger());Assert.Empty(await restarted.FindCandidatesAsync());
        Assert.Single(Directory.GetFiles(r.Root,"snapshot-*.json",SearchOption.AllDirectories));
    }
    [Fact] public async Task UnsavedClipboardReference_RestoresExactCanonicalPng()
    {
        using var f=new TempData();var r=new RecoveryService(f,new Logger());var imageService=new ReferenceImageService(new Logger());
        var bitmap=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[]{0,0,255,255,0,255,0,255,255,0,0,255,12,34,56,90},8);bitmap.Freeze();
        var source=await imageService.LoadClipboardBitmapAsync(bitmap,1280,720);var p=Package(bytes:source.OriginalBytes);
        await r.WriteSnapshotAsync(p,null,true,"clipboard");var candidate=Assert.Single(await new RecoveryService(f,new Logger()).FindCandidatesAsync());
        Assert.Null(candidate.Snapshot.ManualPath);Assert.Equal(source.OriginalBytes,candidate.Package.ReferenceBytes);
        var loaded=await imageService.LoadEmbeddedAsync(candidate.Package.ReferenceBytes!,"Clipboard.png","PNG",ReferenceSourceKind.Clipboard,1280,720);
        Assert.Equal(2,loaded.PixelWidth);Assert.Equal(ReferenceSourceKind.Clipboard,loaded.SourceKind);
        var history=new SessionHistory<int>(0);history.Observe(1);history.Reset(2,true);Assert.False(history.CanUndo);Assert.False(history.CanRedo);Assert.True(history.IsDirty);
    }
    [Fact] public async Task CorruptLatestSnapshot_FallsBackToOlderValid()
    {
        using var f=new TempData();var r=new RecoveryService(f,new Logger());var p=Package();await r.WriteSnapshotAsync(p,null,true,"A");
        await r.WriteSnapshotAsync(p with {Manifest=p.Manifest with {ModifiedUtc=p.Manifest.ModifiedUtc.AddMinutes(1)}},null,true,"B");
        var newest=Directory.GetFiles(r.Root,"snapshot-*.json",SearchOption.AllDirectories).OrderDescending().First();await File.WriteAllTextAsync(newest,"{");
        var candidate=Assert.Single(await r.FindCandidatesAsync());Assert.Equal(p.Manifest.ModifiedUtc,candidate.Snapshot.CapturedUtc);
    }
    [Fact] public async Task MissingOrCorruptedEmbeddedAsset_IsSkipped()
    {
        using var f=new TempData();var r=new RecoveryService(f,new Logger());var p=Package();await r.WriteSnapshotAsync(p,null,true,"A");
        var asset=Assert.Single(Directory.GetFiles(r.Root,"*.bin",SearchOption.AllDirectories));await File.WriteAllBytesAsync(asset,[0]);Assert.Empty(await r.FindCandidatesAsync());
        File.Delete(asset);Assert.Empty(await r.FindCandidatesAsync());
    }
    [Fact] public async Task FailedManualSave_DoesNotDeleteRecovery()
    {
        using var f=new TempData();var r=new RecoveryService(f,new Logger());var p=Package();await r.WriteSnapshotAsync(p,null,true,"A");
        await Assert.ThrowsAsync<ProjectArchiveException>(()=>new ProjectArchiveService().SaveAsync(Path.Combine(f.DataDirectory,"Manual.TDFE"),p with {ReferenceBytes=[0]}));
        Assert.Single(await r.FindCandidatesAsync());
    }
    [Fact] public async Task MetadataDoesNotContainHistoryOrSourceBytes()
    {
        using var f=new TempData();var r=new RecoveryService(f,new Logger());await r.WriteSnapshotAsync(Package(),null,true,"A");
        var text=File.ReadAllText(Assert.Single(Directory.GetFiles(r.Root,"snapshot-*.json",SearchOption.AllDirectories)));
        Assert.DoesNotContain("history",text,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("originalBytes",text,StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public async Task BlockedRecoveryDirectory_FailsWithoutAffectingProject()
    {
        using var f=new TempData();Directory.CreateDirectory(f.DataDirectory);var r=new RecoveryService(f,new Logger());await File.WriteAllTextAsync(r.Root,"blocked");
        await Assert.ThrowsAsync<IOException>(()=>r.WriteSnapshotAsync(Package(),null,true,"A"));Assert.False(r.IsWriting);Assert.Empty(await r.FindCandidatesAsync());
    }
}
