// TELAR - visual das telas principais (organização no estilo AnyDesk): topo com o nome, barra de endereço,
// faixa de aviso, "Este dispositivo" com o número grande, abas e cartões.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

static class Cores
{
    public static readonly Color Fundo = Color.FromArgb(30, 30, 30);
    public static readonly Color Topo = Color.FromArgb(43, 43, 43);
    public static readonly Color Endereco = Color.FromArgb(37, 37, 37);
    public static readonly Color Laranja = Color.FromArgb(245, 124, 32);
    public static readonly Color Texto = Color.FromArgb(235, 235, 235);
    public static readonly Color Apagado = Color.FromArgb(150, 150, 150);
    public static readonly Color Linha = Color.FromArgb(70, 70, 70);
    public static readonly Color Verde = Color.FromArgb(70, 190, 100);
    public static readonly Color Vermelho = Color.FromArgb(225, 75, 60);
    // cores dos cartões (cada PC ganha sempre a mesma, pelo nome)
    public static readonly Color[] Cartoes =
    {
        Color.FromArgb(214, 96, 52), Color.FromArgb(200, 82, 140), Color.FromArgb(222, 168, 52),
        Color.FromArgb(64, 120, 200), Color.FromArgb(120, 88, 196), Color.FromArgb(40, 150, 130)
    };
    public static Color DoNome(string s)
    {
        int h = 0; foreach (char c in s ?? "") h = h * 31 + c;
        return Cartoes[Math.Abs(h % Cartoes.Length)];
    }
}

// cartão no estilo das "Notícias" do AnyDesk: retângulo colorido com título, linhas de texto e um desenho
class Cartao : Control
{
    public enum Desenho { Nenhum, Pc, Cadeado, Desligado, Controle, Engrenagem, Rede, Ok }
    public Desenho Icone = Desenho.Nenhum;
    public Color Cor = Cores.Laranja;
    public string Titulo = "", Linha1 = "", Linha2 = "";
    // selo no canto (ex.: "Online" com bolinha verde, "Offline" com bolinha cinza)
    public string Selo = "";
    public Color CorSelo = Cores.Verde;
    bool sobre;
    static readonly Font fonteTitulo = new Font("Segoe UI Semibold", 10.5f), fonteLinha = new Font("Segoe UI", 9f), fonteSelo = new Font("Segoe UI Semibold", 8f);

    public Cartao()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(190, 145); Margin = new Padding(0, 0, 10, 10); Cursor = Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { sobre = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { sobre = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var c1 = sobre ? ControlPaint.Light(Cor, 0.15f) : Cor;
        using (var fundo = new LinearGradientBrush(ClientRectangle, c1, ControlPaint.Dark(c1, 0.12f), 135f)) g.FillRectangle(fundo, ClientRectangle);
        using (var branco = new SolidBrush(Color.White))
        using (var claro = new SolidBrush(Color.FromArgb(225, 255, 255, 255)))
        using (var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
        {
            g.DrawString(Titulo, fonteTitulo, branco, new RectangleF(12, 12, Width - 24, 22), fmt);
            g.DrawString(Linha1, fonteLinha, claro, new RectangleF(12, 36, Width - 24, 18), fmt);
            g.DrawString(Linha2, fonteLinha, claro, new RectangleF(12, 54, Width - 24, 18), fmt);
            DesenharIcone(g, new RectangleF(Width - 70, Height - 62, 56, 48));
            if (Selo != "") DesenharSelo(g, branco);
        }
    }

    // pílula escura no canto de baixo à esquerda: bolinha colorida + texto (Online / Offline / ...)
    void DesenharSelo(Graphics g, Brush texto)
    {
        var tam = g.MeasureString(Selo, fonteSelo);
        var r = new RectangleF(10, Height - 32, tam.Width + 26, 22);
        using (var fundo = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
        using (var p = new GraphicsPath())
        {
            float d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 90, 180); p.AddArc(r.Right - d, r.Y, d, d, 270, 180); p.CloseFigure();
            g.FillPath(fundo, p);
        }
        using (var bola = new SolidBrush(CorSelo)) g.FillEllipse(bola, r.X + 8, r.Y + 7, 8, 8);
        g.DrawString(Selo, fonteSelo, texto, r.X + 19, r.Y + 4);
    }

    void DesenharIcone(Graphics g, RectangleF r)
    {
        if (Icone == Desenho.Nenhum) return;
        using (var br = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
        using (var pen = new Pen(Color.FromArgb(235, 255, 255, 255), 4))
        using (var furo = new SolidBrush(Cor))
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            if (Icone == Desenho.Pc || Icone == Desenho.Cadeado || Icone == Desenho.Desligado)
            {
                g.FillRectangle(br, r.X + 4, r.Y + 2, r.Width - 8, r.Height - 16);
                g.FillRectangle(furo, r.X + 9, r.Y + 7, r.Width - 18, r.Height - 26);
                g.FillRectangle(br, cx - 4, r.Bottom - 14, 8, 8);
                g.FillRectangle(br, cx - 12, r.Bottom - 7, 24, 5);
                float my = r.Y + 7 + (r.Height - 26) / 2;
                if (Icone == Desenho.Cadeado)
                {
                    using (var p2 = new Pen(Color.White, 3)) g.DrawArc(p2, cx - 5, my - 10, 10, 11, 180, 180);
                    g.FillRectangle(br, cx - 8, my - 4, 16, 12);
                }
                else if (Icone == Desenho.Desligado)
                {
                    using (var p2 = new Pen(Color.White, 3)) { g.DrawLine(p2, cx - 6, my - 6, cx + 6, my + 6); g.DrawLine(p2, cx + 6, my - 6, cx - 6, my + 6); }
                }
                else g.FillPolygon(br, new[] { new PointF(cx - 5, my - 8), new PointF(cx - 5, my + 8), new PointF(cx + 8, my) });
            }
            else if (Icone == Desenho.Controle)
            {
                g.FillEllipse(br, r.X + 2, r.Y + 10, r.Width - 4, r.Height - 18);
                g.FillRectangle(furo, r.X + 13, cy - 2, 12, 4); g.FillRectangle(furo, r.X + 17, cy - 6, 4, 12);
                g.FillEllipse(furo, r.Right - 20, cy - 7, 6, 6); g.FillEllipse(furo, r.Right - 14, cy - 1, 6, 6);
            }
            else if (Icone == Desenho.Engrenagem)
            {
                for (int i = 0; i < 8; i++) { var m = g.Transform; g.TranslateTransform(cx, cy); g.RotateTransform(i * 45); g.FillRectangle(br, -4, -20, 8, 10); g.Transform = m; }
                g.FillEllipse(br, cx - 14, cy - 14, 28, 28); g.FillEllipse(furo, cx - 6, cy - 6, 12, 12);
            }
            else if (Icone == Desenho.Rede)
            {
                g.DrawArc(pen, cx - 20, cy - 10, 40, 40, 225, 90); g.DrawArc(pen, cx - 12, cy - 2, 24, 24, 225, 90);
                g.FillEllipse(br, cx - 4, cy + 12, 8, 8);
            }
            else if (Icone == Desenho.Ok)
            {
                g.DrawLines(new Pen(Color.White, 6), new[] { new PointF(cx - 14, cy), new PointF(cx - 4, cy + 10), new PointF(cx + 16, cy - 12) });
            }
        }
    }
}

// janela principal: topo, barra de endereço, faixa de aviso, "Este dispositivo", abas e área dos cartões
class Casca : Form
{
    protected readonly Panel topo, barra, faixa, heroi, abas, conteudo, subtopo;
    protected readonly Label lAviso, lHeroiRotulo, lHeroiNumero, lSubtitulo;
    protected readonly TextBox tEndereco;
    protected readonly Button bConectar, bMenu, bVoltar;
    protected readonly FlowLayoutPanel cartoes, botoesHeroi;
    protected readonly Panel bolinha;
    readonly List<Label> rotulosAbas = new List<Label>();
    protected int abaAtual;
    protected event Action<int> AbaMudou;
    protected event Action<string> Conectar;
    protected event Action AbrirMenu;
    Control paginaExtra;

    public Casca(string titulo)
    {
        Text = titulo; BackColor = Cores.Fundo; ForeColor = Cores.Texto; Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen; Size = new Size(1180, 760); MinimumSize = new Size(760, 520);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // topo: símbolo + nome
        topo = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Cores.Topo };
        var logo = new PictureBox { Size = new Size(18, 18), Location = new Point(10, 8), SizeMode = PictureBoxSizeMode.StretchImage };
        try { logo.Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath).ToBitmap(); } catch { }
        topo.Controls.Add(logo);
        topo.Controls.Add(new Label { Text = "TELAR", Font = new Font("Segoe UI Semibold", 10f), ForeColor = Cores.Texto, AutoSize = true, Location = new Point(34, 8) });
        topo.Controls.Add(new Label { Text = "v" + App.Versao, Font = new Font("Segoe UI", 8.5f), ForeColor = Cores.Apagado, AutoSize = true, Location = new Point(84, 10) });

        // barra de endereço: bolinha de status, campo, seta e menu
        barra = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Cores.Endereco, Padding = new Padding(8, 5, 6, 5) };
        bolinha = new Panel { Size = new Size(12, 12), Location = new Point(10, 11), BackColor = Cores.Verde };
        var forma = new GraphicsPath(); forma.AddEllipse(0, 0, 12, 12); bolinha.Region = new Region(forma);
        tEndereco = new TextBox { BorderStyle = BorderStyle.None, BackColor = Cores.Endereco, ForeColor = Cores.Texto, Font = new Font("Segoe UI", 10f), Location = new Point(32, 9), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
        bConectar = BotaoGlifo("", "Conectar");
        bMenu = BotaoGlifo("", "Menu");
        bConectar.Dock = DockStyle.Right; bMenu.Dock = DockStyle.Right;
        barra.Controls.AddRange(new Control[] { bolinha, tEndereco, bConectar, bMenu });
        barra.Resize += delegate { tEndereco.Width = Math.Max(100, barra.Width - 32 - 90); };
        Marcador(tEndereco, "Digite o endereço remoto (IP do PC Forte)");
        tEndereco.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && Conectar != null) { e.SuppressKeyPress = true; Conectar(TextoEndereco()); } };
        bConectar.Click += delegate { if (Conectar != null) Conectar(TextoEndereco()); };
        bMenu.Click += delegate { if (AbrirMenu != null) AbrirMenu(); };

        // faixa de aviso (laranja)
        faixa = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Cores.Laranja };
        lAviso = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Segoe UI", 9f) };
        faixa.Controls.Add(lAviso);

        // "Este dispositivo  1234"
        heroi = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Cores.Fundo };
        lHeroiRotulo = new Label { Text = "Este dispositivo", AutoSize = true, Font = new Font("Segoe UI", 13f), ForeColor = Cores.Texto };
        lHeroiNumero = new Label { AutoSize = true, Font = new Font("Segoe UI", 30f), ForeColor = Cores.Laranja };
        botoesHeroi = new FlowLayoutPanel { AutoSize = true, BackColor = Cores.Fundo, WrapContents = false };
        heroi.Controls.AddRange(new Control[] { lHeroiRotulo, lHeroiNumero, botoesHeroi });
        heroi.Resize += delegate { CentralizarHeroi(); };

        // abas
        abas = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Cores.Fundo, Padding = new Padding(20, 0, 20, 0) };
        abas.Paint += (s, e) => { using (var p = new Pen(Cores.Linha)) e.Graphics.DrawLine(p, 20, abas.Height - 1, abas.Width - 20, abas.Height - 1); };

        // subtopo das páginas extras (Configurações): voltar + título
        subtopo = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Cores.Fundo, Visible = false };
        bVoltar = BotaoGlifo("", "Voltar"); bVoltar.Dock = DockStyle.Left; bVoltar.BackColor = Cores.Fundo;
        lSubtitulo = new Label { Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 13f), ForeColor = Cores.Texto, TextAlign = ContentAlignment.MiddleLeft };
        subtopo.Controls.Add(lSubtitulo); subtopo.Controls.Add(bVoltar);
        bVoltar.Click += delegate { FecharPagina(); };

        conteudo = new Panel { Dock = DockStyle.Fill, BackColor = Cores.Fundo, Padding = new Padding(20, 14, 20, 10) };
        cartoes = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Cores.Fundo };
        conteudo.Controls.Add(cartoes);

        Controls.Add(conteudo); Controls.Add(subtopo); Controls.Add(abas); Controls.Add(heroi); Controls.Add(faixa); Controls.Add(barra); Controls.Add(topo);
    }

    protected Button BotaoGlifo(string glifo, string dica)
    {
        var b = new Button { Text = glifo, Font = new Font("Segoe MDL2 Assets", 11f), Width = 40, FlatStyle = FlatStyle.Flat, ForeColor = Cores.Texto, BackColor = Cores.Endereco, Cursor = Cursors.Hand, TabStop = false };
        b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 60);
        new ToolTip().SetToolTip(b, dica);
        return b;
    }

    // texto cinza dentro do campo enquanto ele está vazio
    string marcador = "";
    void Marcador(TextBox t, string texto)
    {
        marcador = texto;
        Action mostrar = () => { if (t.Text == "" || t.Text == marcador) { t.Text = marcador; t.ForeColor = Cores.Apagado; } };
        t.GotFocus += delegate { if (t.Text == marcador) { t.Text = ""; t.ForeColor = Cores.Texto; } };
        t.LostFocus += delegate { mostrar(); };
        mostrar();
    }
    protected string TextoEndereco() { return tEndereco.Text == marcador ? "" : tEndereco.Text.Trim(); }
    protected void MarcadorEndereco(string texto) { bool vazio = TextoEndereco() == ""; marcador = texto; if (vazio) { tEndereco.Text = texto; tEndereco.ForeColor = Cores.Apagado; } }

    protected void Aviso(string texto, Color cor) { lAviso.Text = texto; faixa.BackColor = cor; }
    protected void Status(bool ok) { bolinha.BackColor = ok ? Cores.Verde : Cores.Vermelho; }

    protected void Heroi(string rotulo, string numero)
    {
        lHeroiRotulo.Text = rotulo; lHeroiNumero.Text = numero; CentralizarHeroi();
    }
    protected Button BotaoHeroi(string glifo, string dica)
    {
        var b = BotaoGlifo(glifo, dica); b.BackColor = Cores.Fundo; b.Height = 30; b.Margin = new Padding(2, 0, 2, 0);
        botoesHeroi.Controls.Add(b); CentralizarHeroi(); return b;
    }
    void CentralizarHeroi()
    {
        int w = lHeroiRotulo.Width + 16 + lHeroiNumero.Width + 12 + botoesHeroi.Width;
        int x = Math.Max(10, (heroi.Width - w) / 2);
        lHeroiRotulo.Location = new Point(x, 40);
        lHeroiNumero.Location = new Point(x + lHeroiRotulo.Width + 16, 22);
        botoesHeroi.Location = new Point(lHeroiNumero.Right + 12, 36);
    }

    protected void DefinirAbas(params string[] nomes)
    {
        abas.Controls.Clear(); rotulosAbas.Clear();
        int x = 20;
        for (int i = 0; i < nomes.Length; i++)
        {
            int indice = i;
            var l = new Label { Text = nomes[i], AutoSize = true, Location = new Point(x, 12), Cursor = Cursors.Hand, Font = new Font("Segoe UI", 10f) };
            l.Click += delegate { EscolherAba(indice); };
            abas.Controls.Add(l); rotulosAbas.Add(l);
            x += TextRenderer.MeasureText(nomes[i], l.Font).Width + 22;
        }
        EscolherAba(0);
    }
    protected void EscolherAba(int i)
    {
        abaAtual = i;
        for (int k = 0; k < rotulosAbas.Count; k++)
        {
            rotulosAbas[k].ForeColor = k == i ? Cores.Laranja : Cores.Apagado;
            rotulosAbas[k].Font = k == i ? fonteAbaAtiva : fonteAba;
        }
        if (AbaMudou != null) AbaMudou(i);
    }
    static readonly Font fonteAba = new Font("Segoe UI", 10f), fonteAbaAtiva = new Font("Segoe UI Semibold", 10f), fonteVazio = new Font("Segoe UI", 10.5f);

    // apaga os cartões antigos liberando a memória deles (sem isso a janela fica cada vez mais pesada)
    protected void LimparCartoes()
    {
        var velhos = cartoes.Controls.Cast<Control>().ToList();
        cartoes.Controls.Clear();
        foreach (var c in velhos) { if (c.ContextMenuStrip != null) c.ContextMenuStrip.Dispose(); c.Dispose(); }
    }

    protected Cartao NovoCartao(string titulo, string l1, string l2, Color cor, Cartao.Desenho icone)
    {
        var c = new Cartao { Titulo = titulo, Linha1 = l1, Linha2 = l2, Cor = cor, Icone = icone };
        cartoes.Controls.Add(c); return c;
    }
    protected void TextoVazio(string texto)
    {
        cartoes.Controls.Add(new Label { Text = texto, AutoSize = false, Size = new Size(640, 60), ForeColor = Cores.Apagado, Font = fonteVazio });
    }

    // páginas extras (ex.: Configurações) ocupam o lugar do "Este dispositivo" e das abas
    protected void AbrirPagina(Control pagina, string titulo)
    {
        if (paginaExtra != null) conteudo.Controls.Remove(paginaExtra);
        paginaExtra = pagina; pagina.Dock = DockStyle.Fill;
        cartoes.Visible = false; heroi.Visible = false; abas.Visible = false;
        lSubtitulo.Text = titulo; subtopo.Visible = true;
        conteudo.Controls.Add(pagina); pagina.BringToFront();
    }
    protected virtual void FecharPagina()
    {
        if (paginaExtra != null) { conteudo.Controls.Remove(paginaExtra); paginaExtra.Dispose(); paginaExtra = null; }
        subtopo.Visible = false; heroi.Visible = true; abas.Visible = true; cartoes.Visible = true;
        EscolherAba(abaAtual);
    }
}
