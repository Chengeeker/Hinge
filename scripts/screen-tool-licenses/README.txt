Hinge bundled screen runtime (Windows x64)

Unmodified binaries: Genymobile scrcpy 5.0.1 official Windows x64 release.
https://github.com/Genymobile/scrcpy/releases/tag/v5.0.1
SHA-256: b12a2c4ee8be317422451fc7dcf8ee20a71b5ea7ef9ad73ddd825a25316227a5
scrcpy is Copyright Genymobile and contributors, Apache License 2.0.
The original license is retained as ../LICENSE.txt.

Dependencies:
- FFmpeg 9.0.2, LGPL 2.1 or later; avcodec/avformat/avutil/swresample shared DLLs.
- libusb 1.0.30, LGPL 2.1 or later, shared DLL.
- SDL 3.4.18, zlib license; see SDL.txt.
- dav1d 1.5.4 (linked into FFmpeg), BSD 2-Clause; see dav1d.txt.
- zlib (linked into FFmpeg), zlib license; see zlib.txt.
- Android Platform-Tools 37.0.1: original ADB-NOTICE.txt retained.

LGPL-2.1.txt contains the LGPL license. Exact FFmpeg and libusb corresponding
source archives, and the dav1d source archive, are shipped in ../sources/.
The shared libraries are unmodified and may be replaced with compatible builds.
Hinge imposes no restriction on reverse engineering these libraries for debugging
modifications to them. Hinge itself remains MIT licensed.

Upstream dependency recipes (including compiler flags and shared linking):
https://github.com/Genymobile/scrcpy/tree/v5.0.1/app/deps
The accompanying *-build.sh files are unchanged Apache-2.0 upstream recipes.
FFmpeg includes its own copyright notices and license alternatives in its source.
The selected official binary reports "LGPL version 2.1 or later", not GPL.

Only runtime files, source archives and notices are bundled. Launch helper BAT/VBS
scripts from the upstream Windows archive are not shipped or invoked by Hinge.
