# CountdownDisplay 倒计时显示器

一个常驻桌面的 WPF 倒计时 / 时钟小组件。窗口透明、无边框、不抢焦点，可设置在桌面层级或始终置顶，并支持点击穿透。

## 功能

- 倒计时目标时间、标题、显示单位与显示模式可配置
- 独立的桌面时钟窗口
- 始终置顶 / 桌面层级两种模式
- 点击穿透、位置与字号可调
- Win+D 显示桌面时保持可见
- 开机自启（可切换）
- 托盘菜单：打开设置、显示/隐藏倒计时与时钟、退出
- 单实例运行，重复启动会打开已有实例的设置窗口

## 环境要求

- Windows 10 / 11 x64
- .NET 10 SDK（仅构建时需要；发布版自带运行时）

## 构建

```powershell
dotnet build -c Release
```

生成自包含单文件发布包：

```powershell
dotnet publish CountdownDisplay.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
```

## 配置

配置保存在 **exe 所在目录** 的 `settings.json`，不再写入 C 盘用户目录。

首次启动会自动从旧位置迁移：

```
C:\Users\<用户名>\AppData\Roaming\CountdownDisplay\settings.json
```

迁移完成后会删除旧的 `%APPDATA%\CountdownDisplay` 文件夹。迁移会校验 JSON、逐字节确认新文件写入成功后才删除旧文件；异常或非空目录不会误删。

## 测试

桌面窗口恢复检查（会创建临时窗口，需要 Explorer 正在运行）：

```powershell
dotnet run --project .\checks\DesktopRecoveryChecks.csproj -c Release
```
