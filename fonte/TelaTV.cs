// TELAR - PC da TV. Mesma organização do receptor: tela "Computadores" (PCs achados, com cadeado até parear),
// tela de apps do PC escolhido e tela "Configurações" com todas as opções de transmissão.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

class PcInfo
{
    public string Ip, Nome;
    public bool Online, Pareado, Ocupado;
}

// ---------------- quadrado de PC / app ----------------
class Bloco : Control
{
    public enum Icone { Pc, App, Steam }
    public Icone Tipo = Icone.Pc;
    public bool Cadeado, Desligado;
    public string Aviso = "";
    bool sobre;

    public Bloco()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(230, 250); Margin = new Padding(8); Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 20f); ForeColor = Color.White;
    }
    protected override void OnMouseEnter(EventArgs e) { sobre = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { sobre = false; Invalidate(); base.OnMouseLeave(e); }

    static GraphicsPath Arred(RectangleF r, float raio)
    {
        var p = new GraphicsPath(); float d = raio * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(sobre ? Color.FromArgb(84, 84, 84) : Color.FromArgb(64, 64, 64));
        var cor = Desligado ? Color.FromArgb(140, 140, 140) : Color.White;
        using (var br = new SolidBrush(cor))
        using (var fundo = new SolidBrush(sobre ? Color.FromArgb(84, 84, 84) : Color.FromArgb(64, 64, 64)))
        {
            float cx = Width / 2f;
            if (Tipo == Icone.Pc || Tipo == Icone.App)
            {
                // monitor
                var tela = new RectangleF(cx - 72, 14, 144, 112);
                using (var p = Arred(tela, 14)) g.FillPath(br, p);
                using (var p = Arred(new RectangleF(tela.X + 12, tela.Y + 12, tela.Width - 24, tela.Height - 32), 4)) g.FillPath(fundo, p);
                g.FillRectangle(br, cx - 12, tela.Bottom, 24, 14);
                g.FillRectangle(br, cx - 26, tela.Bottom + 12, 52, 10);
                float my = tela.Y + 12 + (tela.Height - 32) / 2f;
                if (Tipo == Icone.Pc && Cadeado)
                {
                    // cadeado
                    using (var pen = new Pen(cor, 6)) g.DrawArc(pen, cx - 11, my - 24, 22, 26, 180, 180);
                    using (var p = Arred(new RectangleF(cx - 18, my - 11, 36, 30), 5)) g.FillPath(br, p);
                    g.FillEllipse(fundo, cx - 4, my - 1, 8, 8);
                }
                else if (Tipo == Icone.App)
                {
                    g.FillPolygon(br, new[] { new PointF(cx - 12, my - 18), new PointF(cx - 12, my + 18), new PointF(cx + 18, my) });
                }
                else if (Desligado)
                {
                    using (var pen = new Pen(cor, 6)) { g.DrawLine(pen, cx - 14, my - 14, cx + 14, my + 14); g.DrawLine(pen, cx + 14, my - 14, cx - 14, my + 14); }
                }
            }
            else
            {
                // controle (Steam Big Picture)
                using (var p = Arred(new RectangleF(cx - 70, 40, 140, 80), 36)) g.FillPath(br, p);
                g.FillRectangle(fundo, cx - 46, 72, 26, 8); g.FillRectangle(fundo, cx - 37, 63, 8, 26);
                g.FillEllipse(fundo, cx + 24, 60, 12, 12); g.FillEllipse(fundo, cx + 36, 74, 12, 12);
            }
            var fmt = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString(Text, Font, br, new RectangleF(10, 160, Width - 20, 44), fmt);
            if (Aviso != "")
                using (var f2 = new Font("Segoe UI", 10f)) g.DrawString(Aviso, f2, br, new RectangleF(10, 206, Width - 20, 36), fmt);
        }
    }
}

// ---------------- janela principal do PC da TV ----------------
class TelaTV : Form
{
    readonly Config cfg;
    readonly Panel topo, corpo;
    readonly Label titulo;
    readonly Button bVoltar, bAdicionar, bAjuda, bConfig;
    readonly FlowLayoutPanel pcs, apps;
    Panel config;
    readonly List<PcInfo> lista = new List<PcInfo>();
    PcInfo atual;
    readonly System.Windows.Forms.Timer relogio = new System.Windows.Forms.Timer { Interval = 12000 };
    bool procurando;

    static readonly Color Fundo = Color.FromArgb(48, 48, 48);
    static readonly Color Barra = Color.FromArgb(14, 120, 104);

    public TelaTV(Config c)
    {
        cfg = c;
        Text = "TELAR"; BackColor = Fundo; ForeColor = Color.White; Font = new Font("Segoe UI", 10f);
        StartPosition = FormStartPosition.CenterScreen; Size = new Size(1280, 820); MinimumSize = new Size(820, 560);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        topo = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Barra };
        titulo = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 15f), ForeColor = Color.White };
        bVoltar = Icone("", "Voltar"); bVoltar.Dock = DockStyle.Left;
        bConfig = Icone("", "Configurações"); bConfig.Dock = DockStyle.Right;
        bAjuda = Icone("", "Ajuda"); bAjuda.Dock = DockStyle.Right;
        bAdicionar = Icone("", "Adicionar PC pelo IP"); bAdicionar.Dock = DockStyle.Right;
        topo.Controls.AddRange(new Control[] { titulo, bVoltar, bAdicionar, bAjuda, bConfig });

        corpo = new Panel { Dock = DockStyle.Fill, BackColor = Fundo };
        pcs = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8), BackColor = Fundo };
        apps = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8), BackColor = Fundo };
        corpo.Controls.Add(pcs);
        Controls.Add(corpo); Controls.Add(topo);

        bVoltar.Click += delegate { MostrarPCs(); };
        bConfig.Click += delegate { MostrarConfig(); };
        bAdicionar.Click += delegate { AdicionarManual(); };
        bAjuda.Click += delegate { Ajuda(); };
        relogio.Tick += delegate { if (cfg.Get("tv.procurar") != "0") Procurar(); };
        FormClosing += delegate { Salvar(); };

        foreach (var s in cfg.Get("tv.pcs").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var x = s.Split('|');
            if (x.Length >= 2) lista.Add(new PcInfo { Ip = x[0], Nome = x[1], Pareado = x.Length > 2 && x[2] == "1" });
        }
        Shown += delegate
        {
            MostrarPCs();
            if (!File.Exists(App.ClienteExe)) MessageBox.Show(this, "Falta o receptor do TELAR. Rode o TELAR-Setup.exe de novo.", "TELAR");
            Procurar(); relogio.Start();
        };
    }

    Button Icone(string glifo, string dica)
    {
        var b = new Button { Text = glifo, Font = new Font("Segoe MDL2 Assets", 15f), Width = 56, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Barra, Cursor = Cursors.Hand, TabStop = false };
        b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = Color.FromArgb(20, 145, 126);
        new ToolTip().SetToolTip(b, dica);
        return b;
    }

    string G(string chave, string padrao) { string v = cfg.Get("tv." + chave); return v == "" ? padrao : v; }
    bool B(string chave, bool padrao) { string v = cfg.Get("tv." + chave); return v == "" ? padrao : v == "1"; }

    void Salvar()
    {
        cfg.Set("tv.pcs", string.Join(";", lista.Select(p => p.Ip + "|" + p.Nome.Replace("|", " ").Replace(";", " ") + "|" + (p.Pareado ? "1" : "0")).ToArray()));
        cfg.Salvar();
    }

    void Pagina(Control c, string t, bool voltar)
    {
        corpo.Controls.Clear(); corpo.Controls.Add(c);
        titulo.Text = t; bVoltar.Visible = voltar; bAdicionar.Visible = !voltar;
    }

    // ---------------- Computadores ----------------
    void MostrarPCs()
    {
        atual = null;
        Pagina(pcs, "Computadores", false);
        Redesenhar();
    }

    void Redesenhar()
    {
        pcs.SuspendLayout();
        pcs.Controls.Clear();
        foreach (var pc in lista.OrderByDescending(p => p.Online).ThenBy(p => p.Nome))
        {
            var b = new Bloco { Text = pc.Nome, Cadeado = !pc.Pareado, Desligado = !pc.Online, Tag = pc };
            b.Aviso = pc.Ocupado ? "Conectando..." : !pc.Online ? (procurando ? "Procurando..." : "Desligado") : pc.Ip.StartsWith("100.") ? "fora de casa · " + pc.Ip : pc.Ip;
            var info = pc;
            b.Click += delegate { AbrirPC(info); };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Ver apps", null, delegate { AbrirPC(info); });
            menu.Items.Add("Parear de novo", null, delegate { Parear(info); });
            menu.Items.Add("Detalhes", null, delegate { MessageBox.Show(this, "Nome: " + info.Nome + "\nIP: " + info.Ip + "\nPareado: " + (info.Pareado ? "sim" : "não") + "\nNa rede agora: " + (info.Online ? "sim" : "não"), "TELAR"); });
            menu.Items.Add("Esquecer este PC", null, delegate { lista.Remove(info); Salvar(); Redesenhar(); });
            b.ContextMenuStrip = menu;
            pcs.Controls.Add(b);
        }
        if (lista.Count == 0)
            pcs.Controls.Add(new Label { AutoSize = false, Size = new Size(700, 80), ForeColor = Color.FromArgb(190, 190, 190), Font = new Font("Segoe UI", 12f), Margin = new Padding(16),
                Text = procurando ? "Procurando o PC Forte na rede..." : "Nenhum PC Forte encontrado. Confira se o TELAR está instalado e ligado nele, ou clique em + para digitar o IP." });
        pcs.ResumeLayout();
    }

    void Procurar()
    {
        if (procurando) return;
        procurando = true;
        if (atual == null && corpo.Controls.Contains(pcs)) Redesenhar();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var achados = Util.ProcurarPCsForte();
            BeginInvoke((Action)(() =>
            {
                procurando = false;
                foreach (var pc in lista) pc.Online = false;
                foreach (var a in achados)
                {
                    var x = a.Split('|');
                    var pc = lista.FirstOrDefault(p => p.Ip == x[0]);
                    if (pc == null) { pc = new PcInfo { Ip = x[0] }; lista.Add(pc); }
                    pc.Nome = x[1]; pc.Online = true;
                }
                Salvar();
                if (atual == null && corpo.Controls.Contains(pcs)) Redesenhar();
                ConferirPareamento();
            }));
        });
    }

    // confere uma vez por PC (em segundo plano) se ele já está pareado, para tirar o cadeado
    readonly HashSet<string> conferidos = new HashSet<string>();
    void ConferirPareamento()
    {
        foreach (var pc in lista.Where(p => p.Online && !conferidos.Contains(p.Ip)).ToList())
        {
            conferidos.Add(pc.Ip);
            var alvo = pc;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string lixo; bool ok = Listar(alvo.Ip, out lixo) == 0;
                BeginInvoke((Action)(() =>
                {
                    alvo.Pareado = ok; Salvar();
                    if (atual == null && corpo.Controls.Contains(pcs)) Redesenhar();
                }));
            });
        }
    }

    void AdicionarManual()
    {
        string ip = Perguntar("Adicionar PC", "Digite o IP do PC Forte (aparece no TELAR de lá).\nFora de casa use o IP do Tailscale, que começa com 100.");
        if (string.IsNullOrEmpty(ip)) return;
        ip = ip.Trim();
        if (lista.All(p => p.Ip != ip)) lista.Add(new PcInfo { Ip = ip, Nome = ip });
        Salvar(); Redesenhar(); Procurar();
    }

    // clicar num PC: confere se está pareado e mostra os apps; se não estiver, pareia
    void AbrirPC(PcInfo pc)
    {
        if (pc.Ocupado) return;
        pc.Ocupado = true; Redesenhar();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            string saida; int c = Listar(pc.Ip, out saida);
            BeginInvoke((Action)(() =>
            {
                pc.Ocupado = false;
                if (c == 0)
                {
                    pc.Pareado = true; pc.Online = true; Salvar();
                    var nomes = saida.Split('\n').Select(l => l.Trim()).Where(l => l != "" && !l.Contains(" - ") && !l.StartsWith("Usage")).ToList();
                    if (nomes.Count == 0) nomes = new List<string> { "Desktop", "Steam Big Picture" };
                    MostrarApps(pc, nomes);
                }
                else
                {
                    pc.Pareado = false; Salvar(); Redesenhar();
                    Parear(pc);
                }
            }));
        });
    }

    static int Listar(string ip, out string saida)
    {
        saida = "";
        try
        {
            var psi = new ProcessStartInfo(App.ClienteExe, "list \"" + ip + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(App.ClienteExe) };
            using (var p = Process.Start(psi))
            {
                p.ErrorDataReceived += delegate { }; p.BeginErrorReadLine();
                saida = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } return -999; }
                return p.ExitCode;
            }
        }
        catch { return -998; }
    }

    // ---------------- apps do PC ----------------
    void MostrarApps(PcInfo pc, List<string> nomes)
    {
        atual = pc;
        apps.Controls.Clear();
        foreach (var n in nomes)
        {
            var b = new Bloco { Text = n, Tipo = n.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0 ? Bloco.Icone.Steam : Bloco.Icone.App };
            var plano = Plano(pc.Ip);
            b.Aviso = plano.Largura + "x" + plano.Altura + " · " + plano.Fps + " FPS";
            string app = n;
            b.Click += delegate { Jogar(pc, app); };
            apps.Controls.Add(b);
        }
        Pagina(apps, pc.Nome, true);
    }

    void Jogar(PcInfo pc, string app)
    {
        var plano = Plano(pc.Ip);
        if (plano.Aviso != "") titulo.Text = pc.Nome + "  ·  " + plano.Aviso;
        Receptor.Abrir(Argumentos(pc.Ip, app, plano, cfg), false);
    }

    // ---------------- pareamento ----------------
    void Parear(PcInfo pc)
    {
        string pin = new Random().Next(1000, 10000).ToString();
        var p = Receptor.Abrir("pair \"" + pc.Ip + "\" --pin " + pin, true);
        var f = new Form { Text = "Parear com " + pc.Nome, BackColor = Fundo, ForeColor = Color.White, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(460, 300), Font = Font };
        f.Controls.Add(new Label { Text = "No TELAR do PC Forte, digite este PIN e clique em Liberar:", ForeColor = Color.FromArgb(200, 200, 200), Bounds = new Rectangle(24, 20, 420, 24) });
        f.Controls.Add(new Label { Text = pin, Font = new Font("Segoe UI Semibold", 48f), ForeColor = Color.White, Bounds = new Rectangle(24, 52, 420, 100) });
        var st = new Label { Text = "Esperando o PC Forte liberar...", ForeColor = Color.FromArgb(200, 200, 200), Bounds = new Rectangle(24, 168, 420, 50) };
        f.Controls.Add(st);
        var ok = new Button { Text = "Fechar", FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(70, 70, 70), ForeColor = Color.White, Bounds = new Rectangle(24, 236, 412, 42) };
        ok.FlatAppearance.BorderSize = 0; ok.Click += delegate { f.Close(); }; f.Controls.Add(ok);
        // o receptor continua aberto depois de parear; por isso confere a cada 2 s se o PC Forte já liberou
        var tm = new System.Windows.Forms.Timer { Interval = 2000 };
        bool conferindo = false, pareado = false;
        tm.Tick += delegate
        {
            if (conferindo) return;
            conferindo = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string lixo; bool deu = Listar(pc.Ip, out lixo) == 0;
                f.BeginInvoke((Action)(() =>
                {
                    conferindo = false;
                    if (!deu && !p.HasExited) return;
                    tm.Stop();
                    if (!p.HasExited) try { p.Kill(); } catch { }
                    pareado = deu;
                    st.ForeColor = deu ? Color.FromArgb(90, 210, 130) : Color.FromArgb(240, 120, 110);
                    st.Text = deu ? "Pareado. Pode jogar." : "Não pareou. Confira se o TELAR está aberto no PC Forte e tente de novo.";
                    ok.Text = deu ? "Ver apps" : "Fechar";
                }));
            });
        };
        tm.Start();
        f.FormClosed += delegate { tm.Stop(); if (!p.HasExited) try { p.Kill(); } catch { } };
        f.ShowDialog(this);
        pc.Pareado = pareado; Salvar(); Redesenhar();
        if (pareado) AbrirPC(pc);
    }

    // ---------------- plano de transmissão ----------------
    public class PlanoTx { public int Largura, Altura, Fps, Kbps; public bool Fora; public string Aviso = ""; }

    PlanoTx Plano(string ip) { return Resolver(ip, cfg, Tela.Atual()[2]); }

    public static PlanoTx Resolver(string ip, Config cfg, int hz)
    {
        Func<string, string, string> G = (k, d) => { string v = cfg.Get("tv." + k); return v == "" ? d : v; };
        var p = new PlanoTx { Fora = ip.StartsWith("100.") && G("foraAuto", "1") == "1" };
        string res = G("res", "auto"), fps = G("fps", "auto");
        p.Altura = res == "auto" ? (p.Fora ? 1080 : 1440) : int.Parse(res);
        p.Largura = p.Altura == 2160 ? 3840 : p.Altura == 1440 ? 2560 : p.Altura == 1080 ? 1920 : 1280;
        p.Fps = fps == "auto" ? (hz >= 119 ? 120 : 60) : int.Parse(fps);
        if (p.Fps > 60 && hz < p.Fps - 1)
        {
            p.Aviso = "a TV está em " + hz + " Hz, usando " + (hz >= 119 ? 120 : 60) + " FPS";
            p.Fps = hz >= 119 ? 120 : 60;
        }
        if (p.Fora && p.Fps > 60) { p.Fps = 60; p.Aviso = "fora de casa: 60 FPS"; }
        int auto = BitrateAuto(p.Altura, p.Fps, p.Fora);
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
        bool vsync = B("vsync", false), ritmo = B("ritmo", false);
        if (p.Fora) { vsync = true; ritmo = true; } // internet oscila: suaviza
        a.Append(vsync ? " --vsync" : " --no-vsync");
        a.Append(ritmo ? " --frame-pacing" : " --no-frame-pacing");
        a.Append(B("hdr", false) ? " --hdr" : " --no-hdr");
        a.Append(" --audio-config " + G("audio", "stereo"));
        a.Append(B("silenciarHost", true) ? " --no-audio-on-host" : " --audio-on-host");
        a.Append(B("mudoSemFoco", false) ? " --mute-on-focus-loss" : " --no-mute-on-focus-loss");
        a.Append(B("otimizarJogo", true) ? " --game-optimization" : " --no-game-optimization");
        a.Append(B("sairDepois", false) ? " --quit-after" : " --no-quit-after");
        a.Append(B("mouseRemoto", false) ? " --absolute-mouse" : " --no-absolute-mouse");
        a.Append(" --capture-system-keys " + (B("capturarTeclas", true) ? G("capturarQuando", "fullscreen") : "never"));
        a.Append(B("trackpad", false) ? " --touchscreen-trackpad" : " --no-touchscreen-trackpad");
        a.Append(B("trocarMouse", false) ? " --mouse-buttons-swap" : " --no-mouse-buttons-swap");
        a.Append(B("inverterRolagem", false) ? " --reverse-scroll-direction" : " --no-reverse-scroll-direction");
        a.Append(B("trocarAB", false) ? " --swap-gamepad-buttons" : " --no-swap-gamepad-buttons");
        a.Append(B("multiControle", true) ? " --multi-controller" : " --no-multi-controller");
        a.Append(B("controleFundo", false) ? " --background-gamepad" : " --no-background-gamepad");
        a.Append(" --video-decoder " + G("decodificador", "auto"));
        a.Append(" --video-codec " + G("codec", "auto"));
        a.Append(B("yuv444", false) ? " --yuv444" : " --no-yuv444");
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

        int hz = Tela.Atual()[2];

        // Configurações Básicas
        var s1 = Secao(esq, "Configurações Básicas", largura);
        Rotulo(s1, "Resolução e FPS", 11f, Color.White);
        Rotulo(s1, "Valores muito altos para o seu PC ou rede podem causar atraso, engasgos ou erros. Esta TV está em " + hz + " Hz.", 8.5f, Color.FromArgb(190, 190, 190));
        var linha = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        var cRes = Combo(linha, "res", new[] { "auto|Automático (Quad HD)", "720|720p", "1080|1080p", "1440|1440p", "2160|4K" }, "auto", 190);
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
            var p = Resolver("192.168.0.1", cfg, hz);
            int v = p.Kbps / 1000;
            trilha.Value = Math.Max(trilha.Minimum, Math.Min(trilha.Maximum, v));
            lBit.Text = "Taxa de bits de vídeo: " + v + " Mbps" + (G("bitrate", "auto") == "auto" ? " (automático para " + p.Altura + "p " + p.Fps + " FPS)" : "") + (p.Aviso != "" ? "  ·  " + p.Aviso : "");
        };
        bool mexendo = false;
        trilha.Scroll += delegate { mexendo = true; cfg.Set("tv.bitrate", (trilha.Value * 1000).ToString()); atualizarBit(); mexendo = false; };
        bAuto.Click += delegate { cfg.Set("tv.bitrate", "auto"); atualizarBit(); };
        cRes.SelectedIndexChanged += delegate { if (!mexendo) atualizarBit(); };
        cFps.SelectedIndexChanged += delegate { if (!mexendo) atualizarBit(); };
        atualizarBit();
        Rotulo(s1, "Modo de exibição", 10f, Color.White);
        var lm = new FlowLayoutPanel { AutoSize = true }; s1.Controls.Add(lm);
        Combo(lm, "exibicao", new[] { "fullscreen|Tela cheia (Recomendado)", "borderless|Janela sem bordas", "windowed|Janela" }, "fullscreen", 230);
        var l2 = new FlowLayoutPanel { AutoSize = true }; s1.Controls.Add(l2);
        Check(l2, "vsync", "Sincronização vertical", false);
        Check(l2, "ritmo", "Ritmo do quadro", false);
        Check(s1, "hdr", "Habilitar HDR", false);
        Rotulo(s1, "Em casa o TELAR deixa os dois desligados para o menor atraso. Fora de casa liga os dois.", 8.5f, Color.FromArgb(190, 190, 190));

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
        Check(s7, "foraAuto", "Ajuste automático fora de casa (Tailscale): 60 FPS e taxa de bits baixa", true);

        Pagina(config, "Configurações", true);
        titulo.Text = "Configurações  ·  TELAR v" + App.Versao;
    }

    FlowLayoutPanel Secao(FlowLayoutPanel coluna, string nome, int largura)
    {
        coluna.Controls.Add(new Label { Text = nome, ForeColor = Color.FromArgb(90, 200, 180), AutoSize = true, Margin = new Padding(4, 12, 0, 4), Font = new Font("Segoe UI", 10f) });
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
            "1. Instale o TELAR no PC Forte (opção PC Forte) e deixe ele ligado.\n" +
            "2. Aqui, o PC Forte aparece sozinho em Computadores. Se não aparecer, clique em + e digite o IP mostrado no PC Forte.\n" +
            "3. Clique no PC com cadeado: aparece um PIN. Digite o PIN no TELAR do PC Forte e clique em Liberar.\n" +
            "4. Clique no PC e escolha Desktop para ver a tela inteira do PC Forte.\n\n" +
            "Durante o jogo: Ctrl+Alt+Shift+Q sai, Ctrl+Alt+Shift+S mostra as estatísticas, Ctrl+Alt+Shift+Z solta o mouse.\n" +
            "Fora de casa: instale o Tailscale nos dois PCs e use o IP 100.x do PC Forte.",
            "Ajuda do TELAR");
    }
}
