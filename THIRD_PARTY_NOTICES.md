# Third-party components

The MIT license in this repository applies to LumaConvert's original application code and assets. It does not replace third-party licenses.

| Component | Use | Upstream |
| --- | --- | --- |
| FFmpeg 8.1 shared GPL build | Video probing, conversion and first-frame previews, as separate processes | [FFmpeg](https://ffmpeg.org/), [BtbN build scripts and distribution](https://github.com/BtbN/FFmpeg-Builds) |
| Magick.NET Q8 x64 15.0.0 | Image conversion and previews | [Magick.NET](https://github.com/dlemstra/Magick.NET) |
| ImageMagick and bundled image codecs | Native image decoding and encoding | [ImageMagick](https://imagemagick.org/) and the Magick.NET package notices |
| Windows App SDK / WinUI 3 | Native Windows user interface | [Windows App SDK](https://github.com/microsoft/WindowsAppSDK) |
| .NET | Managed runtime | [dotnet/runtime](https://github.com/dotnet/runtime) |

FFmpeg is executed as a separate program. The selected BtbN distribution enables GPL components; consult its included `LICENSE.txt`, the `ffmpeg -L` output, and upstream build recipes. FFmpeg's own source for the initially tested shared build is commit [`46d899d7a0`](https://github.com/FFmpeg/FFmpeg/commit/46d899d7a0); the setup script records the exact archive checksum downloaded for each build. BtbN's build repository records the toolchain and external codec recipes.

Magick.NET carries notices for ImageMagick and its bundled dependencies. Preserve the package notices and native codec license terms when redistributing.

Portable release archives contain a `licenses` directory with FFmpeg's license information and Magick.NET notices. Source builds download third-party components separately; these binaries are not checked into this Git repository.
