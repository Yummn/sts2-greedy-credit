# 贪婪赊账（Greedy Credit）

金币不够也想把商店里的关键牌带走？这个 MOD 允许购买商品和删牌时把金币花到负数，但赊账不是免费的：欠款会按区间往牌组里加入游戏自带的“贪婪”诅咒。

## 规则

- 商店商品和删牌服务都可以正常赊账。
- 每欠 50 金币记一档惩罚：`-1` 到 `-50` 金币为 1 张“贪婪”，`-51` 到 `-100` 为 2 张，以此类推。
- 再次进入商店时会根据当前金币重新结算欠款；已经还清的部分会移除由本 MOD 追踪的“贪婪”。
- 不会把原本就在牌组里的同名诅咒当作债务删除。
- 不依赖 BaseLib，也不包含 PCK 资源。

当前版本为 [v0.2.1](https://github.com/Yummn/sts2-greedy-credit/releases/tag/v0.2.1)，支持 Android v0.103.2、Android v0.110.1 和 PC v0.107.x。请按压缩包文件名选择对应平台，DLL 不能跨版本混用。

## 安装

下载对应 Release，解压后把完整的 `GreedyCredit` 文件夹放进游戏的 `mods` 目录，并在启动器中启用。

## 从源码构建

准备好对应版本的游戏程序集后，在仓库根目录运行：

```powershell
dotnet build GreedyCredit.csproj -c Release
```

也可以参考 `local.props.template` 配置本机路径，再使用 `scripts/build.ps1` 打包。
