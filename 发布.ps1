# ============================================================
#  Lattice Launcher 一键发布
#  产物（发布\）：
#    Lattice启动器.exe        —— 单文件自包含（约 84MB，压缩：体积锁 100MB 内——8-13 批次 34；启动动画独立线程覆盖解压等待），双击即用，无需安装 .NET
#    Lattice启动器-轻量版.exe —— 框架依赖（约 23MB），需装 .NET 10 Desktop Runtime
#  用法：右键 → 使用 PowerShell 运行（或 powershell -ExecutionPolicy Bypass -File 发布.ps1）
# ============================================================
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out  = Join-Path $root "发布"

Write-Host ""
Write-Host "=== Lattice Launcher 发布 ===" -ForegroundColor Cyan

# 0) 自动关闭运行中的启动器（两个版本进程名都匹配；用户不在时自动处理，无需手动关）
$running = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like "Lattice启动器*" }
if ($running) {
    Write-Host "检测到启动器正在运行，自动关闭..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

# 1) 清空旧产物
if (Test-Path $out) { Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $out -Force | Out-Null

# 2) 发布函数：publish → 取 exe → 移动到 发布\ 对应名字（跑两遍：自包含 + 轻量版）
# 8-19 密钥派生盐（必须与 BundledCfKeyGen.KeySalt 一致——改动两处同步）
$script:CfKeySalt = "Lattice.CfKey.Internal.v2"

# 构建注入占位模板（无 key 版本；注入/收尾共用——含 Decrypt/EncryptForTest 完整实现）
$script:CfKeyPlaceholder = @"
using System.Security.Cryptography;
using System.Text;

namespace Launcher.Core.Services;

/// <summary>
/// 构建注入生成——勿手改/勿提交含 key 版本（发布.ps1 覆盖）。
/// 8-19 升级（对齐 PCL2 AES-HMAC 思路）：内置值 = AES-256-CBC + HMAC-SHA256 密文，
/// 密钥由内嵌常量派生（SHA256）——直接 grep 二进制搜不到密钥与明文（旧版 +7 移位可逆 30 秒还原）。
/// </summary>
internal static class BundledCfKeyGen
{
    /// <summary>构建注入字段：AES-HMAC|base64(iv+cipher+hmac)；空 = 无内置 key（用户自填兜底）</summary>
    public static readonly string Encrypted = "";

    /// <summary>预留字段（构建注入模板同步用）</summary>
    public static readonly string Decoy = "Nzg5Ojs8PT4/QGhpamtsbTc4OTo7PD0+P0BoaWprbG0=";

    /// <summary>密钥派生盐（发布脚本同款；改动必须两处同步——发布.ps1 的 Inject-BundledCfKey）</summary>
    private const string KeySalt = "Lattice.CfKey.Internal.v2";

    /// <summary>加密镜像（测试往返用；与发布.ps1 Inject-BundledCfKey 的 PowerShell 实现一一对应：
    /// iv(16)|hmac(32)|cipher，AES-256-CBC + HMAC-SHA256，密钥 = SHA256(KeySalt)）</summary>
    public static string EncryptForTest(string plain)
    {
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(KeySalt));
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        byte[] cipher;
        using (var enc = aes.CreateEncryptor())
            cipher = enc.TransformFinalBlock(Encoding.UTF8.GetBytes(plain), 0, plain.Length);
        using var h = new HMACSHA256(aes.Key);
        var hmac = h.ComputeHash(cipher);
        var outBuf = new byte[16 + 32 + cipher.Length];
        Buffer.BlockCopy(aes.IV, 0, outBuf, 0, 16);
        Buffer.BlockCopy(hmac, 0, outBuf, 16, 32);
        Buffer.BlockCopy(cipher, 0, outBuf, 48, cipher.Length);
        return "AES-HMAC|" + Convert.ToBase64String(outBuf);
    }

    /// <summary>运行时解密（读注入字段）；失败返回空（安全降级 → 用户自填 key）</summary>
    public static string Decrypt() => Decrypt(Encrypted);

    /// <summary>解密指定密文；失败返回空（测试/篡改检测用）</summary>
    public static string Decrypt(string? encrypted)
    {
        try
        {
            if (string.IsNullOrEmpty(encrypted)) return "";
            if (!encrypted.StartsWith("AES-HMAC|", StringComparison.Ordinal)) return "";
            var raw = Convert.FromBase64String(encrypted["AES-HMAC|".Length..]);
            // 布局：iv(16) | hmac(32) | cipher(rest)
            if (raw.Length < 16 + 32 + 16) return "";
            var iv = raw[..16];
            var hmac = raw[16..48];
            var cipher = raw[48..];
            var key = SHA256.HashData(Encoding.UTF8.GetBytes(KeySalt));
            // HMAC 校验（防篡改/防误写）
            using var h = new HMACSHA256(key);
            var expected = h.ComputeHash(cipher);
            if (!CryptographicOperations.FixedTimeEquals(hmac, expected)) return "";
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var dec = aes.CreateDecryptor();
            var plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
            return Encoding.UTF8.GetString(plain);
        }
        catch { return ""; }
    }
}

"@

function Inject-BundledCfKey {
    # 构建注入内置 CF key（PCL/HMCL 式：key 不进源码仓库，发布时从环境变量注入 AES-HMAC 密文覆盖生成文件）
    # 设 LATTICE_CF_KEY 才注入；否则生成空占位（用户自填 key 或走官网跳转平替）。
    $genFile = Join-Path $root "src\Launcher.Core\Services\BundledCfKeyGen.cs"
    $cfKey = $env:LATTICE_CF_KEY
    $enc = ""
    if ($cfKey) {
        # AES-256-CBC + HMAC-SHA256（对齐 PCL2 AES-HMAC 思路）：密钥 = SHA256(盐)，
        # 布局 iv(16)|hmac(32)|cipher —— 与 BundledCfKeyGen.Decrypt() 严格对应
        $sha = [System.Security.Cryptography.SHA256]::Create()
        $key = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($script:CfKeySalt))
        $iv = New-Object byte[] 16
        (New-Object System.Security.Cryptography.RNGCryptoServiceProvider).GetBytes($iv)
        $aes = New-Object System.Security.Cryptography.RijndaelManaged
        $aes.KeySize = 256; $aes.BlockSize = 128
        $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
        $aes.Key = $key; $aes.IV = $iv
        $plainBytes = [Text.Encoding]::UTF8.GetBytes($cfKey)
        $encBytes = $aes.CreateEncryptor().TransformFinalBlock($plainBytes, 0, $plainBytes.Length)
        # PS 5.1 坑：New-Object HMACSHA256($key) 会把 byte[] 拆成参数列表（报"参数为:32"）——用 ::new()
        $hmac = [System.Security.Cryptography.HMACSHA256]::new($key)
        $hmacBytes = $hmac.ComputeHash($encBytes)
        $out = New-Object byte[] (16 + 32 + $encBytes.Length)
        [Array]::Copy($iv, 0, $out, 0, 16)
        [Array]::Copy($hmacBytes, 0, $out, 16, 32)
        [Array]::Copy($encBytes, 0, $out, 48, $encBytes.Length)
        $enc = "AES-HMAC|" + [Convert]::ToBase64String($out)
    }
    $content = $script:CfKeyPlaceholder.Replace('public static readonly string Encrypted = "";',
        "public static readonly string Encrypted = `"$enc`";")
    [System.IO.File]::WriteAllText($genFile, $content, [System.Text.Encoding]::UTF8)
    if ($cfKey) {
        # 注入校验：读回生成文件确认密文已写入（防替换漏执行 → 带空值上生产线）
        $back = Get-Content $genFile -Raw
        if ($back -notmatch ('Encrypted = "' + [regex]::Escape($enc) + '"')) {
            throw "[cf-key] 注入校验失败：生成文件未写入密文——终止发布，检查磁盘权限"
        }
        Write-Host "[cf-key] 已注入内置 CF key（AES-HMAC 加密，来自 LATTICE_CF_KEY），校验通过" -ForegroundColor Green
    } else {
        Write-Host "[cf-key] 未设 LATTICE_CF_KEY → 内置 key 为空（用户自填 / 官网跳转平替）" -ForegroundColor DarkGray
    }
}

function Publish-One([string]$finalName, [switch]$SelfContained) {
    $stage = Join-Path $out "stage"
    $pubDir = Join-Path $root "src\Launcher.App\bin\Release\net10.0-windows\win-x64\publish"
    # 8-18 根治：清 RID obj 缓存强制全量重编译——Avalonia 增量缓存会把 ResourceInclude
    # 资源字典改动漏编译（实机：NavIcons.axaml 菱形图标改动没进 dll，用户验收旧界面）
    Remove-Item (Join-Path $root "src\Launcher.App\obj\Release\net10.0-windows\win-x64") -Recurse -Force -ErrorAction SilentlyContinue
    Inject-BundledCfKey
    # 两步 publish：先出 DLL 形式供发布期处理，再 --no-build 打包单文件
    Write-Host "  [1/3] dotnet publish（非单文件，供混淆）..." -ForegroundColor DarkGray
    & dotnet publish (Join-Path $root "src/Launcher.App") `
        -c Release -r win-x64 --self-contained $SelfContained `
        -p:PublishSingleFile=false `
        -p:RollForward=LatestMajor `
        -p:DebugType=None -p:DebugSymbols=false | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "publish(非单文件) 失败 (exit $LASTEXITCODE)" }
    $coreDll = Join-Path $pubDir "Launcher.Core.dll"
    if (Test-Path $coreDll) {
        Write-Host "  [2/3] Obfuscar 混淆 Launcher.Core.dll..." -ForegroundColor DarkYellow
        & obfuscar.console (Join-Path $root "obfuscar.xml") | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Obfuscar 失败 (exit $LASTEXITCODE)" }
        $obfOut = Join-Path $pubDir "obf-stage\Launcher.Core.dll"
        Move-Item $obfOut $coreDll -Force
        Remove-Item (Join-Path $pubDir "obf-stage") -Recurse -Force -ErrorAction SilentlyContinue
    } else {
        Write-Host "  [警告] 未找到 $coreDll，跳过混淆" -ForegroundColor Yellow
    }
    Write-Host "  [3/3] dotnet publish（单文件打包，含混淆 DLL）..." -ForegroundColor DarkGray
    & dotnet publish (Join-Path $root "src/Launcher.App") `
        -c Release -r win-x64 --self-contained $SelfContained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=$SelfContained `
        -p:RollForward=LatestMajor `
        -p:DebugType=None -p:DebugSymbols=false `
        --no-build -o $stage | Out-Null
    # 注：EnableCompressionInSingleFile 单文件压缩仅支持 self-contained（fdep 会报 NETSDK1176）；
    # 反引号续行内严禁插入注释行（会打断续行链，-p: 被当独立命令）；
    # | Out-Null 必须：dotnet publish 的 stdout 会进函数管道，泄漏到返回值（$finalSelf 变数组导致 Get-Item 报错）。
    if ($LASTEXITCODE -ne 0) { throw "publish(单文件) 失败 (exit $LASTEXITCODE)" }

    $exe = Get-ChildItem (Join-Path $stage "*.exe") | Select-Object -First 1
    if ($null -eq $exe) { Write-Host "[错误] 发布产物中未找到 exe" -ForegroundColor Red; exit 1 }
    # 8-22 防呆：记录构建产物时间，Move 后校验。此前发布显示成功但 exe 停留在旧版
    # （增量缓存/占用静默失败），用户测到的「修复无效」实为旧 exe——产物必须新鲜
    $builtAt = $exe.LastWriteTime
    $final = Join-Path $out $finalName
    # 占用检测：目标被运行中的启动器锁定时明确提示（不再静默失败）
    if (Test-Path $final) {
        try {
            $fs = [System.IO.File]::Open($final, 'Open', 'Read', 'None')
            $fs.Close()
        } catch {
            Write-Host "[错误] $finalName 被占用（启动器正在运行）——请先关闭再运行本脚本" -ForegroundColor Red
            exit 1
        }
    }
    Move-Item $exe.FullName $final -Force
    if ((Get-Item $final).LastWriteTime -lt $builtAt) {
        throw "$finalName 产物未更新（可能是增量缓存或文件被占用）——请重跑发布脚本"
    }
    Remove-Item $stage -Recurse -Force
    return $final
}

Write-Host "[1/5] dotnet publish 自包含版（单文件压缩，约 2-4 分钟）..."
$finalSelf = Publish-One "Lattice启动器.exe" -SelfContained

Write-Host "[2/5] dotnet publish 轻量版（框架依赖，约 1-2 分钟）..."
$finalLite = Publish-One "Lattice启动器-轻量版.exe"

# 3) 签名（复用 LauncherDev 自签名证书；无证书时自动创建）
Write-Host "[3/4] 签名..."
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "scripts/sign-output.ps1") $out

# 4) 使用说明
$sizeSelf = [Math]::Round((Get-Item $finalSelf).Length / 1MB)
$sizeLite = [Math]::Round((Get-Item $finalLite).Length / 1MB)
@"
Lattice Launcher（晶格启动器）
=============================
两个版本，任选其一：

[Lattice启动器.exe]（约 $sizeSelf MB）—— 自包含版，双击即用，无需安装任何东西。
  首次启动会解压运行库到临时目录，需几秒到十几秒，属正常现象。

[Lattice启动器-轻量版.exe]（约 $sizeLite MB）—— 轻量版，体积小，但需要先装
  .NET 10 Desktop Runtime（https://dotnet.microsoft.com/download/dotnet/10.0）。
  没装运行时会弹窗引导下载，装一次即可；以后更新只需下载小包。

[如果被 Windows 阻止]
1. SmartScreen（"Windows 已保护你的电脑"）→ 点「更多信息」→「仍要运行」（自签名发布者，属正常）
2. 智能应用控制（SAC，仅 Win11 新装机器）会无提示阻止——需在 设置→隐私和安全性→Windows 安全中心→应用和浏览器控制→智能应用控制 中关闭（关闭后不可轻易重开，属系统设计）
"@ | Out-File (Join-Path $out "使用说明.txt") -Encoding UTF8

Write-Host "[4/4] 完成！" -ForegroundColor Green
# 注：无 KeyProxy——AL50 已砍本地代理，CF key 并入主进程（DPAPI 加密存设置）
Write-Host "  -> $finalSelf"
Write-Host "  -> $finalLite"
Write-Host "  -> $(Join-Path $out '使用说明.txt')"

# 5) 8-19 安全收尾：注入结束后恢复空占位模板——密文绝不留在工作区。
# （git add -A 会把带值文件整体提交进仓库——8-19 实锤：混淆值可逆，key 泄漏进公开历史；
#  AES-HMAC 密文同样不能留：二进制可逆推理，占位必须恢复）
$genFile = Join-Path $root "src\Launcher.Core\Services\BundledCfKeyGen.cs"
# 模板自带文件尾换行（与 git 模板字节一致，发布后 git status 保持干净）
[System.IO.File]::WriteAllText($genFile, $script:CfKeyPlaceholder, [System.Text.Encoding]::UTF8)
Write-Host "[5/5] 注入占位已恢复（key 不留工作区）" -ForegroundColor DarkGray
