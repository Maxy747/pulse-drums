# References and sample content

## Audio libraries

NAudio.Core and NAudio.Asio **2.2.1** are from the official [NAudio repository](https://github.com/naudio/NAudio/tree/v2.2.1) / NuGet packages, under MIT (Mark Heath and contributors). Microsoft.Win32.Registry **4.7.0**, .NET Framework 4.6 compatibility assembly, is from Microsoft's NuGet package under MIT (.NET Foundation and contributors). Binaries are vendored in `vendor/NAudio` and embedded in Pulse.exe. License texts are present there and embedded as resources. `SHA256.json` records the shipped binary hashes.

The app's vector kit drawing uses the user's supplied numbered photograph and top-down dark/green artwork as visual references. The provided images are not redistributed in this repository or embedded into the executable.

## GSCW sample library

Source: https://github.com/gregharvey/drum-samples

Pinned revision: `ea524c8a952545fc099565426fc673e79076136f`

360 original WAV files, roughly 301 MB, across GSCW Drum Kits 1 and 2. The downloader retains the upstream `LICENSE.md` and `GSCW 2005 LICENSE AGREEMENT.rtf`. The sample content is **not covered by Pulse's MIT license**, and is fetched directly from its source rather than republished in Pulse's source repository or releases.

The G&S Custom Works 2005 agreement describes royalty-free use and restrictions on resale, sublicensing and modification. Read the actual upstream license before using the sample library. Pulse does not change the sample files on disk; format decoding and sample-rate conversion occur in memory for playback.

Kit 1 has recorded 10-inch and 13-inch toms, without a separate 12-inch tom. Its Low tom preset uses the matching 12-inch sample from Kit 2. Kit 2 provides 10/12/13-inch samples, so it is the first-run sound preset. Each part's selector is restricted to that instrument's category. Custom matching WAV files can also be loaded.

## Arduino interface

Serial compatibility is based on https://github.com/marwans200/Arduino-Drums. Pulse is independently implemented; upstream Python/Arduino source code and executables are not bundled.
