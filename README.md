# 缪缪桌宠 · Muelsyse Desktop

缪尔赛思的独立 Windows 桌面陪伴程序。当前版本 **1.3.0**，适用于 **Windows 10 / 11 x64**。

![动作预览](docs/actions.png)

## 下载与使用

前往 [最新发布](https://github.com/Suy201/muelsyse-desktop/releases/latest)，下载 **Muelsyse-Desktop-1.3.0-Windows-x64.zip**。这是包含完整程序、正式素材、源码和离线预览的发布包。

完整解压后双击 `Muelsyse.exe`，无需另外安装 .NET。请保留 `assets` 文件夹与 EXE 的相对位置。GitHub 自动生成的 Source code 压缩包是源码，不含可直接启动的 EXE。

- 左键点击互动，按住拖动桌宠。
- 右键 → **观察手记 · 状态与额度** → **动作预览**：悬停查看浮动预览，点击播放。
- 12 项动作：打招呼、整理头发、轻轻歪头、微笑、整理叶饰、观察掌心水滴、捧杯喝水、轻轻伸展、叶尖听风、掌心蓄露、等一阵微风、递一颗糖。
- 自动待机在完整动作结束后随机等待 10–20 秒，不连续重复上一项动作。
- 随身手记、健康提醒、透明浮窗、置顶和显示大小设置。
- 可连接本机 Codex，查看任务状态和额度，提供新任务与实时语音入口。未连接时仍可使用桌宠互动。

Codex 相关功能需要本机安装并登录 Codex。实时语音使用 Codex 已配置的语音聊天快捷键；首次请在 Codex 设置中配置。功能可用性取决于客户端和账号。桌宠不附带账号或登录凭据。

设置保存在 `%LOCALAPPDATA%\MuelsysePet`。更新时退出旧版，解压新版后启动即可。

## 1.3.0 更新

新增已验收的“藏进热水壶 / 从壶中出来”：拖到屏幕边缘松手或手动隐藏，缪缪由完整流形整体融合为清水，再流入热水壶。拖回屏幕内、点击壶或取消隐藏后恢复本体。支持中途反向，暂停设置保留。

![入壶关键姿态](docs/kettle.png)

144 帧、单程 4.8 秒，使用 14 张完整姿态与整帧补间。全身细节同步柔化，没有脸部先化水、孤立头部或螺旋尾。正常本体端点与原图逐像素一致。原有 12 项日常动作及结束后等待 10–20 秒、不连续重复的规则保持。

完整包新增 `入壶动画预览/index.html`，支持离线查看和慢放。

## 从源码构建

动画二进制统一随完整 Release 分发。克隆仓库后，先从发布 ZIP 中将 `Muelsyse-Desktop/assets/` 的内容复制到仓库的 `assets/` 文件夹。发布 ZIP 的 `source/` 与 `assets/` 已经相邻，也可直接在解压目录构建。

在 Windows 上安装 .NET 9 SDK 后，在仓库根目录运行：

```powershell
dotnet publish source/Muelsyse.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

运行 `dist/Muelsyse.exe`。正式素材位于 `assets/`，会随构建复制。

## 验证与发布内容

174 项逻辑/素材自检、84 项真实窗口检查通过，包括靠边触发、进出反向、暂停保持和恢复端点。动画已获得用户确认。报告见 `验证报告/`；其中首次界面检查与 runtime.json 为 1.2.6 历史记录，当前版本以 self-test.json、ui.json 和 kettle-validation.json 为准。

完整发布包包含程序、58 个正式素材文件、源码、离线预览与当前检查记录。账号凭据、本机设置、调试符号和内部工作记录不随公开包发布。ZIP 的 SHA-256 另附于 Release，包内 `SHA256.json` 可逐文件校验。

角色与素材信息见 [NOTICE.md](NOTICE.md)，详细操作见 [使用说明](使用说明.md)。
