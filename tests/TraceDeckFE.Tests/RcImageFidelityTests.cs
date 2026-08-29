using ImageMagick;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TraceDeckFE.Models;
using TraceDeckFE.Services;
using PointD = TraceDeckFE.Models.PointD;

namespace TraceDeckFE.Tests;

public sealed class RcImageFidelityTests
{
    private sealed class Logger : ITraceLogger
    { public void Info(string m){} public void Warning(string m){} public void Error(string m,Exception? e=null){} }

    [Fact]
    public async Task LargePngKeepsFullResolutionBytesAlphaAndOriginalPicker()
    {
        using var data=new TempData();Directory.CreateDirectory(data.DataDirectory);
        var path=Path.Combine(data.DataDirectory,"Large.png");
        using(var image=new MagickImage(new MagickColor("#2876C880"),4096,2160))image.Write(path,MagickFormat.Png32);
        var bytes=await File.ReadAllBytesAsync(path);
        var images=new ReferenceImageService(new Logger());
        var source=await images.LoadAsync(path,1280,720);
        Assert.Equal(4096,source.OriginalBitmap.PixelWidth);Assert.Equal(2160,source.OriginalBitmap.PixelHeight);
        var hash=SHA256.HashData(bytes);Assert.Equal(hash,SHA256.HashData(source.OriginalBytes));
        var unchanged=await images.RenderDisplayAsync(source,false,0,500,264);
        Assert.Same(source.OriginalBitmap,unchanged);
        var effects=await images.RenderDisplayAsync(source,true,28,500,264);
        Assert.Equal(4096,effects.PixelWidth);Assert.Equal(2160,effects.PixelHeight);
        var color=new ReferenceColorService(images);
        var state=new ReferenceState();state.SetImage(source,1280,720);
        var point=ReferenceTransformMath.ImageToDisplay(state.VisualTransform,new PointD(2048.5,1080.5),source.PixelWidth,source.PixelHeight);
        var sampled=await color.SampleDisplayAsync(source,state.VisualTransform,point);
        Assert.Equal(new RgbaColor(40,118,200,128),sampled);
        Assert.Equal(hash,SHA256.HashData(source.OriginalBytes));
    }

    [Fact]
    public async Task ComplexSvgRetainsSourceAndRequestedHighResolution()
    {
        using var data=new TempData();Directory.CreateDirectory(data.DataDirectory);
        var svg=new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"2048\" height=\"1024\" viewBox=\"0 0 2048 1024\">");
        svg.Append("<rect width=\"2048\" height=\"1024\" fill=\"#123456\"/>");
        for(var i=0;i<400;i++)svg.Append($"<path d=\"M {i*5} 10 Q {i*5+20} 500 {i*5} 1000\" stroke=\"#abcdef\" fill=\"none\" stroke-width=\"1\"/>");
        svg.Append("</svg>");
        var path=Path.Combine(data.DataDirectory,"Complex.svg");await File.WriteAllTextAsync(path,svg.ToString(),new UTF8Encoding(false));
        var images=new ReferenceImageService(new Logger());var source=await images.LoadAsync(path,1280,720);
        var hash=SHA256.HashData(source.OriginalBytes);
        var rendered=await images.RenderDisplayAsync(source,false,0,4096,2048);
        Assert.True(source.IsVector);Assert.Equal(4096,rendered.PixelWidth);Assert.Equal(2048,rendered.PixelHeight);Assert.True(rendered.IsFrozen);
        var original=await images.RenderOriginalSourceAsync(source,2048,1024);
        Assert.Equal(2048,original.PixelWidth);Assert.Equal(1024,original.PixelHeight);
        Assert.Equal(hash,SHA256.HashData(source.OriginalBytes));
    }
}
