# 贪婪赊账 / GreedyCredit

> 项目分类：个人项目 / 《杀戮尖塔 2》Mod / 商店机制扩展 / 可直接使用

一个允许在商店中“欠钱消费”的 Mod。购买和删牌时可以让金币降到负数，并用游戏内置“贪婪”诅咒表示债务压力。

## 当前版本

- [v0.2.1](https://github.com/Yummn/sts2-greedy-credit/releases/tag/v0.2.1)
- [全部 Releases](https://github.com/Yummn/sts2-greedy-credit/releases)

## 功能

- 商店购买和删牌费用允许金币降到负数；
- 每 50 金币债务区间加入一张游戏内置“贪婪”诅咒，`-1..-50` 会立即获得 1 张；
- 再次进入商店时，会按已有金币自动偿还债务，并移除本 Mod 追踪的贪婪诅咒；
- 修复删牌费用不足时负数金币被原版逻辑重置为 0 的问题；
- 不依赖 BaseLib，不含 PCK。

## 兼容版本

当前发布包覆盖：

- Android v0.103.x
- Android v0.110.1
- PC v0.107.x

请按 Release 文件名选择对应版本。

## 安装

1. 下载对应平台的 ZIP。
2. 解压后，将其中的 `GreedyCredit` 文件夹完整复制到游戏 `mods/` 目录。
3. 在启动器中启用模组。
