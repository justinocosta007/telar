// TELAR - joga no PC da TV usando a força do PC Forte.
// Um único exe: no PC Forte cuida do motor de transmissão; no PC da TV escolhe a qualidade e abre o jogo.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("TELAR")]
[assembly: System.Reflection.AssemblyProduct("TELAR")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

static class App
{
    public const int Versao = 9;
    public const string SiteUrl = "https://justinocosta007.github.io/telar/";
    public static readonly string Pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TELAR");
    public static readonly string PastaPrograma = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TELAR");
    public static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    // motor do PC Forte: fica em Program Files porque roda como serviço do Windows
    public static string MotorDir { get { return Path.Combine(ProgramFiles, "TELAR", "motor"); } }
    public static string MotorExe { get { return Path.Combine(MotorDir, "sunshine.exe"); } }
    public static string MotorConf { get { return Path.Combine(MotorDir, "config", "sunshine.conf"); } }
    public const string Servico = "TELARMotor";
    // receptor do PC da TV: fica junto do TELAR.exe
    public static string ClienteExe { get { return Path.Combine(PastaPrograma, "cliente", "telar-cliente.exe"); } }
    public static string TailscaleExe { get { return Path.Combine(ProgramFiles, "Tailscale", "tailscale.exe"); } }

    public static readonly Color Fundo = Color.FromArgb(20, 23, 27);
    public static readonly Color Cartao = Color.FromArgb(32, 36, 42);
    public static readonly Color Texto = Color.FromArgb(232, 235, 239);
    public static readonly Color Apagado = Color.FromArgb(148, 158, 170);
    public static readonly Color Casa = Color.FromArgb(14, 138, 118);
    public static readonly Color Fora = Color.FromArgb(84, 80, 214);
    public static readonly Color Cinza = Color.FromArgb(58, 64, 74);
    public static readonly Color Ok = Color.FromArgb(80, 200, 120);
    public static readonly Color Ruim = Color.FromArgb(240, 110, 100);

    [STAThread]
    static int Main(string[] args)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
        Directory.CreateDirectory(Pasta);

        if (args.Length >= 1 && args[0] == "--instalar-forte") return Motor.InstalarForte();
        if (args.Length >= 1 && args[0] == "--desinstalar") return Instalacao.Desinstalar();

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // um erro inesperado não pode fechar o TELAR calado: anota em erros.txt e segue
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => AnotarErro(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => AnotarErro(e.ExceptionObject as Exception);
        if (Atualizacao.Verificar(args)) return 0;

        var cfg = Config.Carregar();
        if (cfg.Get("papel") == "") cfg.Set("papel", EscolherPapel.Perguntar());
        if (cfg.Get("papel") == "") return 0;
        cfg.Salvar();

        if (cfg.Get("papel") == "forte")
        {
            // só um TELAR do PC Forte por vez (ele abre sozinho quando o Windows liga e fica perto do relógio);
            // abrir de novo pelo atalho só traz a janela que já está aberta
            bool nova; var unica = new Mutex(true, "TELAR-PC-Forte-Janela", out nova);
            if (!nova)
            {
                if (!args.Contains("--minimizado"))
                    try { using (var ev = EventWaitHandle.OpenExisting(TelaForte.SinalMostrar)) ev.Set(); } catch { }
                return 0;
            }
            Application.Run(new TelaForte(cfg, args.Contains("--minimizado")));
            GC.KeepAlive(unica);
        }
        else Application.Run(new TelaTV(cfg));
        return 0;
    }

    public static void AnotarErro(Exception e)
    {
        if (e == null) return;
        try { File.AppendAllText(Path.Combine(Pasta, "erros.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " v" + Versao + "  " + e + "\r\n\r\n"); } catch { }
    }
}

// ---------------- configuração simples chave=valor ----------------
class Config
{
    readonly Dictionary<string, string> d = new Dictionary<string, string>();
    static string Arq { get { return Path.Combine(App.Pasta, "config.ini"); } }
    public static Config Carregar()
    {
        var c = new Config();
        if (File.Exists(Arq))
            foreach (var l in File.ReadAllLines(Arq, Encoding.UTF8))
            {
                int i = l.IndexOf('=');
                if (i > 0) c.d[l.Substring(0, i)] = l.Substring(i + 1);
            }
        return c;
    }
    public string Get(string k) { string v; return d.TryGetValue(k, out v) ? v : ""; }
    public void Set(string k, string v) { d[k] = v ?? ""; }
    public void Salvar() { File.WriteAllLines(Arq, d.Select(p => p.Key + "=" + p.Value).ToArray(), Encoding.UTF8); }
}

// ---------------- atalhos, registro em "Aplicativos instalados" e desinstalação ----------------
static class Instalacao
{
    static string AtalhoDesktop { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "TELAR.lnk"); } }
    static string AtalhoMenu { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "TELAR.lnk"); } }
    const string ChaveUninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TELAR";

    public static void Registrar()
    {
        string exe = Path.Combine(App.PastaPrograma, "TELAR.exe");
        Atalho(AtalhoDesktop, exe);
        Atalho(AtalhoMenu, exe);
        using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(ChaveUninstall))
        {
            k.SetValue("DisplayName", "TELAR");
            k.SetValue("DisplayVersion", App.Versao.ToString());
            k.SetValue("Publisher", "TELAR");
            k.SetValue("DisplayIcon", exe + ",0");
            k.SetValue("InstallLocation", App.PastaPrograma);
            k.SetValue("UninstallString", "\"" + exe + "\" --desinstalar");
            k.SetValue("NoModify", 1); k.SetValue("NoRepair", 1);
            k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            long bytes = 0;
            foreach (var pasta in new[] { App.PastaPrograma, Path.Combine(App.ProgramFiles, "TELAR") })
                if (Directory.Exists(pasta)) foreach (var f in Directory.GetFiles(pasta, "*", SearchOption.AllDirectories)) try { bytes += new FileInfo(f).Length; } catch { }
            k.SetValue("EstimatedSize", (int)(bytes / 1024), Microsoft.Win32.RegistryValueKind.DWord);
        }
    }

    // PC Forte: o TELAR abre minimizado quando o Windows liga, para aceitar o PC da TV sem ninguém mexer aqui
    public static void IniciarComWindows()
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                k.SetValue("TELAR", "\"" + Path.Combine(App.PastaPrograma, "TELAR.exe") + "\" --minimizado");
        }
        catch { }
    }

    public static bool Admin
    {
        get { return new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); }
    }

    public static int Desinstalar()
    {
        Application.EnableVisualStyles();
        if (MessageBox.Show("Remover o TELAR deste PC?", "TELAR", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
        if (Directory.Exists(App.MotorDir) && !Admin)
        {
            // o motor é um serviço do Windows: removê-lo pede administrador
            try { using (var p = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--desinstalar") { UseShellExecute = true, Verb = "runas" })) p.WaitForExit(); }
            catch { }
            return 0;
        }
        Util.RodarCodigo("sc", "stop " + App.Servico, 20000);
        Thread.Sleep(1500);
        Util.RodarCodigo("sc", "delete " + App.Servico, 20000);
        Util.RodarCodigo("netsh", "advfirewall firewall delete rule name=\"TELAR Motor\"", 20000);
        foreach (var nome in new[] { "sunshine", "telar-cliente" })
            foreach (var p in Process.GetProcessesByName(nome)) try { p.Kill(); } catch { }
        try { Directory.Delete(Path.Combine(App.ProgramFiles, "TELAR"), true); } catch { }
        try { File.Delete(AtalhoDesktop); File.Delete(AtalhoMenu); } catch { }
        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(ChaveUninstall, false); } catch { }
        try { Directory.Delete(App.Pasta, true); } catch { }
        // o próprio TELAR.exe está aberto: apaga a pasta depois que este processo sair
        Process.Start(new ProcessStartInfo("cmd.exe", "/c timeout /t 3 /nobreak >nul & rmdir /s /q \"" + App.PastaPrograma + "\"") { CreateNoWindow = true, UseShellExecute = false });
        MessageBox.Show("TELAR removido.", "TELAR");
        return 0;
    }

    static void Atalho(string lnk, string alvo)
    {
        Type t = Type.GetTypeFromProgID("WScript.Shell");
        dynamic sh = Activator.CreateInstance(t);
        dynamic a = sh.CreateShortcut(lnk);
        a.TargetPath = alvo; a.WorkingDirectory = Path.GetDirectoryName(alvo); a.IconLocation = alvo + ",0";
        a.Description = "TELAR";
        a.Save();
    }
}

// ---------------- atualização automática pelo GitHub Pages ----------------
static class Atualizacao
{
    // baixa com tempo-limite: no começo do Windows a internet às vezes demora e o TELAR não pode ficar preso esperando
    class Cliente : WebClient
    {
        protected override WebRequest GetWebRequest(Uri u) { var r = base.GetWebRequest(u); r.Timeout = 15000; return r; }
    }

    // version.json: {"version": 2, "exe": "telar.exe"}
    // devolve true quando baixou a versão nova e já abriu ela (este processo deve sair)
    public static bool Verificar(string[] args)
    {
        string atual = Application.ExecutablePath;
        try { if (File.Exists(atual + ".old")) File.Delete(atual + ".old"); } catch { }
        try
        {
            var wc = new Cliente();
            wc.Headers[HttpRequestHeader.CacheControl] = "no-cache";
            string json = wc.DownloadString(App.SiteUrl + "version.json?t=" + DateTime.UtcNow.Ticks);
            var mv = System.Text.RegularExpressions.Regex.Match(json, "\"version\"\\s*:\\s*(\\d+)");
            var me = System.Text.RegularExpressions.Regex.Match(json, "\"exe\"\\s*:\\s*\"([^\"]+)\"");
            if (!mv.Success || int.Parse(mv.Groups[1].Value) <= App.Versao) return false;
            string novo = atual + ".new";
            wc.DownloadFile(App.SiteUrl + (me.Success ? me.Groups[1].Value : "telar.exe") + "?t=" + DateTime.UtcNow.Ticks, novo);
            if (new FileInfo(novo).Length < 20000) { File.Delete(novo); return false; }
            // o Windows deixa renomear um exe em uso; a cópia velha é apagada na próxima abertura
            File.Move(atual, atual + ".old");
            try { File.Move(novo, atual); }
            catch { File.Move(atual + ".old", atual); return false; }
            // abre a versão nova do mesmo jeito (ex.: minimizado perto do relógio quando veio do Windows)
            Process.Start(atual, string.Join(" ", args.Select(a => "\"" + a + "\"").ToArray()));
            return true;
        }
        catch { return false; }
    }
}

// ---------------- utilidades ----------------
static class Util
{
    public static string Rodar(string exe, string args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(timeoutMs);
                return o;
            }
        }
        catch { return ""; }
    }
    public static int RodarCodigo(string exe, string args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return -999; }
                return p.ExitCode;
            }
        }
        catch { return -998; }
    }
    public static string IpLocal()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var props = ni.GetIPProperties();
            if (props.GatewayAddresses.Count == 0) continue;
            foreach (var a in props.UnicastAddresses)
            {
                if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var b = a.Address.GetAddressBytes();
                if (b[0] == 169 || (b[0] == 100 && b[1] >= 64 && b[1] < 128)) continue;
                return a.Address.ToString();
            }
        }
        return "";
    }
    // a conexão de rede usada agora é Wi-Fi? (aí o TELAR escolhe uma qualidade mais leve e estável)
    public static bool WifiAgora()
    {
        try
        {
            string meu = IpLocal();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.GetIPProperties().UnicastAddresses.Any(a => a.Address.ToString() == meu))
                    return ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            }
        }
        catch { }
        return false;
    }

    // endereço da rede de casa (não é Tailscale nem internet)
    public static bool RedeDeCasa(string ip)
    {
        var b = Bytes(ip);
        return b != null && (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] < 32) || (b[0] == 192 && b[1] == 168));
    }
    // endereço do Tailscale (100.64.0.0 a 100.127.255.255): só os seus aparelhos alcançam
    public static bool Tailscale(string ip)
    {
        var b = Bytes(ip);
        return b != null && b[0] == 100 && b[1] >= 64 && b[1] < 128;
    }
    // de onde o TELAR aceita conexão sozinho, sem código: rede de casa ou o seu Tailscale (nunca da internet aberta)
    public static bool Confiavel(string ip) { return RedeDeCasa(ip) || Tailscale(ip); }
    static byte[] Bytes(string ip)
    {
        if (ip == null) return null;
        ip = ip.Trim(); if (ip.StartsWith("::ffff:")) ip = ip.Substring(7);
        IPAddress a; if (!IPAddress.TryParse(ip, out a) || a.AddressFamily != AddressFamily.InterNetwork) return null;
        return a.GetAddressBytes();
    }

    // o PC desse endereço está ligado? Na rede de casa pergunta direto à placa de rede dele (ARP),
    // que responde mesmo com o firewall do Windows bloqueando tudo. Fora da rede de casa não dá para saber.
    [DllImport("iphlpapi.dll")] static extern int SendARP(uint destino, uint origem, byte[] mac, ref int tamanho);
    public static bool Ligado(string ip)
    {
        var b = Bytes(ip);
        if (b == null || !RedeDeCasa(ip)) return false;
        try { var mac = new byte[6]; int t = 6; return SendARP(BitConverter.ToUInt32(b, 0), 0, mac, ref t) == 0; }
        catch { return false; }
    }

    // código usado na rede de casa: sai da identidade do motor do PC Forte, então os dois lados chegam
    // no mesmo número sem ninguém digitar nada. O PC Forte só aceita esse código de endereços da rede de casa.
    public static string CodigoDaRede(string idMotor)
    {
        using (var h = SHA256.Create())
        {
            var b = h.ComputeHash(Encoding.UTF8.GetBytes("TELAR-rede-de-casa|" + idMotor.Trim().ToUpperInvariant()));
            return (BitConverter.ToUInt32(b, 0) % 9000 + 1000).ToString();
        }
    }

    // nome e identidade do motor de um PC Forte (null se não respondeu)
    public static string[] InfoMotor(string ip, int timeoutMs)
    {
        try
        {
            var req = (HttpWebRequest)WebRequest.Create("http://" + ip + ":47989/serverinfo");
            req.Timeout = timeoutMs; req.ReadWriteTimeout = timeoutMs; req.Proxy = null;
            using (var r = req.GetResponse())
            using (var s = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
            {
                string xml = s.ReadToEnd();
                var n = System.Text.RegularExpressions.Regex.Match(xml, "<hostname>([^<]*)</hostname>");
                var u = System.Text.RegularExpressions.Regex.Match(xml, "<uniqueid>([^<]*)</uniqueid>");
                return new[] { n.Success ? n.Groups[1].Value : ip, u.Success ? u.Groups[1].Value : "" };
            }
        }
        catch { return null; }
    }

    static bool PortaAberta(string ip, int timeoutMs)
    {
        using (var c = new TcpClient())
        {
            try
            {
                var r = c.BeginConnect(ip, 47989, null, null);
                if (!r.AsyncWaitHandle.WaitOne(timeoutMs) || !c.Connected) return false;
                c.EndConnect(r); return true;
            }
            catch { return false; }
        }
    }

    // acha PCs Fortes com o TELAR ligado. completa = varre a rede de casa (/24) e os aparelhos do Tailscale;
    // senão confere só os endereços já conhecidos (bem leve, para não pesar no Wi-Fi nem no PC da TV).
    // devolve "ip|nome"
    public static List<string> ProcurarPCsForte(IEnumerable<string> conhecidos, bool completa)
    {
        var alvos = new List<string>(conhecidos);
        if (completa)
        {
            string meu = IpLocal();
            if (meu != "") { string b = meu.Substring(0, meu.LastIndexOf('.') + 1); for (int i = 1; i < 255; i++) if (b + i != meu) alvos.Add(b + i); }
            if (File.Exists(App.TailscaleExe))
                foreach (var l in Rodar(App.TailscaleExe, "status", 5000).Split('\n'))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(l, @"^\s*(100\.\d+\.\d+\.\d+)\s");
                    if (m.Success) alvos.Add(m.Groups[1].Value);
                }
        }
        var achados = new List<string>();
        // poucos de cada vez: não abre centenas de conexões juntas
        System.Threading.Tasks.Parallel.ForEach(alvos.Distinct().ToList(), new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 24 }, ip =>
        {
            if (!PortaAberta(ip, ip.StartsWith("100.") ? 1200 : 400)) return;
            var info = InfoMotor(ip, 2500);
            if (info != null) lock (achados) achados.Add(ip + "|" + info[0]);
        });
        return achados.OrderBy(a => a.StartsWith("100.") ? 1 : 0).ToList();
    }

    public static string IpTailscale()
    {
        if (!File.Exists(App.TailscaleExe)) return "";
        string o = Rodar(App.TailscaleExe, "ip -4", 5000).Trim();
        return o.StartsWith("100.") ? o.Split('\n')[0].Trim() : "";
    }
}

// receptor do PC da TV: sempre aberto a partir da própria pasta, senão ele não entra no modo portátil
// (o portable.dat é procurado na pasta atual) e passa a guardar tudo no registro do Windows
static class Receptor
{
    static ProcessStartInfo Info(string args, bool oculto)
    {
        // sem console sempre: assim o receptor grava o registro dele num arquivo da pasta (é dali que o TELAR
        // lê a resolução e os FPS que chegaram); a janela da transmissão aparece normal
        return new ProcessStartInfo(App.ClienteExe, args)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = oculto ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
            WorkingDirectory = Path.GetDirectoryName(App.ClienteExe)
        };
    }
    public static Process Abrir(string args, bool oculto) { return Process.Start(Info(args, oculto)); }
    public static int Rodar(string args, int timeoutMs)
    {
        try
        {
            using (var p = Process.Start(Info(args, true)))
            {
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return -999; }
                return p.ExitCode;
            }
        }
        catch { return -998; }
    }
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct DEVMODE
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
    public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields;
    public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
    public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
    public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency,
        dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
}

static class Tela
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
    // largura, altura e Hz da tela principal (a TV, no PC da TV)
    public static int[] Atual()
    {
        var dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        EnumDisplaySettings(null, -1, ref dm);
        return new[] { dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency };
    }
    // a maior taxa de atualização que a tela aceita na resolução atual (o receptor em tela cheia muda para ela)
    public static int MaxHz()
    {
        var atual = Atual();
        int max = atual[2];
        var dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        for (int i = 0; i < 2000 && EnumDisplaySettings(null, i, ref dm); i++)
            if (dm.dmPelsWidth == atual[0] && dm.dmPelsHeight == atual[1] && dm.dmDisplayFrequency > max && dm.dmDisplayFrequency <= 240) max = dm.dmDisplayFrequency;
        return max;
    }
}

// ---------------- motor (Sunshine no PC Forte) ----------------
static class Motor
{
    public const string Usuario = "telar";
    // senha interna do motor: fica junto do motor (gravada só como administrador, lida pelo TELAR de qualquer usuário)
    static string ArqSenha { get { return Path.Combine(App.MotorDir, "config", "telar.key"); } }

    public static bool Instalado { get { return File.Exists(App.MotorExe); } }
    public static bool Configurado { get { return File.Exists(ArqSenha) || File.Exists(Path.Combine(App.Pasta, "motor.dat")); } }
    public static bool Ligado
    {
        get
        {
            try { using (var s = new ServiceController(App.Servico)) return s.Status == ServiceControllerStatus.Running; }
            catch { return false; }
        }
    }
    public static string Senha()
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(ArqSenha), null, DataProtectionScope.LocalMachine)); }
        catch { }
        // instalações até a v4 guardavam a senha na pasta do usuário
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(App.Pasta, "motor.dat")), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }

    // botão "Reparar motor": refaz a configuração como administrador
    public static bool Reparar(Action<string> log)
    {
        if (!Instalado) { log("O motor não está instalado. Rode o TELAR-Setup.exe de novo."); return false; }
        log("Ajustando o motor (o Windows vai pedir permissão de administrador)...");
        try
        {
            var psi = new ProcessStartInfo(Application.ExecutablePath, "--instalar-forte") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            using (var p = Process.Start(psi))
            {
                while (!p.HasExited) { Application.DoEvents(); Thread.Sleep(100); }
                if (p.ExitCode != 0) log("  falhou (código " + p.ExitCode + ")");
            }
        }
        catch { log("  Permissão negada. Clique de novo e aceite o pedido do Windows."); return false; }
        string resumo = Path.Combine(Path.GetTempPath(), "telar-motor.txt");
        if (File.Exists(resumo)) foreach (var l in File.ReadAllLines(resumo)) log("  " + l);
        return Ligado;
    }

    // roda como administrador (pelo instalador ou pelo botão Reparar):
    // ajusta o motor para a placa, cria a senha interna, o serviço, o firewall e o driver de controle
    public static int InstalarForte()
    {
        // duas execuções ao mesmo tempo (ex.: clique duplo em Reparar) gravariam senhas diferentes; uma espera a outra
        using (var mx = new Mutex(false, @"Global\TELAR-Motor-Instalar"))
        {
            try { mx.WaitOne(TimeSpan.FromMinutes(10)); } catch (AbandonedMutexException) { }
            try { return InstalarForteUmaVez(); }
            finally { try { mx.ReleaseMutex(); } catch { } }
        }
    }

    static int InstalarForteUmaVez()
    {
        var resumo = new List<string>();
        try
        {
            var bytes = new byte[18]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(bytes);
            string senha = Convert.ToBase64String(bytes).Replace('+', 'x').Replace('/', 'y').Replace("=", "");

            string gpus = Placas();
            var ajustes = AjustesDesejados(gpus);
            resumo.Add(gpus.Contains("NVIDIA") ? "Placa: NVIDIA (NVENC, latência mínima)" : gpus.Contains("Radeon") || gpus.Contains("AMD") ? "Placa: AMD (AMF, ultra baixa latência)" : "Placa: Intel (QuickSync)");

            // o motor precisa estar parado de vez: se ainda estiver fechando, ele regrava o estado com a senha antiga
            PararMotor();

            Directory.CreateDirectory(Path.GetDirectoryName(App.MotorConf));
            var linhas = File.Exists(App.MotorConf) ? File.ReadAllLines(App.MotorConf).ToList() : new List<string>();
            var chaves = new HashSet<string>(ajustes.Select(p => p.Key));
            linhas = linhas.Where(l => { int i = l.IndexOf('='); return i < 0 || !chaves.Contains(l.Substring(0, i).Trim()); }).ToList();
            linhas.AddRange(ajustes.Select(p => p.Key + " = " + p.Value));
            File.WriteAllLines(App.MotorConf, linhas.ToArray(), new UTF8Encoding(false));

            GravarSenha(senha);

            string fw = "\"" + App.MotorExe + "\"";
            Util.RodarCodigo("netsh", "advfirewall firewall delete rule name=\"TELAR Motor\"", 20000);
            Util.RodarCodigo("netsh", "advfirewall firewall add rule name=\"TELAR Motor\" dir=in action=allow protocol=tcp program=" + fw + " enable=yes", 20000);
            Util.RodarCodigo("netsh", "advfirewall firewall add rule name=\"TELAR Motor\" dir=in action=allow protocol=udp program=" + fw + " enable=yes", 20000);
            resumo.Add("Firewall liberado para o motor");

            // driver de controle virtual (Xbox/PS) para os controles ligados no PC da TV
            string driver = Path.Combine(Path.GetDirectoryName(App.MotorDir), "drivers", "ViGEmBus.exe");
            bool temDriver = Util.Rodar("sc", "query ViGEmBus", 10000).Contains("ViGEmBus");
            if (!temDriver && File.Exists(driver))
            {
                int c = Util.RodarCodigo(driver, "/exenoui /qn /norestart", 5 * 60 * 1000);
                resumo.Add(c == 0 || c == 3010 ? "Driver de controle instalado" : "Driver de controle não instalou (código " + c + ")");
            }
            else if (temDriver) resumo.Add("Driver de controle já instalado");

            Util.RodarCodigo("powercfg", "/change standby-timeout-ac 0", 10000);
            Util.RodarCodigo("powershell", "-NoProfile -Command \"Get-NetConnectionProfile | Where-Object NetworkCategory -ne 'DomainAuthenticated' | Set-NetConnectionProfile -NetworkCategory Private\"", 30000);
            resumo.Add("PC Forte não entra mais em suspensão na tomada");
            resumo.Add("Rede marcada como Privada");

            // serviço que liga o motor junto com o Windows
            string svc = "\"" + Path.Combine(App.MotorDir, "tools", "sunshinesvc.exe") + "\"";
            bool existe = Util.Rodar("sc", "query " + App.Servico, 10000).Contains(App.Servico);
            Util.RodarCodigo("sc", (existe ? "config " : "create ") + App.Servico + " binPath= \"" + svc.Replace("\"", "\\\"") + "\" start= auto DisplayName= \"TELAR Motor\"", 20000);
            Util.RodarCodigo("sc", "description " + App.Servico + " \"Transmite a tela, o áudio e recebe os controles do TELAR.\"", 10000);
            LigarMotor();
            // confere se a senha interna funciona; se o motor recusar, grava de novo com ele parado
            if (ConferirSenha() == HttpStatusCode.Unauthorized)
            {
                PararMotor(); GravarSenha(senha); LigarMotor();
                resumo.Add(ConferirSenha() == HttpStatusCode.OK ? "Senha interna regravada" : "Senha interna ainda recusada");
            }
            resumo.Add("Motor ligado");
            File.WriteAllLines(Path.Combine(Path.GetTempPath(), "telar-motor.txt"), resumo.ToArray());
            return 0;
        }
        catch (Exception e)
        {
            resumo.Add("Erro: " + e.Message);
            try { File.WriteAllLines(Path.Combine(Path.GetTempPath(), "telar-motor.txt"), resumo.ToArray()); } catch { }
            return 1;
        }
    }

    static string Placas()
    {
        string gpus = "";
        try
        {
            using (var q = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                foreach (ManagementObject o in q.Get()) gpus += o["Name"] + "; ";
        }
        catch { }
        return gpus;
    }

    // ajustes do motor que o TELAR quer (os mesmos na instalação e na atualização automática)
    static List<KeyValuePair<string, string>> AjustesDesejados(string gpus)
    {
        var a = new List<KeyValuePair<string, string>>();
        Action<string, string> A = (k, v) => a.Add(new KeyValuePair<string, string>(k, v));
        if (gpus.Contains("NVIDIA")) { A("encoder", "nvenc"); A("nvenc_preset", "2"); A("nvenc_twopass", "quarter_res"); }
        else if (gpus.Contains("Radeon") || gpus.Contains("AMD"))
        {
            A("encoder", "amdvce"); A("amd_usage", "ultralowlatency"); A("amd_rc", "cbr"); A("amd_quality", "balanced"); A("amd_vbaq", "enabled");
            // segura o tamanho de cada quadro: troca brusca de cena não vira um quadro gigante sem proteção contra perda (engasgo no Wi-Fi)
            A("amd_enforce_hrd", "enabled");
        }
        else { A("encoder", "quicksync"); A("qsv_preset", "veryfast"); }
        // durante a transmissão a tela do PC Forte acompanha o FPS da TV (ex.: 144 Hz vira 120 Hz): os quadros saem
        // em ritmo certinho, sem tremidas; quando a TV desconecta a tela volta ao normal
        A("dd_configuration_option", "verify_only"); A("dd_refresh_rate_option", "auto"); A("dd_config_revert_on_disconnect", "enabled");
        A("sunshine_name", "TELAR " + Environment.MachineName);
        A("system_tray", "disabled");
        A("origin_web_ui_allowed", "pc");
        return a;
    }

    // atualiza os ajustes do motor sem pedir administrador, pela interface do próprio motor (que roda como serviço).
    // Só reinicia o motor se ninguém conectou desde que ele ligou, para nunca derrubar uma transmissão.
    // Devolve null quando não precisou mudar nada.
    public static string AtualizarAjustes()
    {
        var js = new System.Web.Script.Serialization.JavaScriptSerializer();
        var atual = js.Deserialize<Dictionary<string, object>>(Api("GET", null, "/api/config"));
        foreach (var k in new[] { "status", "platform", "version" }) atual.Remove(k);
        var falta = AjustesDesejados(Placas()).Where(p => !atual.ContainsKey(p.Key) || Convert.ToString(atual[p.Key]) != p.Value).ToList();
        if (falta.Count == 0 || AlguemConectouDesdeQueLigou()) return null;
        foreach (var p in falta) atual[p.Key] = p.Value;
        if (!Api("POST", js.Serialize(atual), "/api/config").Replace(" ", "").Contains("\"status\":true")) return null;
        try { Api("POST", "{}", "/api/restart"); } catch { } // o motor fecha a conexão ao reiniciar
        for (int i = 0; i < 40; i++) { Thread.Sleep(1000); try { Api("GET", null, "/api/config"); return "Motor do TELAR otimizado: imagem mais estável e 120 Hz certinho."; } catch { } }
        return "Motor do TELAR otimizado. Ele está reiniciando.";
    }

    // alguém conectou desde que o motor ligou? (o registro do motor começa de novo a cada vez que ele liga)
    public static bool AlguemConectouDesdeQueLigou()
    {
        try
        {
            using (var fs = new FileStream(Path.Combine(App.MotorDir, "config", "sunshine.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(fs))
                return r.ReadToEnd().Contains("CLIENT CONNECTED");
        }
        catch { return true; } // na dúvida, não mexe
    }

    // encerra a transmissão que estiver rolando agora (o PC da TV continua conectado e pode jogar de novo depois)
    public static bool EncerrarTransmissao()
    {
        try { return Api("POST", "{}", "/api/apps/close").Replace(" ", "").Contains("\"status\":true"); }
        catch { return false; }
    }

    static void PararMotor()
    {
        try
        {
            using (var s = new ServiceController(App.Servico))
                if (s.Status != ServiceControllerStatus.Stopped) { s.Stop(); s.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30)); }
        }
        catch { } // serviço ainda não existe
        for (int i = 0; i < 80 && Process.GetProcessesByName("sunshine").Length > 0; i++) Thread.Sleep(250);
        foreach (var p in Process.GetProcessesByName("sunshine")) try { p.Kill(); p.WaitForExit(5000); } catch { }
    }

    static void LigarMotor()
    {
        using (var s = new ServiceController(App.Servico))
        {
            if (s.Status != ServiceControllerStatus.Running) s.Start();
            s.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
        }
    }

    static void GravarSenha(string senha)
    {
        Util.RodarCodigo(App.MotorExe, "--creds " + Usuario + " " + senha, 30000);
        File.WriteAllBytes(ArqSenha, ProtectedData.Protect(Encoding.UTF8.GetBytes(senha), null, DataProtectionScope.LocalMachine));
        // a pasta config do motor só deixa administradores escreverem; libera leitura deste arquivo para os usuários
        Util.RodarCodigo("icacls", "\"" + ArqSenha + "\" /grant *S-1-5-32-545:R", 10000);
    }

    // espera o motor responder e diz se aceitou a senha (OK) ou recusou (Unauthorized)
    static HttpStatusCode ConferirSenha()
    {
        for (int i = 0; i < 20; i++)
        {
            try { Api("GET", null); return HttpStatusCode.OK; }
            catch (WebException e)
            {
                var hr = e.Response as HttpWebResponse;
                if (hr != null) return hr.StatusCode;
            }
            Thread.Sleep(1000);
        }
        return HttpStatusCode.ServiceUnavailable;
    }

    // libera um PC da TV que está mostrando o PIN
    static string Api(string metodo, string corpoJson) { return Api(metodo, corpoJson, "/api/pin"); }

    // aparelhos (PCs da TV) que já conectaram: pares nome|uuid
    public static List<string[]> ListarAparelhos()
    {
        var r = new List<string[]>();
        try
        {
            string json = Api("GET", null, "/api/clients/list");
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(json, "\\{[^{}]*\\}"))
            {
                var n = System.Text.RegularExpressions.Regex.Match(m.Value, "\"name\"\\s*:\\s*\"([^\"]*)\"");
                var u = System.Text.RegularExpressions.Regex.Match(m.Value, "\"uuid\"\\s*:\\s*\"([^\"]*)\"");
                if (u.Success) r.Add(new[] { n.Success && n.Groups[1].Value != "" ? n.Groups[1].Value : "PC da TV", u.Groups[1].Value });
            }
        }
        catch { }
        return r;
    }

    public static bool Desconectar(string uuid)
    {
        try { return Api("POST", "{\"uuid\":\"" + uuid + "\"}", "/api/clients/unpair").Replace(" ", "").Contains("\"status\":true"); }
        catch { return false; }
    }

    static string Api(string metodo, string corpoJson, string caminho)
    {
        var req = (HttpWebRequest)WebRequest.Create("https://127.0.0.1:47990" + caminho);
        req.Method = metodo; req.Timeout = 5000;
        req.Headers["Authorization"] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(Usuario + ":" + Senha()));
        // o motor bloqueia pedidos sem origem conhecida (proteção CSRF)
        req.Headers["Origin"] = "https://localhost:47990"; req.Referer = "https://localhost:47990/pin";
        if (corpoJson != null)
        {
            req.ContentType = "application/json";
            var corpo = Encoding.UTF8.GetBytes(corpoJson);
            using (var s = req.GetRequestStream()) s.Write(corpo, 0, corpo.Length);
        }
        using (var resp = (HttpWebResponse)req.GetResponse())
        using (var r = new StreamReader(resp.GetResponseStream())) return r.ReadToEnd();
    }

    // o "código" que os dois lados calculam sozinhos a partir da identidade do motor (ninguém digita nada)
    static string codigoRede = "";
    static string CodigoDaRedeDaqui()
    {
        if (codigoRede != "") return codigoRede;
        var info = Util.InfoMotor("127.0.0.1", 3000);
        if (info != null && info[1] != "") codigoRede = Util.CodigoDaRede(info[1]);
        return codigoRede;
    }

    // confirma sozinho, sem código, os PCs da TV que pediram para conectar da rede de casa ou do seu Tailscale.
    // Pedidos vindos da internet aberta são ignorados. null = nenhum pedido; "" = conectou um; outro texto = aviso
    public static string ConfirmarPedidos()
    {
        try
        {
            // {"pairings":[{"address":"192.168.3.20","id":"...","name":"..."}]}
            var pedidos = System.Text.RegularExpressions.Regex.Matches(Api("GET", null), "\\{[^{}]*\"id\"[^{}]*\\}");
            if (pedidos.Count == 0) return null;
            string pin = CodigoDaRedeDaqui();
            if (pin == "") return null;
            bool algum = false;
            foreach (System.Text.RegularExpressions.Match m in pedidos)
            {
                var id = System.Text.RegularExpressions.Regex.Match(m.Value, "\"id\"\\s*:\\s*\"([^\"]+)\"");
                var end = System.Text.RegularExpressions.Regex.Match(m.Value, "\"address\"\\s*:\\s*\"([^\"]*)\"");
                if (!id.Success || !end.Success || !Util.Confiavel(end.Groups[1].Value)) continue;
                string ip = end.Groups[1].Value.Replace("::ffff:", "");
                string txt = Api("POST", "{\"pairing_id\":\"" + id.Groups[1].Value + "\",\"pin\":\"" + pin + "\",\"name\":\"PC da TV (" + ip + ")\"}");
                if (txt.Replace(" ", "").Contains("\"status\":true")) algum = true;
            }
            return algum ? "" : null;
        }
        catch (WebException e)
        {
            var hr = e.Response as HttpWebResponse;
            if (hr != null && hr.StatusCode == HttpStatusCode.Unauthorized) return "A senha interna do motor não bate. Clique em Reparar motor (lá embaixo).";
            return null;
        }
        catch { return null; }
    }

    public static string LiberarPin(string pin, string nome)
    {
        try
        {
            // pedidos de pareamento esperando: {"pairings":[{"address":..,"id":..,"name":..}]}
            var ids = System.Text.RegularExpressions.Regex.Matches(Api("GET", null), "\"id\"\\s*:\\s*\"([^\"]+)\"");
            if (ids.Count == 0) return "Nenhum PC da TV pediu pareamento. Clique em Parear no PC da TV primeiro.";
            // pode haver pedidos antigos esperando; tenta o PIN do mais novo para o mais velho
            for (int i = ids.Count - 1; i >= 0; i--)
            {
                string txt = Api("POST", "{\"pairing_id\":\"" + ids[i].Groups[1].Value + "\",\"pin\":\"" + pin + "\",\"name\":\"" + nome.Replace("\"", "") + "\"}");
                if (txt.Replace(" ", "").Contains("\"status\":true")) return "";
            }
            return "PIN errado. Confira o número que aparece na TV.";
        }
        catch (WebException e)
        {
            var hr = e.Response as HttpWebResponse;
            if (hr != null && hr.StatusCode == HttpStatusCode.Unauthorized) return "Senha interna do motor inválida. Clique em Reparar motor.";
            return "Não falou com o motor: " + e.Message;
        }
    }
}

// ---------------- qualidade ----------------
class Qualidade
{
    public string Chave, Nome;
    public Qualidade(string c, string n) { Chave = c; Nome = n; }
    public override string ToString() { return Nome; }

    public static readonly Qualidade[] Todas =
    {
        new Qualidade("auto", "✨ Automático (melhor possível)"),
        new Qualidade("2160x60", "4K 2160p 60fps"),
        new Qualidade("1440x120", "Quad HD 1440p 120fps"),
        new Qualidade("1440x60", "Quad HD 1440p 60fps"),
        new Qualidade("1080x120", "Full HD 1080p 120fps"),
        new Qualidade("1080x60", "Full HD 1080p 60fps"),
        new Qualidade("720x60", "720p 60fps (internet fraca)"),
    };

    public class Plano { public int Largura, Altura, Fps, Kbps; public string Aviso = ""; }

    public static Plano Resolver(string chave, bool fora, int hzTela)
    {
        var p = new Plano();
        bool aceita120 = hzTela >= 119;
        if (chave == "auto")
        {
            if (fora) chave = "1080x60";
            else if (aceita120) chave = "1440x120";
            else { chave = "1440x60"; p.Aviso = "A tela está em " + hzTela + " Hz, então o Automático usa 60 FPS."; }
        }
        p.Altura = int.Parse(chave.Split('x')[0]); p.Fps = int.Parse(chave.Split('x')[1]);
        p.Largura = p.Altura == 2160 ? 3840 : p.Altura == 1440 ? 2560 : p.Altura == 1080 ? 1920 : 1280;
        if (p.Fps == 120 && (fora || !aceita120))
        {
            p.Fps = 60;
            p.Aviso = fora ? "Fora de casa o TELAR trava em 60 FPS." : "A tela está em " + hzTela + " Hz. Usando 60 FPS. Coloque a TV em 120 Hz no Windows para liberar 120.";
        }
        if (fora) p.Kbps = p.Altura >= 2160 ? 20000 : p.Altura == 1440 ? 15000 : p.Altura == 1080 ? 12000 : 8000;
        else
        {
            var casa = new Dictionary<string, int> { { "2160x60", 80000 }, { "1440x120", 75000 }, { "1440x60", 45000 }, { "1080x120", 50000 }, { "1080x60", 30000 }, { "720x60", 15000 } };
            p.Kbps = casa[p.Altura + "x" + p.Fps];
        }
        return p;
    }
}

// ---------------- base visual ----------------
class Janela : Form
{
    protected readonly Font Normal = new Font("Segoe UI", 10f);
    public Janela(string titulo, int w, int h)
    {
        Text = titulo + "  ·  v" + App.Versao; BackColor = App.Fundo; ForeColor = App.Texto; Font = Normal;
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(w, h);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
    }
    // deixa a janela maximizar: o conteúdo vai para um painel que fica centralizado
    protected void PermitirMaximizar()
    {
        var painel = new Panel { Size = ClientSize, BackColor = BackColor };
        foreach (var c in Controls.Cast<Control>().ToArray()) painel.Controls.Add(c);
        Controls.Add(painel);
        FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = true; MinimumSize = Size;
        Resize += delegate { painel.Left = Math.Max(0, (ClientSize.Width - painel.Width) / 2); painel.Top = Math.Max(0, (ClientSize.Height - painel.Height) / 2); };
    }

    protected Label Rotulo(string t, int x, int y, int w, float tam, Color cor, bool negrito = false)
    {
        var l = new Label { Text = t, ForeColor = cor, AutoSize = false, Font = new Font(negrito ? "Segoe UI Semibold" : "Segoe UI", tam), Bounds = new Rectangle(x, y, w, (int)(tam * 2.2f) + 4) };
        Controls.Add(l); return l;
    }
    protected TextBox Caixa(string v, int x, int y, int w)
    {
        var t = new TextBox { Text = v, BackColor = App.Cartao, ForeColor = App.Texto, BorderStyle = BorderStyle.FixedSingle, Bounds = new Rectangle(x, y, w, 28) };
        Controls.Add(t); return t;
    }
    protected Button Botao(string t, int x, int y, int w, Color cor, int h = 40)
    {
        var b = new Button { Text = t, FlatStyle = FlatStyle.Flat, BackColor = cor, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 10.5f), Bounds = new Rectangle(x, y, w, h), Cursor = Cursors.Hand };
        b.FlatAppearance.BorderSize = 0; Controls.Add(b); return b;
    }
}

static class EscolherPapel
{
    public static string Perguntar()
    {
        string escolha = "";
        var f = new Janela("TELAR", 460, 250);
        var t = new Label { Text = "TELAR", Font = new Font("Segoe UI Semibold", 20f), ForeColor = App.Texto, Bounds = new Rectangle(24, 18, 400, 44) };
        var s = new Label { Text = "Qual PC é este?", ForeColor = App.Apagado, Bounds = new Rectangle(26, 66, 400, 24) };
        var b1 = new Button { Text = "PC Forte (sala)\nroda os jogos", FlatStyle = FlatStyle.Flat, BackColor = App.Casa, ForeColor = Color.White, Bounds = new Rectangle(24, 104, 200, 110), Font = new Font("Segoe UI Semibold", 11f) };
        var b2 = new Button { Text = "PC da TV\nonde você joga", FlatStyle = FlatStyle.Flat, BackColor = App.Cinza, ForeColor = Color.White, Bounds = new Rectangle(236, 104, 200, 110), Font = new Font("Segoe UI Semibold", 11f) };
        b1.FlatAppearance.BorderSize = 0; b2.FlatAppearance.BorderSize = 0;
        b1.Click += delegate { escolha = "forte"; f.Close(); };
        b2.Click += delegate { escolha = "tv"; f.Close(); };
        f.Controls.AddRange(new Control[] { t, s, b1, b2 });
        f.ShowDialog();
        return escolha;
    }
}

// ---------------- PC Forte (estilo AnyDesk) ----------------
// "Este dispositivo" mostra o nome deste PC. Não existe código: o PC da TV da mesma rede (ou do seu Tailscale)
// conecta sozinho. Fechar a janela deixa o TELAR ligado perto do relógio, senão a TV não consegue conectar.
class TelaForte : Casca
{
    public const string SinalMostrar = "TELAR-PC-Forte-Mostrar";
    readonly Config cfg;
    readonly System.Windows.Forms.Timer vigia = new System.Windows.Forms.Timer { Interval = 1000 };
    readonly System.Windows.Forms.Timer relogioStatus = new System.Windows.Forms.Timer { Interval = 15000 };
    bool motorOk, vigiando, reparando, saindo, podeMostrar, atualizando;
    List<string[]> aparelhos = new List<string[]>();
    Dictionary<string, bool> aparelhoLigado = new Dictionary<string, bool>();
    string ipTs = "", ipCasa = "";
    bool driverControle;
    readonly NotifyIcon bandeja;

    public TelaForte(Config c, bool minimizado) : base("TELAR · PC Forte")
    {
        cfg = c;
        podeMostrar = !minimizado;
        Instalacao.IniciarComWindows();
        Heroi("Este dispositivo", Environment.MachineName);
        BotaoHeroi("", "No PC da TV, abra o TELAR e clique neste PC. Conecta sozinho, sem código.").Click += delegate { Ajuda(); };
        MarcadorEndereco("Este é o PC Forte. Para jogar, abra o TELAR no PC da TV e clique neste PC.");
        tEndereco.ReadOnly = true; bConectar.Enabled = false;
        Aviso("Pronto. No PC da TV, abra o TELAR e clique neste PC: conecta sozinho, sem código.", Cores.Laranja);

        AbaMudou += delegate { Redesenhar(); };
        AbrirMenu += delegate { MostrarMenu(); };
        DefinirAbas("Status", "Aparelhos conectados");

        // perto do relógio: o TELAR continua confirmando o PC da TV mesmo com a janela fechada
        bandeja = new NotifyIcon { Text = "TELAR · PC Forte ligado", Icon = Icon ?? SystemIcons.Application, Visible = true };
        var mb = new ContextMenuStrip();
        mb.Items.Add("Abrir o TELAR", null, delegate { MostrarJanela(); });
        mb.Items.Add("Encerrar a transmissão agora", null, delegate { Encerrar(); });
        mb.Items.Add(new ToolStripSeparator());
        mb.Items.Add("Sair do TELAR", null, delegate { Sair(); });
        bandeja.ContextMenuStrip = mb;
        bandeja.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) MostrarJanela(); };

        FormClosing += (s, e) =>
        {
            if (saindo || e.CloseReason != CloseReason.UserClosing) return;
            e.Cancel = true; Esconder();
        };
        FormClosed += delegate { vigia.Stop(); relogioStatus.Stop(); bandeja.Visible = false; bandeja.Dispose(); };
        vigia.Tick += delegate { Vigiar(); };
        relogioStatus.Tick += delegate { if (Visible) Atualizar(); };

        // começa logo que o programa roda, com a janela aberta ou escondida
        var inicio = new System.Windows.Forms.Timer { Interval = 300 };
        inicio.Tick += delegate
        {
            inicio.Stop(); inicio.Dispose();
            Atualizar(); vigia.Start(); relogioStatus.Start();
            AjustarMotorSozinho();
        };
        inicio.Start();
        EscutarOutraJanela();
    }

    // aberto pelo Windows (--minimizado): fica só perto do relógio, sem janela e sem botão na barra de tarefas
    protected override void SetVisibleCore(bool value)
    {
        if (!podeMostrar) { value = false; if (!IsHandleCreated) CreateHandle(); }
        base.SetVisibleCore(value);
    }

    void MostrarJanela()
    {
        podeMostrar = true;
        ShowInTaskbar = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate(); BringToFront();
        Atualizar();
    }

    void Esconder()
    {
        Hide();
        if (cfg.Get("forte.avisouRelogio") != "1")
        {
            bandeja.ShowBalloonTip(6000, "O TELAR continua ligado", "Ele fica aqui perto do relógio para o PC da TV conseguir conectar. Para fechar de vez: botão direito no ícone → Sair do TELAR.", ToolTipIcon.Info);
            cfg.Set("forte.avisouRelogio", "1"); cfg.Salvar();
        }
    }

    void Sair()
    {
        if (MessageBox.Show(this, "Fechar o TELAR de vez?\n\nSem ele aberto, o PC da TV não consegue conectar a este PC. Ele abre sozinho de novo quando o Windows ligar.", "TELAR", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        saindo = true; Close();
    }

    // outro TELAR aberto pelo atalho pede para mostrar esta janela
    void EscutarOutraJanela()
    {
        var t = new Thread(() =>
        {
            try
            {
                using (var ev = new EventWaitHandle(false, EventResetMode.AutoReset, SinalMostrar))
                    while (!IsDisposed)
                        if (ev.WaitOne(1000) && !IsDisposed && IsHandleCreated) BeginInvoke((Action)MostrarJanela);
            }
            catch { }
        }) { IsBackground = true };
        t.Start();
    }

    void MostrarMenu()
    {
        var m = new ContextMenuStrip();
        m.Items.Add("Encerrar a transmissão agora", null, delegate { Encerrar(); });
        m.Items.Add("Atualizar status", null, delegate { Atualizar(); });
        m.Items.Add("Reparar motor (só se der problema)", null, delegate { Preparar(); });
        m.Items.Add("Como usar", null, delegate { Ajuda(); });
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Sair do TELAR", null, delegate { Sair(); });
        m.Show(bMenu, new Point(bMenu.Width - m.Width, bMenu.Height));
    }

    void Ajuda()
    {
        MessageBox.Show(this,
            "Este é o PC Forte: ele roda os jogos e transmite a tela inteira.\n\n" +
            "No PC da TV, abra o TELAR e clique neste PC. Na mesma rede (ou pelo Tailscale) conecta sozinho, sem código.\n\n" +
            "Deixe o TELAR ligado aqui: ele fica perto do relógio e abre sozinho quando o Windows liga.\n\n" +
            "Encerrar a transmissão: pelo menu ☰ ou pelo ícone perto do relógio. A TV continua conectada para jogar de novo depois.",
            "Como usar o TELAR");
    }

    void Encerrar()
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            bool ok = Motor.EncerrarTransmissao();
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)(() => Aviso(ok ? "Transmissão encerrada. O PC da TV continua conectado e pode jogar de novo quando quiser." : "Não havia transmissão para encerrar.", Cores.Laranja))); } catch { }
        });
    }

    // na primeira vez depois de uma atualização do TELAR, ajusta o motor sozinho (sem pedir administrador)
    void AjustarMotorSozinho()
    {
        if (!motorOk) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            string r = null;
            try { r = Motor.AtualizarAjustes(); } catch (Exception e) { App.AnotarErro(e); }
            if (r == null || IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)(() => { Aviso(r, Cores.Verde); Atualizar(); })); } catch { }
        });
    }

    void Atualizar()
    {
        motorOk = Motor.Instalado && Motor.Configurado && Motor.Ligado;
        Status(motorOk);
        if (atualizando) return;
        atualizando = true;
        // o que é lento (Tailscale, driver, lista do motor, quem está ligado) roda fora da janela, para ela nunca travar
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var lista = motorOk ? Motor.ListarAparelhos() : new List<string[]>();
                string ts = Util.IpTailscale(), casa = Util.IpLocal();
                bool controle = Util.Rodar("sc", "query ViGEmBus", 5000).Contains("ViGEmBus");
                var ligados = new Dictionary<string, bool>();
                foreach (var a in lista) { string ip = IpDoNome(a[0]); if (ip != "") ligados[a[1]] = Util.Ligado(ip); }
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)(() => { aparelhos = lista; aparelhoLigado = ligados; ipTs = ts; ipCasa = casa; driverControle = controle; atualizando = false; Redesenhar(); }));
            }
            catch (Exception e) { atualizando = false; App.AnotarErro(e); }
        });
        Redesenhar();
    }

    // o nome do aparelho guarda o endereço dele: "PC da TV (192.168.3.20)"
    static string IpDoNome(string nome)
    {
        var m = System.Text.RegularExpressions.Regex.Match(nome ?? "", @"\((\d+\.\d+\.\d+\.\d+)\)");
        return m.Success ? m.Groups[1].Value : "";
    }

    void Redesenhar()
    {
        if (!cartoes.Visible || IsDisposed) return;
        cartoes.SuspendLayout();
        LimparCartoes();
        if (abaAtual == 0)
        {
            string motor = !Motor.Instalado ? "Não instalado" : !Motor.Configurado ? "Falta configurar" : Motor.Ligado ? "Ligado e pronto" : "Desligado";
            var cm = NovoCartao("Motor de transmissão", motor, motorOk ? "Tela, áudio e controles" : "Use Reparar motor", motorOk ? Cores.Verde : Cores.Vermelho,
                motorOk ? Cartao.Desenho.Ok : Cartao.Desenho.Desligado);
            cm.Selo = motorOk ? "Online" : "Offline"; cm.CorSelo = motorOk ? Cores.Verde : Color.FromArgb(150, 150, 150);
            cm.Click += delegate { Atualizar(); };
            NovoCartao("Endereço em casa", ipCasa == "" ? "..." : ipCasa, "Conecta sozinho, sem código", Cores.Cartoes[3], Cartao.Desenho.Rede);
            NovoCartao("Fora de casa", ipTs != "" ? ipTs : "opcional: Tailscale", "Trabalho remoto · sem código", Cores.Cartoes[4], Cartao.Desenho.Rede);
            NovoCartao("Controle", driverControle ? "Driver instalado" : "Conferindo...", "Xbox e PlayStation na TV", Cores.Cartoes[1], Cartao.Desenho.Controle);
            int online = aparelhos.Count(a => aparelhoLigado.ContainsKey(a[1]) && aparelhoLigado[a[1]]);
            NovoCartao("Aparelhos conectados", aparelhos.Count == 0 ? "nenhum ainda" : aparelhos.Count + (aparelhos.Count == 1 ? " PC da TV" : " PCs da TV") + (online > 0 ? " · " + online + " online" : ""), "Ver, encerrar ou esquecer", Cores.Cartoes[0], Cartao.Desenho.Pc)
                .Click += delegate { EscolherAba(1); };
            NovoCartao("Reparar motor", reparando ? "Reparando..." : "Só se der problema", "Pede administrador", Color.FromArgb(78, 78, 78), Cartao.Desenho.Engrenagem)
                .Click += delegate { Preparar(); };
        }
        else
        {
            foreach (var a in aparelhos)
            {
                var ap = a;
                bool sabe = aparelhoLigado.ContainsKey(ap[1]), ligado = sabe && aparelhoLigado[ap[1]];
                var c = NovoCartao(ap[0], ligado ? "Online agora" : sabe ? "Offline (desligado ou fora da rede)" : "Conectado a este PC", "Clique para encerrar ou esquecer", Cores.DoNome(ap[1]), Cartao.Desenho.Pc);
                c.Selo = ligado ? "Online" : sabe ? "Offline" : "Pareado"; c.CorSelo = ligado ? Cores.Verde : sabe ? Color.FromArgb(150, 150, 150) : Cores.Laranja;
                var menu = new ContextMenuStrip();
                menu.Items.Add("Encerrar a transmissão agora", null, delegate { Encerrar(); });
                menu.Items.Add("Esquecer este aparelho", null, delegate
                {
                    if (MessageBox.Show(this, "Esquecer \"" + ap[0] + "\"?\n\nSe ele estiver jogando, a transmissão cai. Para voltar, é só clicar neste PC no TELAR da TV: ele conecta sozinho de novo, sem código.", "TELAR", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    Aviso(Motor.Desconectar(ap[1]) ? "\"" + ap[0] + "\" foi esquecido. Ele conecta de novo sozinho quando clicar neste PC." : "Não consegui. Tente Reparar motor.", Cores.Laranja);
                    Atualizar();
                });
                c.ContextMenuStrip = menu;
                c.Click += delegate { menu.Show(Cursor.Position); };
            }
            if (aparelhos.Count == 0) TextoVazio("Nenhum PC da TV conectado ainda. No PC da TV, abra o TELAR e clique neste PC: conecta sozinho, sem código.");
        }
        cartoes.ResumeLayout();
    }

    void Log(string m) { Aviso(m.Trim(), Cores.Laranja); Application.DoEvents(); }

    void Preparar()
    {
        if (reparando) return;
        reparando = true; Redesenhar();
        Motor.Reparar(Log);
        reparando = false;
        Atualizar();
        Aviso(motorOk ? "Motor reparado e ligado." : "O motor não ligou. Rode o TELAR-Setup.exe de novo.", motorOk ? Cores.Verde : Cores.Vermelho);
    }

    // a cada segundo confere se algum PC da TV pediu para conectar e confirma sozinho (rede de casa ou Tailscale)
    void Vigiar()
    {
        if (!motorOk || vigiando) return;
        vigiando = true;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            string res = null;
            try { res = Motor.ConfirmarPedidos(); } catch (Exception e) { App.AnotarErro(e); }
            if (IsDisposed || !IsHandleCreated) { vigiando = false; return; }
            try
            {
                BeginInvoke((Action)(() =>
                {
                    vigiando = false;
                    if (res == null) return; // ninguém pediu
                    if (res == "") { Aviso("PC da TV conectado às " + DateTime.Now.ToString("HH:mm") + ". Ele já pode jogar.", Cores.Verde); Atualizar(); }
                    else Aviso(res, Cores.Vermelho);
                }));
            }
            catch { vigiando = false; }
        });
    }
}
