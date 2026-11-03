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
    public const int Versao = 2;
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
        if (Atualizacao.Verificar()) return 0;

        var cfg = Config.Carregar();
        if (cfg.Get("papel") == "") cfg.Set("papel", EscolherPapel.Perguntar());
        if (cfg.Get("papel") == "") return 0;
        cfg.Salvar();

        if (cfg.Get("papel") == "forte") Application.Run(new TelaForte(cfg));
        else Application.Run(new TelaTV(cfg));
        return 0;
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
        }
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
    // version.json: {"version": 2, "exe": "telar.exe"}
    // devolve true quando baixou a versão nova e já abriu ela (este processo deve sair)
    public static bool Verificar()
    {
        string atual = Application.ExecutablePath;
        try { if (File.Exists(atual + ".old")) File.Delete(atual + ".old"); } catch { }
        try
        {
            var wc = new WebClient();
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
            Process.Start(atual);
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
    // acha PCs Fortes com o TELAR ligado: varre a rede de casa (/24) e os aparelhos do Tailscale na porta do motor
    // devolve pares "ip|nome"
    public static List<string> ProcurarPCsForte()
    {
        var alvos = new List<string>();
        string meu = IpLocal();
        if (meu != "") { string b = meu.Substring(0, meu.LastIndexOf('.') + 1); for (int i = 1; i < 255; i++) alvos.Add(b + i); }
        if (File.Exists(App.TailscaleExe))
            foreach (var l in Rodar(App.TailscaleExe, "status", 5000).Split('\n'))
            {
                var m = System.Text.RegularExpressions.Regex.Match(l, @"^\s*(100\.\d+\.\d+\.\d+)\s");
                if (m.Success) alvos.Add(m.Groups[1].Value);
            }
        var achados = new List<string>();
        var tarefas = alvos.Distinct().Select(ip => System.Threading.Tasks.Task.Factory.StartNew(() =>
        {
            using (var c = new TcpClient())
            {
                try
                {
                    var r = c.BeginConnect(ip, 47989, null, null);
                    if (!r.AsyncWaitHandle.WaitOne(600) || !c.Connected) return;
                    c.EndConnect(r);
                }
                catch { return; }
            }
            try
            {
                var wc = new WebClient(); wc.Encoding = Encoding.UTF8;
                string xml = wc.DownloadString("http://" + ip + ":47989/serverinfo");
                var n = System.Text.RegularExpressions.Regex.Match(xml, "<hostname>([^<]*)</hostname>");
                lock (achados) achados.Add(ip + "|" + (n.Success ? n.Groups[1].Value : ip));
            }
            catch { }
        }, System.Threading.Tasks.TaskCreationOptions.LongRunning)).ToArray();
        System.Threading.Tasks.Task.WaitAll(tarefas, 8000);
        lock (achados) return achados.OrderBy(a => a.StartsWith("100.") ? 1 : 0).ToList();
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
        return new ProcessStartInfo(App.ClienteExe, args)
        {
            UseShellExecute = false, CreateNoWindow = oculto, WindowStyle = oculto ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
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
}

// ---------------- motor (Sunshine no PC Forte) ----------------
static class Motor
{
    public const string Usuario = "telar";
    static string ArqSenha { get { return Path.Combine(App.Pasta, "motor.dat"); } }

    public static bool Instalado { get { return File.Exists(App.MotorExe); } }
    public static bool Configurado { get { return File.Exists(ArqSenha); } }
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
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(ArqSenha), null, DataProtectionScope.CurrentUser)); }
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
        var resumo = new List<string>();
        try
        {
            var bytes = new byte[18]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(bytes);
            string senha = Convert.ToBase64String(bytes).Replace('+', 'x').Replace('/', 'y').Replace("=", "");

            string gpus = "";
            using (var q = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                foreach (ManagementObject o in q.Get()) gpus += o["Name"] + "; ";
            var ajustes = new List<KeyValuePair<string, string>>();
            Action<string, string> A = (k, v) => ajustes.Add(new KeyValuePair<string, string>(k, v));
            if (gpus.Contains("NVIDIA")) { A("encoder", "nvenc"); A("nvenc_preset", "1"); A("nvenc_twopass", "quarter_res"); resumo.Add("Placa: NVIDIA (NVENC, latência mínima)"); }
            else if (gpus.Contains("Radeon") || gpus.Contains("AMD")) { A("encoder", "amdvce"); A("amd_usage", "ultralowlatency"); A("amd_rc", "cbr"); A("amd_quality", "speed"); A("amd_vbaq", "enabled"); resumo.Add("Placa: AMD (AMF, ultra baixa latência)"); }
            else { A("encoder", "quicksync"); A("qsv_preset", "veryfast"); resumo.Add("Placa: Intel (QuickSync)"); }
            A("sunshine_name", "TELAR " + Environment.MachineName);
            A("system_tray", "disabled");
            A("origin_web_ui_allowed", "pc");

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
        File.WriteAllBytes(ArqSenha, ProtectedData.Protect(Encoding.UTF8.GetBytes(senha), null, DataProtectionScope.CurrentUser));
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
    static string Api(string metodo, string corpoJson)
    {
        var req = (HttpWebRequest)WebRequest.Create("https://127.0.0.1:47990/api/pin");
        req.Method = metodo; req.Timeout = 15000;
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

// ---------------- PC Forte ----------------
class TelaForte : Janela
{
    readonly Config cfg;
    readonly Label lMotor, lIpCasa, lIpFora, lPar;
    readonly TextBox tPin, tNome, log;
    readonly Button bPreparar, bLiberar;

    public TelaForte(Config c) : base("TELAR · PC Forte", 520, 560)
    {
        cfg = c;
        Rotulo("TELAR", 24, 16, 300, 20f, App.Texto, true);
        Rotulo("PC Forte · roda os jogos e transmite para a TV", 26, 62, 460, 9.5f, App.Apagado);

        Rotulo("Motor de transmissão", 26, 100, 220, 9f, App.Apagado);
        lMotor = Rotulo("", 26, 120, 470, 11f, App.Texto, true);
        Rotulo("IP em casa", 26, 160, 220, 9f, App.Apagado);
        lIpCasa = Rotulo("", 26, 180, 220, 12f, App.Texto, true);
        Rotulo("IP fora de casa (Tailscale)", 270, 160, 230, 9f, App.Apagado);
        lIpFora = Rotulo("", 270, 180, 230, 12f, App.Texto, true);
        Rotulo("Digite esses IPs no TELAR do PC da TV.", 26, 214, 470, 9f, App.Apagado);

        bPreparar = Botao("Preparar motor", 26, 246, 468, App.Cinza);
        bPreparar.Click += delegate { Preparar(); };

        Rotulo("Liberar um PC da TV", 26, 304, 400, 12f, App.Texto, true);
        Rotulo("PIN que aparece na TV", 26, 336, 200, 9f, App.Apagado);
        Rotulo("Nome do aparelho", 250, 336, 200, 9f, App.Apagado);
        tPin = Caixa("", 26, 358, 210); tPin.Font = new Font("Segoe UI Semibold", 14f); tPin.MaxLength = 4;
        tNome = Caixa("PC da TV", 250, 358, 244); tNome.Font = new Font("Segoe UI", 14f);
        bLiberar = Botao("Liberar", 26, 404, 468, App.Casa);
        bLiberar.Click += delegate { Liberar(); };
        lPar = Rotulo("", 26, 450, 468, 9.5f, App.Apagado);

        log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = App.Cartao, ForeColor = App.Apagado, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9f), Bounds = new Rectangle(26, 478, 468, 66) };
        Controls.Add(log);
        AcceptButton = bLiberar;
        Shown += delegate { Atualizar(); };
    }

    void Log(string m) { log.AppendText(m + "\r\n"); Application.DoEvents(); }

    void Atualizar()
    {
        bool ok = Motor.Instalado && Motor.Configurado && Motor.Ligado;
        lMotor.Text = !Motor.Instalado ? "Não instalado · rode o TELAR-Setup.exe" : !Motor.Configurado ? "Instalado, falta configurar · clique em Reparar motor" : Motor.Ligado ? "Ligado e pronto" : "Desligado · clique em Reparar motor";
        lMotor.ForeColor = ok ? App.Ok : App.Ruim;
        bPreparar.Text = "Reparar motor";
        lIpCasa.Text = Util.IpLocal(); if (lIpCasa.Text == "") lIpCasa.Text = "sem rede";
        lIpFora.Text = Util.IpTailscale(); if (lIpFora.Text == "") lIpFora.Text = File.Exists(App.TailscaleExe) ? "entre no Tailscale" : "opcional: Tailscale";
        bLiberar.Enabled = ok;
    }

    void Preparar()
    {
        bPreparar.Enabled = false;
        Motor.Reparar(Log);
        bPreparar.Enabled = true;
        Atualizar();
    }

    void Liberar()
    {
        string pin = tPin.Text.Trim();
        if (pin.Length != 4 || !pin.All(char.IsDigit)) { lPar.ForeColor = App.Ruim; lPar.Text = "O PIN tem 4 números. Copie o que aparece na TV."; return; }
        lPar.ForeColor = App.Apagado; lPar.Text = "Liberando..."; Application.DoEvents();
        string erro = Motor.LiberarPin(pin, tNome.Text.Trim() == "" ? "PC da TV" : tNome.Text.Trim());
        lPar.ForeColor = erro == "" ? App.Ok : App.Ruim;
        lPar.Text = erro == "" ? "Liberado. O PC da TV já pode jogar." : erro;
        if (erro == "") tPin.Text = "";
    }
}
