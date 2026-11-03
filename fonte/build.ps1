# Gera o ícone e compila TELAR.exe (usa só o que já vem no Windows)
$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$root = Split-Path $src
Add-Type -AssemblyName System.Drawing

# ícone: quadrado laranja arredondado com um "T" branco inclinado (desenho próprio do TELAR)
$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::Transparent)
function RoundRect($x, $y, $w, $h, $r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $p.AddArc($x, $y, $r, $r, 180, 90); $p.AddArc($x + $w - $r, $y, $r, $r, 270, 90)
  $p.AddArc($x + $w - $r, $y + $h - $r, $r, $r, 0, 90); $p.AddArc($x, $y + $h - $r, $r, $r, 90, 90)
  $p.CloseFigure(); return $p
}
$laranja = [System.Drawing.Color]::FromArgb(245, 124, 32)
$g.FillPath((New-Object System.Drawing.SolidBrush $laranja), (RoundRect 8 8 240 240 72))
$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
# "T" desenhado reto e depois inclinado para a direita
$t = New-Object System.Drawing.Drawing2D.GraphicsPath
$t.FillMode = 'Winding'   # a junção da barra com a haste fica cheia, sem emenda
$t.AddPath((RoundRect 52 52 152 42 18), $false)
$t.AddPath((RoundRect 106 60 46 146 18), $false)
$m = New-Object System.Drawing.Drawing2D.Matrix
$m.Shear(-0.18, 0); $m.Translate(22, 0, [System.Drawing.Drawing2D.MatrixOrder]::Append)
$t.Transform($m)
$g.FillPath($white, $t)
$g.Dispose()
$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $ms.ToArray()

$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]1)
$w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0)
$w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$png.Length); $w.Write([UInt32]22)
$w.Write($png); $w.Flush()
[System.IO.File]::WriteAllBytes("$src\TELAR.ico", $ico.ToArray())

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /optimize+ /codepage:65001 "/out:$root\TELAR.exe" "/win32icon:$src\TELAR.ico" `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.ServiceProcess.dll `
  /r:System.Security.dll /r:Microsoft.CSharp.dll /r:System.Core.dll "$src\Telar.cs" "$src\Visual.cs" "$src\TelaTV.cs"
if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar" }
"OK: $root\TELAR.exe"

# pacote do instalador: TELAR + motor do PC Forte + receptor do PC da TV + driver de controle
$motores = "$root\motores"
$stage = "$env:TEMP\telar-pacote"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$stage\telar", "$stage\motor", "$stage\cliente", "$stage\drivers" | Out-Null
Copy-Item "$root\TELAR.exe" "$stage\telar\TELAR.exe"
Copy-Item "$src\LICENCAS.txt" "$stage\LICENCAS.txt"
Copy-Item "$motores\host\Sunshine\*" "$stage\motor" -Recurse
Remove-Item "$stage\motor\scripts" -Recurse -Force
Copy-Item "$motores\cliente\*" "$stage\cliente" -Recurse
Rename-Item "$stage\cliente\Moonlight.exe" "telar-cliente.exe"
Copy-Item "$motores\drivers\ViGEmBus.exe" "$stage\drivers\ViGEmBus.exe"
$payload = "$env:TEMP\telar-payload.zip"
Remove-Item $payload -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $payload, 'Optimal', $false)

& $csc /nologo /target:winexe /optimize+ /codepage:65001 /main:Setup "/out:$root\TELAR-Setup.exe" "/win32icon:$src\TELAR.ico" "/win32manifest:$src\setup.manifest" `
  "/resource:$payload,payload.zip" `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.ServiceProcess.dll `
  /r:System.Security.dll /r:Microsoft.CSharp.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "$src\Telar.cs" "$src\Visual.cs" "$src\TelaTV.cs" "$src\Setup.cs"
if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar o instalador" }
"OK: $root\TELAR-Setup.exe ($([math]::Round((Get-Item "$root\TELAR-Setup.exe").Length/1MB,1)) MB)"

# site\ = o que vai para o GitHub Pages (repositório telar)
$versao = [int]([regex]::Match((Get-Content "$src\Telar.cs" -Raw), 'const int Versao = (\d+)').Groups[1].Value)
New-Item -ItemType Directory -Force "$root\site" | Out-Null
Copy-Item "$root\TELAR.exe" "$root\site\telar.exe" -Force
[IO.File]::WriteAllText("$root\site\version.json", "{`"version`": $versao, `"exe`": `"telar.exe`"}", (New-Object Text.UTF8Encoding($false)))
"site\ pronto (versão $versao)"
