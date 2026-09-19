# Fika-Neo · 插件

[English](README.md) · [中文](README.zh.md)

[project-fika/Fika-Plugin](https://github.com/project-fika/Fika-Plugin) 的非官方 BepInEx **客户端**分支。

配套服务端：[Fika-Server-CSharp-Neo](https://github.com/Neko17awa/Fika-Server-CSharp-Neo)。

**这不是官方 Fika。** 与 [Project Fika](https://github.com/project-fika)、[SP-Tarkov](https://sp-tarkov.com/)、Battlestate Games 均无隶属、赞助或代言关系。需要受支持的联机方案时，请使用官方插件和 [Fika Wiki](https://wiki.project-fika.com/)。

[<img src="https://mirrors.creativecommons.org/presskit/buttons/88x31/svg/by-nc-sa.svg" alt="CC BY-NC-SA 4.0" width="120">](https://creativecommons.org/licenses/by-nc-sa/4.0/legalcode.en)

## 与上游的关系

Fika-Neo 以官方 Fika-Plugin **v2.4.3**（`b968ce16`）为起点。`neo` 是独立的非官方修改线，**不会再与上游保持全量同步。** 默认不合并官方后续更新；若以后局部采用上游代码，会作为修改单独标明。

`main` 只保留导入时的官方快照。开发在 **`neo`** 上进行。

## 许可

本仓库是 Project Fika 的改编作品，协议为 **[CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/legalcode.en)**（相同方式共享）。**仅限非商业使用。** 必须保留署名、标明修改，且不得额外限制该协议已授予的权利。

- 完整文本：[`LICENSE.md`](LICENSE.md)
- 署名声明：[`NOTICE.md`](NOTICE.md)

## 环境

- [Visual Studio Code](https://code.visualstudio.com/)（或 Visual Studio）
- [.NET SDK 10.0.x](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

## 准备

1. 将 `EscapeFromTarkov_Data/Managed/` 中的文件复制到 `References/`
2. 将 SPT.Modules 的 `project/Shared/Hollowed/hollowed.dll` 复制到 `References/`

## 构建

必须先建立 `References` 目录，并放入游戏安装中的依赖，才能编译。

**工具** | **操作**
-------- | ------------------------------
PowerShell | `dotnet build`
VSCode     | `Terminal > Run Build Task...`

## 致谢

Fika-Neo 插件派生自 **Project Fika**。分享本仓库或改编件时，须为上游 Fika 署名。

**项目** | **协议**
-------- | -----------------------------------------------------------------------
[Project Fika / Fika-Plugin](https://github.com/project-fika/Fika-Plugin) | [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/legalcode.en)
SPT.Modules | [NCSA](https://dev.sp-tarkov.com/SPT/Modules/src/branch/master/LICENSE.md)
SIT | [NCSA](./Licenses/LICENSE-SIT.md)（`Forked from SIT.Client master:9de30d8`）
Open.NAT | [MIT](https://github.com/lontivero/Open.NAT/blob/master/LICENSE)（UPnP）
LiteNetLib | [MIT](https://github.com/RevenantX/LiteNetLib/blob/master/LICENSE.txt)（P2P UDP）

## 声明

Escape From Tarkov 为 Battlestate Games 的商标。本软件按 CC BY-NC-SA 4.0 第 5 节「按现状」提供，不作任何担保。
