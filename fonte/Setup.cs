// TELAR-Setup - instalador único. Leva dentro dele o TELAR, o motor do PC Forte e o receptor do PC da TV.
// Compilado junto com Telar.cs (reaproveita caminhos, cores, Config e Instalacao).
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

static class Setup
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Directory.CreateDirectory(App.Pasta);
        // TELAR-Setup.exe --forte / --tv já começa a instalar sem perguntar
        string papel = args.Contains("--forte") ? "forte" : args.Contains("--tv") ? "tv" : "";
        Application.Run(new TelaSetup(papel));
        return 0;
    }
}

class TelaSetup : Janela
{
    readonly Button bForte, bTV;
    readonly Label lPasso;
    readonly TextBox log;

    public TelaSetup(string papelDireto) : base("Instalar TELAR", 520, 430)
    {
        Rotulo("TELAR", 24, 16, 300, 20f, App.Texto, true);
        Rotulo("Qual PC é este?", 26, 62, 460, 10f, App.Apagado);
        bForte = Botao("PC Forte (sala)\nroda os jogos", 24, 96, 230, App.Casa, 96);
        bTV = Botao("PC da TV\nonde você joga", 266, 96, 230, App.Cinza, 96);
        bForte.Font = bTV.Font = new Font("Segoe UI Semibold", 11.5f);
        lPasso = Rotulo("", 26, 206, 470, 10f, App.Texto, true);
        log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = App.Cartao, ForeColor = App.Apagado, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9f), Bounds = new Rectangle(26, 238, 470, 172) };
        Controls.Add(log);
        bForte.Click += delegate { Comecar("forte"); };
        bTV.Click += delegate { Comecar("tv"); };
        if (papelDireto != "") Shown += delegate { Comecar(papelDireto); };
    }

    void Log(string m) { BeginInvoke((Action)(() => log.AppendText(m + "\r\n"))); }
    void Passo(string m) { BeginInvoke((Action)(() => lPasso.Text = m)); Log(m); }

    void Comecar(string papel)
    {
        bForte.Enabled = bTV.Enabled = false;
        new Thread(() =>
        {
            bool ok = Instalar(papel);
            BeginInvoke((Action)(() =>
            {
                lPasso.ForeColor = ok ? App.Ok : App.Ruim;
                lPasso.Text = ok ? "Pronto. O TELAR foi aberto." : "A instalação não terminou. Veja o motivo abaixo.";
                if (!ok) bForte.Enabled = bTV.Enabled = true;
            }));
        }) { IsBackground = true }.Start();
    }

    bool Instalar(string papel)
    {
        try
        {
            Passo("Fechando o TELAR, se estiver aberto...");
            foreach (var nome in new[] { "TELAR", "telar-cliente" })
                foreach (var p in Process.GetProcessesByName(nome)) try { p.Kill(); p.WaitForExit(5000); } catch { }

            string raizPF = Path.Combine(App.ProgramFiles, "TELAR");
            using (var s = typeof(Setup).Assembly.GetManifestResourceStream("payload.zip"))
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                Passo("Copiando o TELAR...");
                Extrair(zip, "telar/", App.PastaPrograma);
                Extrair(zip, "LICENCAS.txt", App.PastaPrograma);
                if (papel == "forte")
                {
                    Passo("Copiando o motor de transmissão...");
                    Util.RodarCodigo("sc", "stop " + App.Servico, 20000);
                    for (int i = 0; i < 40 && Process.GetProcessesByName("sunshine").Length > 0; i++) Thread.Sleep(250);
                    Extrair(zip, "motor/", App.MotorDir);
                    Extrair(zip, "drivers/", Path.Combine(raizPF, "drivers"));
                    Extrair(zip, "LICENCAS.txt", raizPF);
                }
                else
                {
                    Passo("Copiando o receptor...");
                    Extrair(zip, "cliente/", Path.Combine(App.PastaPrograma, "cliente"));
                }
            }

            string telar = Path.Combine(App.PastaPrograma, "TELAR.exe");
            if (papel == "forte")
            {
                Passo("Ligando o motor (placa de vídeo, firewall, controle)...");
                File.Delete(Path.Combine(Path.GetTempPath(), "telar-motor.txt"));
                int c = Util.RodarCodigo(telar, "--instalar-forte", 10 * 60 * 1000);
                string resumo = Path.Combine(Path.GetTempPath(), "telar-motor.txt");
                if (File.Exists(resumo)) foreach (var l in File.ReadAllLines(resumo)) Log("  " + l);
                if (c != 0) { Log("O motor não ligou (código " + c + ")."); return false; }
            }
            else
            {
                Util.RodarCodigo("powercfg", "/setactive SCHEME_MIN", 10000);
                Log("  Plano de energia: Alto desempenho");
            }

            var cfg = Config.Carregar();
            cfg.Set("papel", papel);
            cfg.Salvar();
            Instalacao.Registrar();
            Log("  Atalhos criados na área de trabalho e no menu Iniciar");

            // abre o TELAR sem herdar o modo administrador do instalador
            Process.Start("explorer.exe", "\"" + telar + "\"");
            return true;
        }
        catch (Exception e)
        {
            Log("Erro: " + e.Message);
            return false;
        }
    }

    // copia do pacote todas as entradas que começam com o prefixo (ou o arquivo exato)
    static void Extrair(ZipArchive zip, string prefixo, string destino)
    {
        foreach (var e in zip.Entries)
        {
            // o .NET Framework grava os caminhos do zip com "\"; aceita os dois
            string nome = e.FullName.Replace('\\', '/');
            string rel;
            if (prefixo.EndsWith("/"))
            {
                if (!nome.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase) || nome.EndsWith("/")) continue;
                rel = nome.Substring(prefixo.Length);
            }
            else
            {
                if (!string.Equals(nome, prefixo, StringComparison.OrdinalIgnoreCase)) continue;
                rel = Path.GetFileName(nome);
            }
            string alvo = Path.Combine(destino, rel.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(alvo));
            e.ExtractToFile(alvo, true);
        }
    }
}
