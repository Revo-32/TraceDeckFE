# Third-party software and assets

TraceDeck FE's original source code is released under the repository's [MIT License](LICENSE). The components below remain under their own licenses; the MIT License does not relicense them.

## Production and distributed components

| Component | Purpose | License | Project | Included license text |
| --- | --- | --- | --- | --- |
| Pretendard 1.3.9 | Application and user-guide typography | SIL Open Font License 1.1 | [orioncactus/pretendard](https://github.com/orioncactus/pretendard) | [Pretendard-OFL.txt](licenses/Pretendard-OFL.txt) |
| Magick.NET 14.16.0 | Reference image decoding and raster processing | Apache License 2.0 | [dlemstra/Magick.NET](https://github.com/dlemstra/Magick.NET) | [Magick.NET-Apache-2.0.txt](licenses/Magick.NET-Apache-2.0.txt) |
| ImageMagick and bundled components | Native image codec/processing implementation used by Magick.NET | Upstream licenses and notices listed by the distributor | [ImageMagick](https://github.com/ImageMagick/ImageMagick) | [Magick.NET-NOTICE.txt](licenses/Magick.NET-NOTICE.txt) |
| Microsoft .NET 8 Windows Desktop Runtime | Self-contained Windows application runtime | Microsoft .NET runtime license and upstream third-party notices | [dotnet/runtime](https://github.com/dotnet/runtime) | [.NET license](licenses/dotnet-runtime-LICENSE.txt), [.NET notices](licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt) |

The files in `licenses/` are preserved license and notice payloads and have not been shortened or rewritten.

## Development and test dependencies

The test project restores `Microsoft.NET.Test.Sdk`, xUnit, `xunit.runner.visualstudio`, and `coverlet.collector` from NuGet. These packages are development-only and retain their upstream licenses. They are not included as loose files in the portable release.

## Project branding and fixtures

`TraceDeck_FE_Mini_logo.png` is the TraceDeck FE project branding asset supplied for this project. `TraceDeckFE.ico` is derived from it. The simple SVG test fixtures in `tests/Assets/` and `test-fixtures/` are repository-owned synthetic fixtures; they contain no Microsoft, Xbox, Playground Games, or Forza artwork.

