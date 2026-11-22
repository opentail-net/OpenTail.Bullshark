# Third-party notices

This file lists third-party software that OpenTail.Bullshark uses and that is included in builds. The project's own licence is in [LICENSE.md](LICENSE.md) and is not changed by this file.

## ZstdSharp.Port 0.8.8

- **Use:** decodes Codex rollout files (zstd-compressed) on net10.0 builds, through its `DecompressionStream`. The net11.0 build uses the .NET runtime's own decoder and does not include this package.
- **Project:** https://github.com/oleg-st/ZstdSharp
- **Package:** https://www.nuget.org/packages/ZstdSharp.Port
- **Licence:** MIT. The licence text is reproduced below, as published in the project's LICENSE file (https://github.com/oleg-st/ZstdSharp/blob/master/LICENSE).
- **Copyright:** the licence file reads "Copyright (c) 2021 Oleg Stepanischev"; the NuGet page reads "Copyright Oleg Stepanischev 2026". Both are the same copyright holder.

```
MIT License

Copyright (c) 2021 Oleg Stepanischev

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
