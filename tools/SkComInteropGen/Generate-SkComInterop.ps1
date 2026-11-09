<#
  群益 SKCOM 的 COM 互通定義產生器(Windows PowerShell 5.1)。

  讀群益附的 Interop.SKCOMLib.dll 中繼資料(ReflectionOnlyLoadFrom,不執行它的任何程式碼),依 skcom-interop.psd1 的規格
  產生 src\Ozakboy.TradeKit.Capital.Host\Interop\ 底下的 SkCom*Interop.cs。群益的 DLL 不進 repo,只有產生出來的 .cs 進版控。

  規則:
  - 雙重介面(dual)走 vtable 呼叫,方法「順序」必須與型別庫一致:依 MetadataToken 排序逐格輸出。
  - 只有規格 Real 清單裡的方法給真簽章;其餘一律輸出 Unused<名稱>(IntPtr ...) 佔位(參數個數照原方法、型別一律 IntPtr,
    不影響 vtable 位置,絕對不要呼叫)。
  - 事件介面(IDispatch)只輸出規格 Members 清單裡的事件,DispId 照型別庫。
  - 結構依型別庫的欄位順序、型別、MarshalAs、StructLayout Pack / CharSet 輸出。
  - 規格裡帶 Forbidden 的介面(ISKOrderLib),Real 清單出現任何一個 Forbidden 字樣(不分大小寫)就中止:
    送單、刪單、改價、減量、出入金這類方法只能是佔位。
  - 規格寫錯(方法名稱不存在、型別對不上)一律中止,不產生半套檔案。

  用法(在 repo 根目錄):
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\SkComInteropGen\Generate-SkComInterop.ps1 `
      -TypeLibInterop '<群益 SDK>\元件\x64\Interop.SKCOMLib.dll'
  產生後跑 dotnet build -c Release 與全部測試;git diff 看 vtable 有沒有因為 SKCOM 改版而位移。
#>
param(
    [Parameter(Mandatory = $true)][string]$TypeLibInterop,
    [string]$Spec = '',
    [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 1

# Windows PowerShell 5.1 在 param 預設值裡拿不到 $PSScriptRoot,改在這裡補預設
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrEmpty($Spec)) { $Spec = Join-Path $scriptDirectory 'skcom-interop.psd1' }
if ([string]::IsNullOrEmpty($OutDir)) { $OutDir = Join-Path $scriptDirectory '..\..\src\Ozakboy.TradeKit.Capital.Host\Interop' }

# 變數名稱不分大小寫:不能叫 $spec,否則會被轉型成參數 $Spec 的 [string]
$specData = Import-PowerShellDataFile -Path $Spec
$assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom((Resolve-Path -LiteralPath $TypeLibInterop).Path)
$namespace = 'SKCOMLib'
$keywords = @('object', 'string', 'event', 'params', 'ref', 'out', 'in', 'base', 'this', 'lock', 'fixed', 'operator', 'checked', 'class', 'struct', 'int', 'short', 'long')
$structsGenerated = @{}
$structsUsed = @{}

function Get-TypeLibType([string]$name) {
    $type = $assembly.GetType("$namespace.$name")
    if ($null -eq $type) { throw "型別庫裡沒有 $name" }
    return $type
}

function Get-AttributeValue($member, [string]$attributeName) {
    $data = $member.GetCustomAttributesData() | Where-Object { $_.AttributeType.Name -eq $attributeName } | Select-Object -First 1
    if ($null -eq $data) { return $null }
    return $data.ConstructorArguments[0].Value
}

function Get-OrderedMethods($type) {
    return @($type.GetMethods() | Where-Object { $_.DeclaringType -eq $type } | Sort-Object MetadataToken)
}

function Format-Identifier([string]$name) {
    if ($keywords -contains $name) { return "@$name" }
    return $name
}

function Format-ClrType([Type]$type, [string]$context) {
    switch ($type.FullName) {
        'System.Int16' { return 'short' }
        'System.Int32' { return 'int' }
        'System.Int64' { return 'long' }
        'System.Double' { return 'double' }
        'System.Boolean' { return 'bool' }
        'System.String' { return 'string' }
        'System.Void' { return 'void' }
    }

    if ($type.IsValueType -and $type.Namespace -eq $namespace) {
        $structsUsed[$type.Name] = $context
        return $type.Name
    }

    throw "不支援的型別 $($type.FullName)($context)"
}

function Assert-BStr($member, [string]$context) {
    $marshal = Get-AttributeValue $member 'MarshalAsAttribute'
    if ([int]$marshal -ne 19) { throw "$context 的字串不是 BSTR(MarshalAs = $marshal)" }
}

function Format-Parameter($parameter, [bool]$forceOut, [string]$context) {
    $type = $parameter.ParameterType
    $direction = ''

    if ($type.IsByRef) {
        $type = $type.GetElementType()
        # 型別庫的 [out] 才是 out;[in,out] 是 ref。實測用 out 呼叫過的成員由規格 OutOverride 指定,保留原簽章。
        if ($forceOut -or ($parameter.IsOut -and -not $parameter.IsIn)) { $direction = 'out ' } else { $direction = 'ref ' }
    }

    $clr = Format-ClrType $type $context
    $marshal = ''

    if ($clr -eq 'string') {
        Assert-BStr $parameter "$context.$($parameter.Name)"
        $marshal = '[MarshalAs(UnmanagedType.BStr)] '
    }
    elseif ($clr -eq 'bool') {
        $marshal = '[MarshalAs(UnmanagedType.VariantBool)] '
    }

    return "$marshal$direction$clr $(Format-Identifier $parameter.Name)"
}

function Add-Summary([System.Collections.Generic.List[string]]$lines, [string]$indent, $summary) {
    if (-not [string]::IsNullOrWhiteSpace($summary)) {
        $lines.Add("$indent/// <summary>$summary</summary>")
    }
}

# 一行放得下(140 字元內)就一行;放不下就每個參數一行,比照手寫時的排版
function Add-Signature($lines, [string]$head, $parameters) {
    $single = "    $head($($parameters -join ', '));"
    if ($single.Length -le 140 -or $parameters.Count -eq 0) {
        $lines.Add($single)
        return
    }

    $lines.Add("    $head(")
    for ($i = 0; $i -lt $parameters.Count; $i++) {
        $suffix = ','
        if ($i -eq $parameters.Count - 1) { $suffix = ');' }
        $lines.Add("        $($parameters[$i])$suffix")
    }
}

function Write-Ids($lines, $ids) {
    Add-Summary $lines '' $ids.Summary
    $lines.Add("internal static class $($ids.Name)")
    $lines.Add('{')
    $first = $true

    foreach ($entry in $ids.Entries) {
        if (-not $first) { $lines.Add('') }
        $first = $false
        $guid = Get-AttributeValue (Get-TypeLibType $entry.Type) 'GuidAttribute'
        Add-Summary $lines '    ' $entry.Summary
        $lines.Add("    public static readonly Guid $($entry.Field) = new(""$($guid.ToUpperInvariant())"");")
    }

    $lines.Add('}')
}

function Write-DualInterface($lines, $definition) {
    $type = Get-TypeLibType $definition.TypeLib
    $flags = [int](Get-AttributeValue $type 'TypeLibTypeAttribute')
    if (($flags -band 0x40) -eq 0) { throw "$($definition.TypeLib) 不是雙重介面(TypeLibType = $flags)" }

    $methods = Get-OrderedMethods $type
    $real = $definition.Real
    $outOverride = @()
    if ($definition.ContainsKey('OutOverride')) { $outOverride = @($definition.OutOverride) }
    $forbidden = @()
    if ($definition.ContainsKey('Forbidden')) { $forbidden = @($definition.Forbidden) }
    $names = @($methods | ForEach-Object { $_.Name })

    foreach ($name in $real.Keys) {
        if ($names -notcontains $name) { throw "$($definition.TypeLib) 沒有方法 $name" }
        foreach ($word in $forbidden) {
            if ($name.IndexOf($word, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "$($definition.TypeLib).$name 含禁用字樣 ${word}(這類方法只能是 Unused 佔位)"
            }
        }
    }

    foreach ($name in $outOverride) {
        if (-not $real.ContainsKey($name)) { throw "OutOverride 的 $name 不在 Real 清單" }
    }

    Add-Summary $lines '' $definition.Summary
    $lines.Add('[ComImport]')
    $lines.Add("[Guid(""$((Get-AttributeValue $type 'GuidAttribute').ToUpperInvariant())"")]")
    $lines.Add('[InterfaceType(ComInterfaceType.InterfaceIsDual)]')
    $lines.Add("internal interface $($definition.Name)")
    $lines.Add('{')
    $placeholderNames = @{}
    $slot = 0

    foreach ($method in $methods) {
        $slot++
        if ($slot -gt 1) { $lines.Add('') }
        $dispId = Get-AttributeValue $method 'DispIdAttribute'
        $context = "$($definition.TypeLib).$($method.Name)"

        if ($real.ContainsKey($method.Name)) {
            Add-Summary $lines '    ' $real[$method.Name]
            $lines.Add("    [DispId($dispId)]")
            $returns = Format-ClrType $method.ReturnType $context
            if ($returns -eq 'string') {
                Assert-BStr $method.ReturnParameter "$context(回傳)"
                $lines.Add('    [return: MarshalAs(UnmanagedType.BStr)]')
            }
            $forceOut = $outOverride -contains $method.Name
            $parameters = @($method.GetParameters() | ForEach-Object { Format-Parameter $_ $forceOut $context })
            Add-Signature $lines "$returns $($method.Name)" $parameters
        }
        else {
            $short = $method.Name
            if ($definition.Prefix -and $short.StartsWith($definition.Prefix)) { $short = $short.Substring($definition.Prefix.Length) }
            $placeholder = "Unused$short"
            if ($placeholderNames.ContainsKey($placeholder)) { throw "佔位名稱重複:$placeholder" }
            $placeholderNames[$placeholder] = $true
            $parameters = @($method.GetParameters() | ForEach-Object { "IntPtr $(Format-Identifier $_.Name)" })
            $lines.Add("    [DispId($dispId)]")
            Add-Signature $lines "int $placeholder" $parameters
        }
    }

    $lines.Add('}')
}

function Write-EventInterface($lines, $definition) {
    $type = Get-TypeLibType $definition.TypeLib
    $methods = Get-OrderedMethods $type
    $members = $definition.Members
    $names = @($methods | ForEach-Object { $_.Name })

    foreach ($name in $members.Keys) {
        if ($names -notcontains $name) { throw "$($definition.TypeLib) 沒有事件 $name" }
    }

    Add-Summary $lines '' $definition.Summary
    $lines.Add('[ComImport]')
    $lines.Add("[Guid(""$((Get-AttributeValue $type 'GuidAttribute').ToUpperInvariant())"")]")
    $lines.Add('[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]')
    $lines.Add("internal interface $($definition.Name)")
    $lines.Add('{')
    $first = $true

    foreach ($method in $methods) {
        if (-not $members.ContainsKey($method.Name)) { continue }
        if (-not $first) { $lines.Add('') }
        $first = $false
        $context = "$($definition.TypeLib).$($method.Name)"
        Add-Summary $lines '    ' $members[$method.Name]
        $lines.Add("    [DispId($(Get-AttributeValue $method 'DispIdAttribute'))]")
        $parameters = @($method.GetParameters() | ForEach-Object { Format-Parameter $_ $false $context })
        Add-Signature $lines "void $($method.Name)" $parameters
    }

    $lines.Add('}')
}

function Write-Struct($lines, $definition) {
    $type = Get-TypeLibType $definition.TypeLib
    $layout = $type.StructLayoutAttribute
    if ($layout.Value -ne [Runtime.InteropServices.LayoutKind]::Sequential) { throw "$($definition.TypeLib) 不是 Sequential" }

    Add-Summary $lines '' $definition.Summary
    $lines.Add("[StructLayout(LayoutKind.Sequential, Pack = $($layout.Pack), CharSet = CharSet.$($layout.CharSet))]")
    $lines.Add("internal struct $($definition.TypeLib)")
    $lines.Add('{')
    $fields = @($type.GetFields([Reflection.BindingFlags]'Instance,Public,NonPublic') | Sort-Object MetadataToken)

    foreach ($field in $fields) {
        $clr = Format-ClrType $field.FieldType "$($definition.TypeLib).$($field.Name)"
        if ($clr -eq 'string') {
            Assert-BStr $field "$($definition.TypeLib).$($field.Name)"
            $lines.Add("    [MarshalAs(UnmanagedType.BStr)] public string $($field.Name);")
        }
        else {
            $lines.Add("    public $clr $($field.Name);")
        }
    }

    $lines.Add('}')
    $structsGenerated[$definition.TypeLib] = $true
}

$outputs = @()

foreach ($file in $specData.Files) {
    $lines = New-Object 'System.Collections.Generic.List[string]'
    $lines.Add('using System.Runtime.InteropServices;')
    $lines.Add('')
    $lines.Add('namespace Ozakboy.TradeKit.Capital.Host.Interop;')
    $lines.Add('')
    $lines.Add("// 此檔由 tools/SkComInteropGen/Generate-SkComInterop.ps1 依 $($specData.TypeLibVersion) 型別庫(群益附的 Interop.SKCOMLib.dll")
    $lines.Add('// 中繼資料)產生,勿手改:要改請改 tools/SkComInteropGen/skcom-interop.psd1 後重新產生。群益的 DLL 不進 repo。')
    $lines.Add('// 雙重介面走 vtable 呼叫,方法順序照型別庫(MetadataToken);用不到的方法是 Unused* 佔位(型別一律 IntPtr,不影響 vtable 位置,')
    $lines.Add('// 絕對不要呼叫);事件介面只宣告要接的事件(沒宣告的 DispId 由 COM 回 DISP_E_MEMBERNOTFOUND,無害)。')
    $lines.Add('// Generated from the SKCOM type library metadata by tools/SkComInteropGen; do not edit by hand. Dual interfaces are')
    $lines.Add('// called through the vtable, so method order follows the type library; unused slots are Unused* placeholders.')

    foreach ($headerLine in $file.Header) {
        $lines.Add("// $headerLine")
    }

    if ($file.ContainsKey('Ids')) {
        $lines.Add('')
        Write-Ids $lines $file.Ids
    }

    foreach ($definition in $file.Types) {
        $lines.Add('')
        switch ($definition.Kind) {
            'Dual' { Write-DualInterface $lines $definition }
            'Events' { Write-EventInterface $lines $definition }
            'Struct' { Write-Struct $lines $definition }
            default { throw "不認得的 Kind:$($definition.Kind)" }
        }
    }

    $outputs += , @{ Path = (Join-Path $OutDir $file.Name); Text = (($lines -join "`n") + "`n") }
}

foreach ($used in $structsUsed.Keys) {
    if (-not $structsGenerated.ContainsKey($used)) { throw "結構 $used 被 $($structsUsed[$used]) 用到,但規格沒有產生它" }
}

# 全部檢查通過才寫檔(UTF-8 無 BOM、LF),不留半套
$encoding = New-Object System.Text.UTF8Encoding($false)
foreach ($output in $outputs) {
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($output.Path), $output.Text, $encoding)
    Write-Output "已產生 $($output.Path)"
}
