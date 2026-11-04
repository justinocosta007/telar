// TELAR - PC da TV, no estilo AnyDesk: barra para digitar o endereço do PC Forte, "Este dispositivo",
// abas Encontrados / Sessões Recentes / Favoritos com os PCs Fortes em cartões (Online / Offline), e as Configurações.
// Não existe código: na rede de casa ou pelo Tailscale o clique no PC Forte já conecta sozinho.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

class PcInfo
{
    public string Ip, Nome;
    public bool Online, Pareado, Ocupado, Favorito;
    public bool Ligado; // o PC responde na rede, mas o motor do TELAR não (TELAR desligado nele)
    public long Ultimo; // última vez que jogou nele (ticks), para "Sessões Recentes"
}

// ---------------- janela principal do PC da TV ----------------
class TelaTV : Casca
{
    readonly Config cfg;
    Panel config;
    readonly List<PcInfo> lista = new List<PcInfo>();
    // status dos PCs conhecidos a cada 10 s (bem leve: uma conexão por PC); a varredura da rede inteira
    // só quando não achou nenhum, no máximo 1x por minuto. Parado enquanto joga.
    readonly System.Windows.Forms.Timer relogio = new System.Windows.Forms.Timer { Interval = 10000 };
    bool procurando;
    DateTime ultimaCompleta = DateTime.MinValue;
    Process jogo; // receptor aberto: enquanto joga, o TELAR fica quieto para não roubar rede nem processador
    DateTime inicioJogo;
    string desenhado = "";

    static readonly Color Fundo = Cores.Fundo;
    static readonly Color Barra = Cores.Laranja;
    static readonly Color Cinza = Color.FromArgb(150, 150, 150);
    const string AvisoPadrao = "Clique num PC Forte para jogar.   Durante o jogo: Ctrl+Alt+Shift+Q sai  ·  Ctrl+Alt+Shift+Z solta o mouse  ·  Ctrl+Alt+Shift+S estatísticas";

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);

    public TelaTV(Config c) : base("TELAR · PC da TV")
    {
        cfg = c;
        // da v9 em diante o automático já é Quad HD 1440p: quem tinha escolhido 1440p à mão volta para o automático
        if (cfg.Get("tv.res") == "1440") { cfg.Set("tv.res", "auto"); cfg.Salvar(); }
        string ip = Util.IpLocal();
        Heroi("Este dispositivo", ip == "" ? "sem rede" : ip);
        BotaoHeroi("", "Como usar").Click += delegate { Ajuda(); };
        BotaoHeroi("", "Procurar PCs agora").Click += delegate { Procurar(); };
        Aviso(AvisoPadrao, Cores.Laranja);
        MarcadorEndereco("Digite o endereço remoto (IP do PC Forte)");

        foreach (var s in cfg.Get("tv.pcs").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var x = s.Split('|');
            if (x.Length < 2) continue;
            long ult = 0; if (x.Length > 4) long.TryParse(x[4], out ult);
            lista.Add(new PcInfo { Ip = x[0], Nome = x[1], Pareado = x.Length > 2 && x[2] == "1", Favorito = x.Length > 3 && x[3] == "1", Ultimo = ult });
        }

        AbaMudou += delegate { Redesenhar(); };
        Conectar += endereco => ConectarEndereco(endereco);
        AbrirMenu += delegate { MostrarMenu(); };
        DefinirAbas("Encontrados", "Sessões Recentes", "Favoritos");

        relogio.Tick += delegate
        {
            if (WindowState == FormWindowState.Minimized || cfg.Get("tv.procurar") == "0" || Jogando) return;
            bool algumOnline = lista.Any(p => p.Online);
            Procurar(!algumOnline && (DateTime.Now - ultimaCompleta).TotalSeconds >= 55);
        };
        FormClosing += delegate { Salvar(); };
        Shown += delegate
        {
            if (!File.Exists(App.ClienteExe)) MessageBox.Show(this, "Falta o receptor do TELAR. Rode o TELAR-Setup.exe de novo.", "TELAR");
            Procurar(); relogio.Start();
        };
    }

    string G(string chave, string padrao) { string v = cfg.Get("tv." + chave); return v == "" ? padrao : v; }
    bool B(string chave, bool padrao) { string v = cfg.Get("tv." + chave); return v == "" ? padrao : v == "1"; }

    void Salvar()
    {
        cfg.Set("tv.pcs", string.Join(";", lista.Select(p => p.Ip + "|" + (p.Nome ?? p.Ip).Replace("|", " ").Replace(";", " ") + "|" + (p.Pareado ? "1" : "0") + "|" + (p.Favorito ? "1" : "0") + "|" + p.Ultimo).ToArray()));
        cfg.Salvar();
    }

    void MostrarMenu()
    {
        var m = new ContextMenuStrip();
        m.Items.Add("Configurações", null, delegate { MostrarConfig(); });
        m.Items.Add("Adicionar PC pelo endereço...", null, delegate { AdicionarManual(); });
        m.Items.Add("Procurar PCs agora", null, delegate { Procurar(); });
        m.Items.Add("Como usar", null, delegate { Ajuda(); });
        m.Show(bMenu, new Point(bMenu.Width - m.Width, bMenu.Height));
    }

    // ---------------- transmissão aberta ----------------
    // tem transmissão aberta? (inclusive uma de antes de o TELAR reiniciar para se atualizar)
    bool Jogando
    {
        get
        {
            try { if (jogo != null && !jogo.HasExited) return true; } catch { }
            return Process.GetProcessesByName("telar-cliente").Length > 0 && ReceptorTransmitindo() != null;
        }
    }

    static Process ReceptorTransmitindo()
    {
        try
        {
            using (var q = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='telar-cliente.exe'"))
                foreach (ManagementObject o in q.Get())
                    if (((o["CommandLine"] as string) ?? "").Contains(" stream ")) return Process.GetProcessById(Convert.ToInt32(o["ProcessId"]));
        }
        catch { }
        return null;
    }

    static void TrazerParaFrente(Process p)
    {
        try { p.Refresh(); if (p.MainWindowHandle != IntPtr.Zero) { ShowWindow(p.MainWindowHandle, 9); SetForegroundWindow(p.MainWindowHandle); } } catch { }
    }

    // ---------------- cartões dos PCs ----------------
    void Redesenhar()
    {
        if (!cartoes.Visible || IsDisposed) return;
        Status(lista.Any(p => p.Online));
        IEnumerable<PcInfo> quais =
            abaAtual == 1 ? lista.Where(p => p.Ultimo > 0).OrderByDescending(p => p.Ultimo) :
            abaAtual == 2 ? lista.Where(p => p.Favorito).OrderBy(p => p.Nome) :
            lista.OrderByDescending(p => p.Online).ThenByDescending(p => p.Ligado).ThenBy(p => p.Nome);
        quais = quais.ToList();
        // só refaz os cartões se algo mudou (refazer à toa deixava a janela pesada)
        string marca = abaAtual + "|" + procurando + "|" + string.Join(";", quais.Select(p => p.Ip + p.Nome + p.Online + p.Ligado + p.Ocupado + p.Favorito).ToArray());
        if (marca == desenhado) return;
        desenhado = marca;
        cartoes.SuspendLayout();
        LimparCartoes();
        foreach (var pc in quais)
        {
            string l1, selo; Color corSelo;
            if (pc.Ocupado) { l1 = "Conectando..."; selo = "Conectando"; corSelo = Cores.Laranja; }
            else if (pc.Online) { l1 = Util.Tailscale(pc.Ip) ? "Fora de casa · modo trabalho" : "Pronto · clique para jogar"; selo = "Online"; corSelo = Cores.Verde; }
            else if (pc.Ligado) { l1 = "PC ligado, mas o TELAR não responde nele"; selo = "TELAR desligado"; corSelo = Cores.Laranja; }
            else { l1 = procurando ? "Procurando..." : "Desligado ou fora da rede"; selo = "Offline"; corSelo = Cinza; }
            var c = NovoCartao(pc.Nome, l1, pc.Ip + (pc.Favorito ? "  ★" : ""), pc.Online ? Cores.DoNome(pc.Nome) : Color.FromArgb(78, 78, 78),
                pc.Online ? Cartao.Desenho.Pc : Cartao.Desenho.Desligado);
            c.Selo = selo; c.CorSelo = corSelo;
            var info = pc;
            c.Click += delegate { AbrirPC(info, "Desktop", false); };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Jogar (tela inteira)", null, delegate { AbrirPC(info, "Desktop", false); });
            menu.Items.Add("Abrir Steam Big Picture", null, delegate { AbrirPC(info, "Steam Big Picture", false); });
            menu.Items.Add(info.Favorito ? "Tirar dos favoritos" : "Adicionar aos favoritos", null, delegate { info.Favorito = !info.Favorito; Salvar(); Redesenhar(); });
            menu.Items.Add("Conectar de novo", null, delegate { AbrirPC(info, "Desktop", true); });
            menu.Items.Add("Detalhes", null, delegate
            {
                string ultima = cfg.Get("tv.ultima");
                MessageBox.Show(this, "Nome: " + info.Nome + "\nEndereço: " + info.Ip + "\nStatus: " + (info.Online ? "Online" : info.Ligado ? "ligado, mas o TELAR não responde nele" : "Offline") +
                    (ultima != "" ? "\n\n" + ultima : ""), "TELAR");
            });
            menu.Items.Add("Esquecer este PC", null, delegate { lista.Remove(info); Salvar(); Redesenhar(); });
            c.ContextMenuStrip = menu;
        }
        if (cartoes.Controls.Count == 0)
            TextoVazio(abaAtual == 1 ? "Os PCs em que você jogar vão aparecer aqui." :
                       abaAtual == 2 ? "Clique com o botão direito num PC e escolha \"Adicionar aos favoritos\"." :
                       procurando ? "Procurando o PC Forte na rede..." :
                       "Nenhum PC Forte encontrado. Confira se o TELAR está instalado e ligado nele, ou digite o endereço lá em cima.");
        cartoes.ResumeLayout();
    }

    void Procurar() { Procurar(true); }
    void Procurar(bool completa)
    {
        if (procurando) return;
        procurando = true;
        if (completa) ultimaCompleta = DateTime.Now;
        var conhecidos = lista.Select(p => p.Ip).ToList();
        Redesenhar();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            List<string> achados;
            try { achados = Util.ProcurarPCsForte(conhecidos, completa); } catch { achados = new List<string>(); }
            // os conhecidos que não responderam: o PC está desligado ou só o TELAR está desligado nele?
            var ligados = new HashSet<string>();
            try { foreach (var ip in conhecidos.Where(ip => !achados.Any(a => a.StartsWith(ip + "|")))) if (Util.Ligado(ip)) ligados.Add(ip); } catch { }
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    procurando = false;
                    foreach (var pc in lista) if (!pc.Ocupado) { pc.Online = false; pc.Ligado = ligados.Contains(pc.Ip); }
                    foreach (var a in achados)
                    {
                        var x = a.Split('|');
                        var pc = lista.FirstOrDefault(p => p.Ip == x[0]);
                        if (pc == null) { pc = new PcInfo { Ip = x[0] }; lista.Add(pc); }
                        pc.Nome = x[1]; pc.Online = true; pc.Ligado = true;
                    }
                    Salvar();
                    Redesenhar();
                }));
            }
            catch { procurando = false; }
        });
    }

    void AdicionarManual()
    {
        string ip = Perguntar("Adicionar PC", "Digite o endereço do PC Forte (aparece no TELAR de lá).\nFora de casa use o endereço do Tailscale, que começa com 100.");
        if (!string.IsNullOrEmpty(ip)) ConectarEndereco(ip.Trim(), false);
    }

    // barra de endereço: digitou o IP e apertou Enter / seta
    void ConectarEndereco(string endereco) { ConectarEndereco(endereco, true); }
    void ConectarEndereco(string endereco, bool jogar)
    {
        if (endereco == "") { Aviso("Digite o endereço do PC Forte (ex.: 192.168.0.10) e aperte Enter.", Cores.Laranja); return; }
        var pc = lista.FirstOrDefault(p => p.Ip == endereco);
        if (pc == null) { pc = new PcInfo { Ip = endereco, Nome = endereco }; lista.Add(pc); }
        Salvar(); Redesenhar();
        if (jogar) AbrirPC(pc, "Desktop", false); else Procurar();
    }

    // clicar num PC: se já está conectado abre direto; se não, conecta sozinho (sem código) e abre.
    void AbrirPC(PcInfo pc, string app, bool conectarDeNovo)
    {
        if (pc.Ocupado) return;
        var aberto = ReceptorTransmitindo();
        if (aberto != null) { TrazerParaFrente(aberto); Aviso("A transmissão já está aberta.   Ctrl+Alt+Shift+Q fecha.", Cores.Laranja); return; }
        if (!Util.Confiavel(pc.Ip)) { Aviso("O TELAR conecta a PCs da sua rede de casa ou do seu Tailscale (endereço 100.x).", Cores.Vermelho); return; }
        pc.Ocupado = true; Redesenhar();
        Aviso("Conectando a " + pc.Nome + "...", Cores.Laranja);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            int c = conectarDeNovo ? -1 : Listar(pc.Ip);
            if (c != 0) c = ConectarSozinho(pc.Ip) ? 0 : c;
            bool ligado = c != 0 && Util.Ligado(pc.Ip);
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    pc.Ocupado = false;
                    if (c == 0) { pc.Pareado = true; pc.Online = true; pc.Ligado = true; Salvar(); Redesenhar(); Jogar(pc, app); return; }
                    Salvar(); Redesenhar();
                    Aviso("Não consegui conectar em " + pc.Nome + ". " +
                        (pc.Online ? "Confira se o TELAR está ligado no PC Forte (ícone perto do relógio) e tente de novo." :
                         ligado ? "O PC está ligado, mas o TELAR não respondeu nele." : "Ele parece desligado ou fora da rede."), Cores.Vermelho);
                }));
            }
            catch { }
        });
    }

    // conexão sem código (rede de casa ou Tailscale): os dois lados calculam o mesmo número a partir da
    // identidade do motor do PC Forte, e o TELAR de lá confirma sozinho em cerca de 3 segundos
    static bool ConectarSozinho(string ip)
    {
        string[] info = null;
        for (int i = 0; i < 3 && (info == null || info[1] == ""); i++) { info = Util.InfoMotor(ip, 4000); if (info == null) Thread.Sleep(1000); }
        if (info == null || info[1] == "") return false;
        string pin = Util.CodigoDaRede(info[1]);
        for (int tentativa = 0; tentativa < 2; tentativa++)
        {
            Process p = null;
            try
            {
                p = Receptor.Abrir("pair \"" + ip + "\" --pin " + pin, true);
                var fim = DateTime.Now.AddSeconds(12);
                Thread.Sleep(2500);
                while (DateTime.Now < fim)
                {
                    if (Listar(ip) == 0) return true;
                    if (p.HasExited) break;
                    Thread.Sleep(1500);
                }
            }
            catch { }
            finally { try { if (p != null && !p.HasExited) p.Kill(); } catch { } }
            Thread.Sleep(1000);
            if (Listar(ip) == 0) return true;
        }
        return false;
    }

    // 0 = este PC da TV já está conectado ao PC Forte desse endereço
    static int Listar(string ip)
    {
        try
        {
            var psi = new ProcessStartInfo(App.ClienteExe, "list \"" + ip + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(App.ClienteExe) };
            using (var p = Process.Start(psi))
            {
                p.ErrorDataReceived += delegate { }; p.BeginErrorReadLine();
                p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } return -999; }
                return p.ExitCode;
            }
        }
        catch { return -998; }
    }

    void Jogar(PcInfo pc, string app)
    {
        var plano = Plano(pc.Ip);
        pc.Ultimo = DateTime.Now.Ticks; Salvar();
        Aviso("Abrindo " + pc.Nome + " em " + plano.Largura + "x" + plano.Altura + " a " + plano.Fps + " FPS, " + (plano.Kbps / 1000) + " Mbps" + (plano.Aviso != "" ? "  ·  " + plano.Aviso : "") + ".   Ctrl+Alt+Shift+Q volta para cá.", Cores.Laranja);
        relogio.Stop();
        try
        {
            inicioJogo = DateTime.Now;
            jogo = Receptor.Abrir(Argumentos(pc.Ip, app, plano, cfg), false);
            jogo.EnableRaisingEvents = true;
            // o receptor mais rápido que o normal; o TELAR minimizado e parado até o jogo fechar
            try { jogo.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
            DateTime desde = inicioJogo;
            jogo.Exited += delegate
            {
                if (IsDisposed) return;
                // o que a TV recebeu de verdade (resolução, FPS, perdas), lido no registro do receptor
                var resumo = ResumoUltimaTransmissao(desde);
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        jogo = null;
                        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                        Activate();
                        if (resumo != null) { cfg.Set("tv.ultima", resumo[0]); cfg.Salvar(); Aviso(resumo[0], resumo[1] == "1" ? Cores.Verde : Cores.Laranja); }
                        else Aviso(AvisoPadrao, Cores.Laranja);
                        relogio.Start();
                        Procurar(false);
                    }));
                }
                catch { }
            };
            WindowState = FormWindowState.Minimized;
        }
        catch (Exception e)
        {
            jogo = null; relogio.Start();
            Aviso("Não consegui abrir o receptor: " + e.Message + ". Rode o TELAR-Setup.exe de novo.", Cores.Vermelho);
        }
    }

    // o receptor grava um registro na pasta dele cada vez que abre; o da transmissão diz a resolução e os FPS que
    // chegaram aqui de verdade e quantos quadros se perderam. Devolve { texto, "1" se ficou bom }.
    static string[] ResumoUltimaTransmissao(DateTime desde)
    {
        try
        {
            var arqs = new DirectoryInfo(Path.GetDirectoryName(App.ClienteExe)).GetFiles("Moonlight-*.log").OrderByDescending(f => f.LastWriteTime).ToList();
            foreach (var velho in arqs.Skip(30)) try { velho.Delete(); } catch { } // o receptor cria um por vez que abre
            foreach (var f in arqs.Take(12))
            {
                if (f.LastWriteTime < desde.AddSeconds(-5)) break;
                string txt;
                using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs)) txt = r.ReadToEnd();
                var v = Regex.Match(txt, @"Video stream is (\d+)x(\d+)x(\d+)");
                if (!v.Success) continue;
                Func<string, double> N = padrao =>
                {
                    var m = Regex.Match(txt, padrao); double d;
                    return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : -1;
                };
                double fps = N(@"Rendering frame rate: ([\d.]+) FPS"), rede = N(@"Frames dropped by your network connection: ([\d.]+)%"),
                       ritmo = N(@"Frames dropped due to network jitter: ([\d.]+)%"), lat = N(@"Average network latency: (\d+) ms"),
                       dec = N(@"Average decoding time: ([\d.]+) ms");
                int pedido = int.Parse(v.Groups[3].Value);
                string s = "Última transmissão: " + v.Groups[1].Value + "x" + v.Groups[2].Value + " a " + (fps >= 0 ? fps.ToString("0") : pedido.ToString()) + " FPS";
                double perdidos = Math.Max(0, rede) + Math.Max(0, ritmo);
                if (rede >= 0) s += "  ·  quadros perdidos " + perdidos.ToString("0.0") + "%";
                if (lat >= 0) s += "  ·  rede " + lat.ToString("0") + " ms";
                if (dec >= 0) s += "  ·  decodificação " + dec.ToString("0.0") + " ms";
                bool bom = (fps < 0 || fps >= pedido * 0.95) && perdidos < 1.5;
                if (!bom) s += perdidos >= 1.5 ? "  ·  o Wi-Fi perdeu quadros: use a rede 5 GHz perto do roteador ou diminua a taxa nas Configurações"
                                               : "  ·  este PC não acompanhou o FPS: tente 60 FPS nas Configurações";
                return new[] { s, bom ? "1" : "0" };
            }
        }
        catch { }
        return null;
    }

    // ---------------- plano de transmissão ----------------
    public class PlanoTx { public int Largura, Altura, Fps, Kbps; public bool Fora, Wifi; public string Aviso = ""; }

    // em tela cheia o receptor muda a TV para a taxa de atualização do FPS (ex.: 120 Hz), então vale a maior que ela aceita
    PlanoTx Plano(string ip) { return Resolver(ip, cfg, G("exibicao", "fullscreen") == "fullscreen" ? Tela.MaxHz() : Tela.Atual()[2], Util.WifiAgora()); }

    // PC da TV simples (poucos núcleos) decodifica 1440p 120 com dificuldade e trava tudo
    public static bool PcFraco { get { return Environment.ProcessorCount <= 4; } }

    public static PlanoTx Resolver(string ip, Config cfg, int hz) { return Resolver(ip, cfg, hz, false); }
    public static PlanoTx Resolver(string ip, Config cfg, int hz, bool wifi)
    {
        Func<string, string, string> G = (k, d) => { string v = cfg.Get("tv." + k); return v == "" ? d : v; };
        var p = new PlanoTx { Fora = Util.Tailscale(ip) && G("foraAuto", "1") == "1", Wifi = wifi };
        string res = G("res", "auto"), fps = G("fps", "auto");
        // em casa o automático manda sempre Quad HD 1440p (a resolução nativa do PC Forte: sem reduzir a imagem lá
        // e ampliar de novo aqui, que é o que deixava borrado); num PC da TV bem simples vai a 60 FPS
        bool so60 = !p.Fora && PcFraco;
        // fora de casa é acesso remoto para trabalho: usa a resolução da tela daqui (texto nítido, sem escala)
        int alturaTela = Tela.Atual()[1];
        int foraAltura = alturaTela >= 1440 ? 1440 : alturaTela >= 1080 ? 1080 : 720;
        p.Altura = res == "auto" ? (p.Fora ? foraAltura : 1440) : int.Parse(res);
        p.Largura = p.Altura == 2160 ? 3840 : p.Altura == 1440 ? 2560 : p.Altura == 1080 ? 1920 : 1280;
        p.Fps = fps == "auto" ? (so60 ? 60 : hz >= 119 ? 120 : 60) : int.Parse(fps);
        if (p.Fps > 60 && hz < p.Fps - 1)
        {
            p.Aviso = "a TV está em " + hz + " Hz, usando " + (hz >= 119 ? 120 : 60) + " FPS";
            p.Fps = hz >= 119 ? 120 : 60;
        }
        if (p.Fora && p.Fps > 60) { p.Fps = 60; p.Aviso = "modo trabalho: 60 FPS"; }
        if (so60 && fps == "auto") p.Aviso = "PC da TV simples: 60 FPS para não travar";
        int auto = BitrateAuto(p.Altura, p.Fps, p.Fora);
        // Wi-Fi não aguenta a taxa do cabo sem perder pacotes (é isso que trava a imagem): até 50 Mbps
        if (wifi && !p.Fora) auto = Math.Min(auto, 50000);
        string br = G("bitrate", "auto");
        p.Kbps = br == "auto" ? auto : int.Parse(br);
        return p;
    }

    public static int BitrateAuto(int altura, int fps, bool fora)
    {
        if (fora) return altura >= 2160 ? 20000 : altura == 1440 ? 15000 : altura == 1080 ? 12000 : 8000;
        int base60 = altura >= 2160 ? 80000 : altura == 1440 ? 45000 : altura == 1080 ? 30000 : 15000;
        if (fps <= 30) return base60 / 2;
        if (fps <= 60) return base60;
        int v = (int)(base60 * (fps / 60.0) * 0.83);
        return Math.Min(v, 150000) / 1000 * 1000;
    }

    public static string Argumentos(string ip, string app, PlanoTx p, Config cfg)
    {
        Func<string, bool, bool> B = (k, d) => { string v = cfg.Get("tv." + k); return v == "" ? d : v == "1"; };
        Func<string, string, string> G = (k, d) => { string v = cfg.Get("tv." + k); return v == "" ? d : v; };
        var a = new StringBuilder();
        a.AppendFormat("stream \"{0}\" \"{1}\" --resolution {2}x{3} --fps {4} --bitrate {5}", ip, app, p.Largura, p.Altura, p.Fps, p.Kbps);
        a.Append(" --display-mode " + G("exibicao", "fullscreen"));
        // V-Sync ligado: imagem sem rasgos na TV. Ritmo do quadro no Wi-Fi: segura um quadro para a imagem
        // andar lisinha mesmo quando o Wi-Fi atrasa um pacote
        bool vsync = B("vsync", true), ritmo = B("ritmo", p.Wifi);
        bool mouseRemoto = B("mouseRemoto", false), yuv444 = B("yuv444", false);
        // fora de casa = trabalho remoto: imagem suave na internet, mouse de área de trabalho
        // e cores 4:4:4 (texto mais nítido) quando a placa do PC Forte suportar
        if (p.Fora) { vsync = true; ritmo = true; mouseRemoto = true; yuv444 = true; }
        a.Append(vsync ? " --vsync" : " --no-vsync");
        a.Append(vsync && ritmo ? " --frame-pacing" : " --no-frame-pacing");
        a.Append(B("hdr", false) ? " --hdr" : " --no-hdr");
        a.Append(" --audio-config " + G("audio", "stereo"));
        a.Append(B("silenciarHost", true) ? " --no-audio-on-host" : " --audio-on-host");
        a.Append(B("mudoSemFoco", false) ? " --mute-on-focus-loss" : " --no-mute-on-focus-loss");
        a.Append(B("otimizarJogo", true) ? " --game-optimization" : " --no-game-optimization");
        a.Append(B("sairDepois", false) ? " --quit-after" : " --no-quit-after");
        a.Append(mouseRemoto ? " --absolute-mouse" : " --no-absolute-mouse");
        a.Append(" --capture-system-keys " + (B("capturarTeclas", true) ? G("capturarQuando", "fullscreen") : "never"));
        a.Append(B("trackpad", false) ? " --touchscreen-trackpad" : " --no-touchscreen-trackpad");
        a.Append(B("trocarMouse", false) ? " --mouse-buttons-swap" : " --no-mouse-buttons-swap");
        a.Append(B("inverterRolagem", false) ? " --reverse-scroll-direction" : " --no-reverse-scroll-direction");
        a.Append(B("trocarAB", false) ? " --swap-gamepad-buttons" : " --no-swap-gamepad-buttons");
        a.Append(B("multiControle", true) ? " --multi-controller" : " --no-multi-controller");
        a.Append(B("controleFundo", false) ? " --background-gamepad" : " --no-background-gamepad");
        a.Append(" --video-decoder " + G("decodificador", "auto"));
        a.Append(" --video-codec " + G("codec", "auto"));
        a.Append(yuv444 ? " --yuv444" : " --no-yuv444");
        a.Append(B("manterTela", true) ? " --keep-awake" : " --no-keep-awake");
        a.Append(B("stats", false) ? " --performance-overlay" : " --no-performance-overlay");
        return a.ToString();
    }

    // ---------------- Configurações ----------------
    void MostrarConfig()
    {
        config = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Fundo };
        var grade = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), BackColor = Fundo };
        grade.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); grade.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var esq = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
        var dir = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
        grade.Controls.Add(esq, 0, 0); grade.Controls.Add(dir, 1, 0);
        config.Controls.Add(grade);
        int largura = Math.Max(380, (ClientSize.Width - 60) / 2);
        config.Resize += delegate { int w = Math.Max(380, (config.ClientSize.Width - 60) / 2); foreach (Control s in esq.Controls.Cast<Control>().Concat(dir.Controls.Cast<Control>())) s.Width = w; };

        int hz = Tela.MaxHz();
        bool wifi = Util.WifiAgora();

        // Configurações Básicas
        var s1 = Secao(esq, "Configurações Básicas", largura);
        Rotulo(s1, "Resolução e FPS", 11f, Color.White);
        Rotulo(s1, "Valores muito altos para o seu PC ou rede podem causar atraso, engasgos ou erros. Esta TV aceita até " + hz + " Hz" + (wifi ? " · conectado pelo Wi-Fi" : " · conectado pelo cabo") + ".", 8.5f, Color.FromArgb(190, 190, 190));
        var linha = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        var cRes = Combo(linha, "res", new[] { "auto|Automático (Quad HD 1440p)", "720|720p", "1080|1080p", "1440|1440p", "2160|4K" }, "auto", 210);
        var cFps = Combo(linha, "fps", new[] { "auto|Automático (120, ou 60)", "30|30 FPS", "60|60 FPS", "90|90 FPS", "120|120 FPS", "144|144 FPS" }, "auto", 210);
        s1.Controls.Add(linha);
        var lBit = Rotulo(s1, "", 10f, Color.White);
        Rotulo(s1, "Diminua a taxa de bits em conexões mais lentas. Aumente para melhorar a imagem.", 8.5f, Color.FromArgb(190, 190, 190));
        var trilha = new TrackBar { Minimum = 1, Maximum = B("desbloquear", false) ? 500 : 150, TickStyle = TickStyle.None, Width = 440, BackColor = Fundo };
        var bAuto = new Button { Text = "Automático", FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(70, 70, 70), AutoSize = true };
        bAuto.FlatAppearance.BorderSize = 0;
        var linhaBit = new FlowLayoutPanel { AutoSize = true }; linhaBit.Controls.Add(trilha); linhaBit.Controls.Add(bAuto);
        s1.Controls.Add(linhaBit);
        Action atualizarBit = () =>
        {
            var p = Resolver("192.168.0.1", cfg, hz, wifi);
            int v = p.Kbps / 1000;
            trilha.Value = Math.Max(trilha.Minimum, Math.Min(trilha.Maximum, v));
            lBit.Text = "Taxa de bits de vídeo: " + v + " Mbps" + (G("bitrate", "auto") == "auto" ? " (automático para " + p.Altura + "p " + p.Fps + " FPS" + (wifi ? " no Wi-Fi" : "") + ")" : "") + (p.Aviso != "" ? "  ·  " + p.Aviso : "");
        };
        bool mexendo = false;
        trilha.Scroll += delegate { mexendo = true; cfg.Set("tv.bitrate", (trilha.Value * 1000).ToString()); atualizarBit(); mexendo = false; };
        bAuto.Click += delegate { cfg.Set("tv.bitrate", "auto"); cfg.Salvar(); atualizarBit(); };
        cRes.SelectedIndexChanged += delegate { if (!mexendo) atualizarBit(); };
        cFps.SelectedIndexChanged += delegate { if (!mexendo) atualizarBit(); };
        atualizarBit();
        Rotulo(s1, "Modo de exibição", 10f, Color.White);
        var lm = new FlowLayoutPanel { AutoSize = true }; s1.Controls.Add(lm);
        Combo(lm, "exibicao", new[] { "fullscreen|Tela cheia (Recomendado)", "borderless|Janela sem bordas", "windowed|Janela" }, "fullscreen", 230);
        var l2 = new FlowLayoutPanel { AutoSize = true }; s1.Controls.Add(l2);
        Check(l2, "vsync", "Sincronização vertical", true);
        Check(l2, "ritmo", "Ritmo do quadro", wifi);
        Check(s1, "hdr", "Habilitar HDR", false);
        Rotulo(s1, "Sincronização vertical: imagem sem rasgos na TV. Ritmo do quadro: imagem mais lisa no Wi-Fi (o TELAR liga sozinho quando a TV está no Wi-Fi).", 8.5f, Color.FromArgb(190, 190, 190));

        // Áudio
        var s2 = Secao(esq, "Configurações de Áudio", largura);
        Rotulo(s2, "Configurações de áudio", 10f, Color.White);
        var la = new FlowLayoutPanel { AutoSize = true }; s2.Controls.Add(la);
        Combo(la, "audio", new[] { "stereo|Estéreo", "5.1-surround|Surround 5.1", "7.1-surround|Surround 7.1" }, "stereo", 200);
        Check(s2, "silenciarHost", "Silenciar os alto-falantes do PC Forte durante o streaming", true);
        Check(s2, "mudoSemFoco", "Silenciar o áudio quando o TELAR não é a janela ativa", false);

        // Host
        var s3 = Secao(esq, "Configurações do PC Forte", largura);
        Check(s3, "otimizarJogo", "Otimize as configurações do jogo para streaming", true);
        Check(s3, "sairDepois", "Feche o app no PC Forte depois de encerrar a transmissão", false);

        // Interface
        var s4 = Secao(esq, "Configurações da interface", largura);
        Check(s4, "manterTela", "Mantenha a tela ativa durante a transmissão", true);
        Check(s4, "stats", "Mostrar estatísticas de desempenho durante o streaming", false);

        // Entrada
        var s5 = Secao(dir, "Configurações de Entrada", largura);
        Check(s5, "mouseRemoto", "Otimize o mouse para área de trabalho remota em vez de jogos", false);
        var lc = new FlowLayoutPanel { AutoSize = true }; s5.Controls.Add(lc);
        Check(lc, "capturarTeclas", "Capturar atalhos de teclado do sistema", true);
        Combo(lc, "capturarQuando", new[] { "fullscreen|Em tela cheia", "always|Sempre" }, "fullscreen", 150);
        Check(s5, "trackpad", "Use a tela sensível ao toque como um trackpad virtual", false);
        Check(s5, "trocarMouse", "Troque os botões esquerdo e direito do mouse", false);
        Check(s5, "inverterRolagem", "Inverter a direção de rolagem do mouse", false);

        // Controle
        var s6 = Secao(dir, "Configurações do Controle", largura);
        Check(s6, "trocarAB", "Troque os botões do controle A/B e X/Y", false);
        Check(s6, "multiControle", "Vários controles (cada um vira um jogador)", true);
        Check(s6, "controleFundo", "Processe a entrada do controle quando o TELAR estiver em segundo plano", false);

        // Avançadas
        var s7 = Secao(dir, "Configurações Avançadas", largura);
        Rotulo(s7, "Decodificador de vídeo", 10f, Color.White);
        var ld = new FlowLayoutPanel { AutoSize = true }; s7.Controls.Add(ld);
        Combo(ld, "decodificador", new[] { "auto|Automático (Recomendado)", "hardware|Forçar decodificação por hardware", "software|Forçar decodificação por software" }, "auto", 290);
        Rotulo(s7, "Codec de vídeo", 10f, Color.White);
        var lco = new FlowLayoutPanel { AutoSize = true }; s7.Controls.Add(lco);
        Combo(lco, "codec", new[] { "auto|Automático (Recomendado)", "H.264|H.264", "HEVC|HEVC (H.265)", "AV1|AV1" }, "auto", 290);
        Check(s7, "yuv444", "Habilitar YUV 4:4:4", false);
        var cDes = Check(s7, "desbloquear", "Desbloqueie o limite de taxa de bits (Experimental)", false);
        cDes.CheckedChanged += delegate { trilha.Maximum = cDes.Checked ? 500 : 150; atualizarBit(); };
        Check(s7, "procurar", "Encontre PCs automaticamente na rede (Recomendado)", true);
        Check(s7, "foraAuto", "Fora de casa (Tailscale) usar modo trabalho: resolução desta tela, 60 FPS, mouse de área de trabalho e texto nítido", true);

        AbrirPagina(config, "Configurações");
    }

    FlowLayoutPanel Secao(FlowLayoutPanel coluna, string nome, int largura)
    {
        coluna.Controls.Add(new Label { Text = nome, ForeColor = Cores.Laranja, AutoSize = true, Margin = new Padding(4, 12, 0, 4), Font = new Font("Segoe UI", 10f) });
        var s = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Width = largura, MinimumSize = new Size(largura, 0), Padding = new Padding(10), Margin = new Padding(4, 0, 4, 4), BackColor = Color.FromArgb(56, 56, 56) };
        coluna.Controls.Add(s);
        return s;
    }

    Label Rotulo(Control pai, string t, float tam, Color cor)
    {
        var l = new Label { Text = t, ForeColor = cor, Font = new Font("Segoe UI", tam), AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 4, 0, 2) };
        pai.Controls.Add(l); return l;
    }

    CheckBox Check(Control pai, string chave, string texto, bool padrao)
    {
        var c = new CheckBox { Text = texto, AutoSize = true, Checked = B(chave, padrao), ForeColor = Color.White, Margin = new Padding(0, 6, 12, 6) };
        c.CheckedChanged += delegate { cfg.Set("tv." + chave, c.Checked ? "1" : "0"); cfg.Salvar(); };
        pai.Controls.Add(c); return c;
    }

    ComboBox Combo(Control pai, string chave, string[] itens, string padrao, int largura)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(70, 70, 70), ForeColor = Color.White, Width = largura, Margin = new Padding(0, 2, 8, 2) };
        var valores = itens.Select(i => i.Split('|')[0]).ToList();
        foreach (var i in itens) c.Items.Add(i.Split('|')[1]);
        int idx = valores.IndexOf(G(chave, padrao)); c.SelectedIndex = idx < 0 ? 0 : idx;
        c.SelectedIndexChanged += delegate { cfg.Set("tv." + chave, valores[c.SelectedIndex]); cfg.Salvar(); };
        pai.Controls.Add(c); return c;
    }

    // ---------------- pequenos diálogos ----------------
    string Perguntar(string titulo, string texto)
    {
        var f = new Form { Text = titulo, BackColor = Fundo, ForeColor = Color.White, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(440, 200), Font = Font };
        f.Controls.Add(new Label { Text = texto, Bounds = new Rectangle(20, 16, 400, 60), ForeColor = Color.FromArgb(210, 210, 210) });
        var t = new TextBox { Bounds = new Rectangle(20, 84, 400, 30), BackColor = Color.FromArgb(70, 70, 70), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 13f) };
        var ok = new Button { Text = "Adicionar", DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Barra, ForeColor = Color.White, Bounds = new Rectangle(20, 140, 400, 40) };
        ok.FlatAppearance.BorderSize = 0;
        f.Controls.Add(t); f.Controls.Add(ok); f.AcceptButton = ok;
        return f.ShowDialog(this) == DialogResult.OK ? t.Text : null;
    }

    void Ajuda()
    {
        MessageBox.Show(this,
            "Como usar o TELAR na TV:\n\n" +
            "1. Instale o TELAR no PC Forte (opção PC Forte). Ele fica ligado perto do relógio e abre sozinho com o Windows.\n" +
            "2. Aqui o PC Forte aparece sozinho em Encontrados, com o status Online ou Offline. Se não aparecer, digite o IP dele lá em cima.\n" +
            "3. Clique no PC Forte: conecta sozinho, sem código, e abre a tela inteira dele.\n\n" +
            "Qualidade automática: Quad HD 1440p a 120 FPS (no Wi-Fi use a rede 5 GHz). Ao fechar, o TELAR mostra a resolução e o FPS que chegaram aqui.\n\n" +
            "Durante o jogo: Ctrl+Alt+Shift+Q sai, Ctrl+Alt+Shift+S mostra as estatísticas, Ctrl+Alt+Shift+Z solta o mouse.\n" +
            "Fora de casa (trabalho): instale o Tailscale nos dois PCs e use o IP 100.x do PC Forte. Também conecta sozinho.",
            "Ajuda do TELAR");
    }
}
