# Publica o TELAR no repositório justinocosta007/telar (separado do Concorde), igual ao Concorde:
# raiz com README, LICENSE, telar-setup.exe e o site (index.html, telar.exe, version.json para a atualização
# automática), código em fonte\ e o instalador também na Release. Uso (no terminal com o gh logado): .\publicar.ps1
$ErrorActionPreference = 'Continue'
$gh = 'C:\Program Files\GitHub CLI\gh.exe'
$env:PATH = 'C:\Program Files\Git\cmd;' + $env:PATH
$repo = 'justinocosta007/telar'
$root = Split-Path $PSScriptRoot
Start-Transcript -Path (Join-Path $root 'publicar.log') -Force | Out-Null

$versao = [int]([regex]::Match((Get-Content "$PSScriptRoot\Telar.cs" -Raw), 'const int Versao = (\d+)').Groups[1].Value)
"Versão $versao"

& $gh auth setup-git | Out-Null
$clone = Join-Path $env:TEMP 'telar-repo'
Remove-Item $clone -Recurse -Force -ErrorAction SilentlyContinue
git clone --depth 1 "https://github.com/$repo.git" $clone
if ($LASTEXITCODE -ne 0) { "Não clonou o repositório."; Stop-Transcript | Out-Null; exit 1 }

"Copiando arquivos..."
Copy-Item "$root\site\index.html", "$root\site\telar.exe", "$root\site\version.json", "$root\README.md", "$root\LICENSE" $clone -Force
Copy-Item "$root\TELAR-Setup.exe" "$clone\telar-setup.exe" -Force
New-Item -ItemType Directory -Force "$clone\fonte" | Out-Null
foreach ($f in 'Telar.cs', 'TelaTV.cs', 'Setup.cs', 'build.ps1', 'setup.manifest', 'LICENCAS.txt', 'TELAR.ico', 'publicar.ps1') {
  Copy-Item "$PSScriptRoot\$f" "$clone\fonte\$f" -Force
}

Push-Location $clone
git config user.name 'justinocosta007'
git config user.email 'justinocosta007@users.noreply.github.com'
git add -A
git commit -q -m "TELAR v$versao" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push origin HEAD:main
$push = $LASTEXITCODE
Pop-Location
if ($push -ne 0) { "O envio pelo Git falhou."; Stop-Transcript | Out-Null; exit 1 }

& $gh api -X POST "repos/$repo/pages" -f 'source[branch]=main' -f 'source[path]=/' --jq .html_url 2>$null | Out-Null

"Publicando o instalador na Release v$versao..."
& $gh release view "v$versao" --repo $repo 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
  & $gh release upload "v$versao" "$root\TELAR-Setup.exe" --repo $repo --clobber
} else {
  & $gh release create "v$versao" "$root\TELAR-Setup.exe" --repo $repo --title "TELAR v$versao" --notes "Instalador único do TELAR. Rode nos dois PCs: escolha PC Forte na sala e PC da TV na TV. Tudo vai dentro do instalador; não precisa instalar mais nada." --latest
}
"PRONTO"
Stop-Transcript | Out-Null
