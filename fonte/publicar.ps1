# Publica o TELAR no repositório justinocosta007/telar (separado do Concorde):
# site (GitHub Pages) + código-fonte + instalador na Release. Uso: .\publicar.ps1
$ErrorActionPreference = 'Continue'
$gh = 'C:\Program Files\GitHub CLI\gh.exe'
$repo = 'justinocosta007/telar'
$root = Split-Path $PSScriptRoot
$log = Join-Path $root 'publicar.log'
Start-Transcript -Path $log -Force | Out-Null

$versao = [int]([regex]::Match((Get-Content "$PSScriptRoot\Telar.cs" -Raw), 'const int Versao = (\d+)').Groups[1].Value)
"Versão $versao"

& $gh repo view $repo --json name 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
  "Criando repositório $repo..."
  & $gh repo create $repo --public --description 'TELAR - jogue na TV usando a força do PC da sala' --add-readme
  Start-Sleep 3
}

# envia (ou atualiza) um arquivo pela API de conteúdo
function Enviar($local, $caminho) {
  $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($local))
  $sha = & $gh api "repos/$repo/contents/$caminho" --jq .sha 2>$null
  $corpo = @{ message = "TELAR v$versao`: $caminho"; content = $b64; branch = 'main' }
  if ($LASTEXITCODE -eq 0 -and $sha) { $corpo.sha = $sha.Trim() }
  $tmp = [IO.Path]::GetTempFileName()
  [IO.File]::WriteAllText($tmp, ($corpo | ConvertTo-Json -Compress), (New-Object Text.UTF8Encoding($false)))
  & $gh api -X PUT "repos/$repo/contents/$caminho" --input $tmp --jq .content.path
  Remove-Item $tmp
}

"Enviando site..."
Enviar "$root\site\index.html" 'index.html'
Enviar "$root\site\telar.exe" 'telar.exe'
Enviar "$root\site\version.json" 'version.json'
"Enviando código-fonte..."
foreach ($f in 'Telar.cs', 'TelaTV.cs', 'Setup.cs', 'build.ps1', 'setup.manifest', 'LICENCAS.txt', 'TELAR.ico', 'publicar.ps1') {
  Enviar "$PSScriptRoot\$f" "fonte/$f"
}

"Ligando o GitHub Pages..."
& $gh api -X POST "repos/$repo/pages" -f 'source[branch]=main' -f 'source[path]=/' --jq .html_url 2>$null
if ($LASTEXITCODE -ne 0) { "  (Pages já estava ligado)" }

"Publicando o instalador na Release v$versao..."
& $gh release view "v$versao" --repo $repo 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
  & $gh release upload "v$versao" "$root\TELAR-Setup.exe" --repo $repo --clobber
} else {
  & $gh release create "v$versao" "$root\TELAR-Setup.exe" --repo $repo --title "TELAR v$versao" --notes "Instalador único do TELAR. Rode nos dois PCs: escolha PC Forte na sala e PC da TV na TV. Tudo vai dentro do instalador; não precisa instalar mais nada." --latest
}
"PRONTO"
Stop-Transcript | Out-Null
