# TELAR

Jogue na TV usando a força do PC da sala. O PC Forte roda o jogo e transmite a tela inteira, o áudio do sistema e recebe teclado, mouse e controle do PC da TV. Em casa vai até **Quad HD 1440p a 120 fps** (ou **4K a 60 fps**). Fora de casa, pelo Tailscale, vira **acesso remoto para trabalho**.

*Streaming de jogos e da área de trabalho entre dois PCs com Windows: captura por hardware, compressão na placa de vídeo (NVIDIA, AMD ou Intel) e transmissão de baixa latência pela rede local.*

## Baixar / Download

- **Instalador para Windows:** [telar-setup.exe](https://github.com/justinocosta007/telar/raw/main/telar-setup.exe)
- **Página do TELAR:** [https://justinocosta007.github.io/telar/](https://justinocosta007.github.io/telar/)
- **Última versão (Releases):** [TELAR-Setup.exe](https://github.com/justinocosta007/telar/releases/latest/download/TELAR-Setup.exe)

Um arquivo só, com tudo dentro: não precisa instalar mais nenhum programa. O instalador pede permissão de administrador uma vez (o motor de transmissão roda como serviço do Windows), cria os atalhos e aparece em *Configurações do Windows → Aplicativos instalados*. O TELAR se atualiza sozinho a partir deste repositório.

Se o Windows mostrar "O Windows protegeu o computador", clique em **Mais informações** e depois em **Executar assim mesmo**.

## Como usar

1. Rode o instalador nos dois PCs. No PC da sala escolha **PC Forte**; no PC da TV escolha **PC da TV**.
2. No PC da TV, o PC Forte aparece sozinho em **Encontrados**.
3. Clique nele. Na mesma rede **conecta sozinho, sem código**, e abre a tela inteira do PC Forte.
4. Fora de casa (Tailscale) ele pede o **código de 4 números** que aparece no TELAR do PC Forte.

O TELAR do PC Forte abre sozinho (minimizado) quando o Windows liga.

Durante a transmissão: `Ctrl+Alt+Shift+Q` sai, `Ctrl+Alt+Shift+S` mostra as estatísticas, `Ctrl+Alt+Shift+Z` solta o mouse.

## Qualidade

| Modo | Uso | Taxa de bits |
|---|---|---|
| Automático com cabo de rede | Quad HD 1440p 120 fps; cai para 60 fps se a TV não aceitar 120 Hz | ~75 Mbps |
| Automático no Wi-Fi | Full HD 1080p 120 fps (estável no Wi-Fi 5 GHz); 60 fps em PC da TV bem simples | ~32 Mbps |
| 4K 2160p 60 fps | opção em casa | 80 Mbps |
| Modo trabalho | fora de casa pelo Tailscale, na resolução da sua tela, 60 fps | 8 a 15 Mbps |

Todas as opções (resolução, FPS, taxa de bits, V-Sync, HDR, áudio, mouse, controle, codec) ficam em **Configurações** no PC da TV.

## Código-fonte

O código do TELAR está em [`fonte/`](fonte). Para gerar o instalador: `fonte\build.ps1` (usa só o compilador C# que já vem no Windows).

O TELAR leva dentro dele componentes de código aberto, sem modificação: o motor de transmissão [Sunshine](https://github.com/LizardByte/Sunshine) (GPL-3.0), o receptor [Moonlight](https://github.com/moonlight-stream/moonlight-qt) (GPL-3.0) e o driver de controle [ViGEmBus](https://github.com/nefarius/ViGEmBus) (BSD-3). Por isso o TELAR também é distribuído sob a [GPL-3.0](LICENSE).
