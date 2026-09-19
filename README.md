# Fika-Neo · Plugin

Unofficial client (BepInEx) fork of [project-fika/Fika-Plugin](https://github.com/project-fika/Fika-Plugin). Companion server: [Fika-Server-CSharp-Neo](https://github.com/Neko17awa/Fika-Server-CSharp-Neo).

**This is not official Fika.** It is not affiliated with, sponsored by, or endorsed by [Project Fika](https://github.com/project-fika), [SP-Tarkov](https://sp-tarkov.com/), or Battlestate Games. For the supported stack, use the official plugin and the [Fika Wiki](https://wiki.project-fika.com/).

这是 [Fika-Plugin](https://github.com/project-fika/Fika-Plugin) 的非官方客户端分支（Fika-Neo）。对应服务端为 [Fika-Server-CSharp-Neo](https://github.com/Neko17awa/Fika-Server-CSharp-Neo)。**不是**官方 Fika；需要官方联机请走上游仓库和 Wiki。

[<img src="https://mirrors.creativecommons.org/presskit/buttons/88x31/svg/by-nc-sa.svg" alt="CC BY-NC-SA 4.0" width="120">](https://creativecommons.org/licenses/by-nc-sa/4.0/legalcode.en)

| Branch | Role |
| --- | --- |
| `main` | Tracks [upstream `main`](https://github.com/project-fika/Fika-Plugin) |
| `neo` | **Unofficial modification branch** (default) |

Current upstream snapshot: `v2.4.3` (`b968ce16`). Plugin source on the first `neo` commit matches that snapshot; later `neo` commits are Fika-Neo changes.

Sync:

```powershell
git fetch upstream
git checkout main
git merge --ff-only upstream/main
git checkout neo
git merge main
```

## License / 许可

Adapted from Project Fika under **[CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/legalcode.en)** (ShareAlike: same license elements). Non-commercial use only. Keep attribution, mark modifications, and do not add extra restrictions. Legal text: [`LICENSE.md`](LICENSE.md). Attribution: [`NOTICE.md`](NOTICE.md).

本仓库是上游的改编作品，仅限非商业使用，必须署名、标明修改、以 CC BY-NC-SA 4.0 再分发。

---

# Upstream: Fika — BepInEx plugin

Client-side changes to make multiplayer work. The sections below are the original Project Fika build notes, kept so the unofficial fork stays usable.

## State of the project

Fully functional with minimal bugs.

- All base game features are replicating and working properly
- Unique interpolation system inspired by the id Tech 3 networking model
- Extremely efficient bandwidth usage
- Headless client to off-load AI (see [Fika-Headless](https://github.com/project-fika/Fika-Headless) repo)
- Base game bug fixes that have been unfixed for years
- Base game performance fixes
- DNS support
- Works with all mods that are developed without hacky workarounds

## Supported OS

Fika is meant to be ran on Windows 10/11. Any other OS might work, but is not officially supported nor do we develop for them. Please respect this when creating an issue/bug report.

## Requirements

- [Visual Studio Code](https://code.visualstudio.com/)
- [.NET SDK 10.0.x](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

## Setup

1. Copy-paste the contents of `EscapeFromTarkov_Data/Managed/` into `References/`
2. Copy-paste from SPT.Modules `project/Shared/Hollowed/hollowed.dll` into `References/`

## Build

### Debug / Release

**Tool**   | **Action**
---------- | ------------------------------
PowerShell | `dotnet build`
VSCode     | `Terminal > Run Build Task...`

You have to create a `References` folder and populate it with the required dependencies from your game installation for the project to build.

## Credits (from upstream)

**Project** | **License**
----------- | -----------------------------------------------------------------------
SPT.Modules | [NCSA](https://dev.sp-tarkov.com/SPT/Modules/src/branch/master/LICENSE.md)
SIT         | [NCSA](./Licenses/LICENSE-SIT.md) (`Forked from SIT.Client master:9de30d8`)
Open.NAT    | [MIT](https://github.com/lontivero/Open.NAT/blob/master/LICENSE) (for UPnP implementation)
LiteNetLib  | [MIT](https://github.com/RevenantX/LiteNetLib/blob/master/LICENSE.txt) (for P2P UDP implementation)

## Disclaimer / 声明

Escape From Tarkov is a trademark of Battlestate Games. Fika-Neo Plugin is an unofficial, non-commercial adaptation for private modification and study, provided as-is under CC BY-NC-SA 4.0 Section 5.

Escape From Tarkov 为 Battlestate Games 的商标。本仓库仅用于非商业的私人修改与研究，按现状提供，不作任何担保。
