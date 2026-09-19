# Fika-Neo · Plugin

[English](README.md) · [中文](README.zh.md)

Unofficial BepInEx **client** fork of [project-fika/Fika-Plugin](https://github.com/project-fika/Fika-Plugin).

Companion server: [Fika-Server-CSharp-Neo](https://github.com/Neko17awa/Fika-Server-CSharp-Neo).

**This is not official Fika.** It is not affiliated with, sponsored by, or endorsed by [Project Fika](https://github.com/project-fika), [SP-Tarkov](https://sp-tarkov.com/), or Battlestate Games. For the supported multiplayer stack, use the official plugin and the [Fika Wiki](https://wiki.project-fika.com/).

[<img src="https://mirrors.creativecommons.org/presskit/buttons/88x31/svg/by-nc-sa.svg" alt="CC BY-NC-SA 4.0" width="120">](https://creativecommons.org/licenses/by-nc-sa/4.0/legalcode.en)

## Divergence

Fika-Neo started from official Fika-Plugin **v2.4.3** (`b968ce16`). The `neo` branch is an independent unofficial line. **It will not stay in full sync with upstream.** Official updates are not merged by default; any later reuse of upstream code will be selective and called out as a modification.

`main` is only the import snapshot of the official tree. Work happens on **`neo`**.

## License

This repository is Adapted Material of Project Fika, licensed under **[CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/legalcode.en)** (ShareAlike). Use is **non-commercial** only. Keep attribution, mark changes, and do not add extra restrictions.

- Legal text: [`LICENSE.md`](LICENSE.md)
- Attribution: [`NOTICE.md`](NOTICE.md)

## Requirements

- [Visual Studio Code](https://code.visualstudio.com/) (or Visual Studio)
- [.NET SDK 10.0.x](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

## Setup

1. Copy the contents of `EscapeFromTarkov_Data/Managed/` into `References/`
2. Copy SPT.Modules `project/Shared/Hollowed/hollowed.dll` into `References/`

## Build

You have to create a `References` folder and populate it with the required dependencies from your game installation for the project to build.

**Tool**   | **Action**
---------- | ------------------------------
PowerShell | `dotnet build`
VSCode     | `Terminal > Run Build Task...`

## Credits

Fika-Neo Plugin is derived from **Project Fika**. Credit the upstream project when you share this work.

**Project** | **License**
----------- | -----------------------------------------------------------------------
[Project Fika / Fika-Plugin](https://github.com/project-fika/Fika-Plugin) | [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/legalcode.en)
SPT.Modules | [NCSA](https://dev.sp-tarkov.com/SPT/Modules/src/branch/master/LICENSE.md)
SIT | [NCSA](./Licenses/LICENSE-SIT.md) (`Forked from SIT.Client master:9de30d8`)
Open.NAT | [MIT](https://github.com/lontivero/Open.NAT/blob/master/LICENSE) (for UPnP implementation)
LiteNetLib | [MIT](https://github.com/RevenantX/LiteNetLib/blob/master/LICENSE.txt) (for P2P UDP implementation)

## Disclaimer

Escape From Tarkov is a trademark of Battlestate Games. This software is provided as-is, without warranty, as stated in CC BY-NC-SA 4.0 Section 5.
