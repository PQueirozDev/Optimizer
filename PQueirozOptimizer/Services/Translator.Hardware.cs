namespace PQueirozOptimizer.Services;

/// <summary>Traduções das páginas de hardware: Saúde do hardware, Dispositivos, Stress test, Biblioteca e Overlay.</summary>
public static partial class Translator
{
    // Os textos dinâmicos dessas páginas já são montados no idioma ativo (L(pt, en)); aqui ficam os fixos do menu e do cabeçalho
    private static readonly Dictionary<string, string> HardwareExact = new(StringComparer.Ordinal)
    {
        ["Biblioteca"] = "Library", ["Overlay"] = "Overlay", ["Stress test"] = "Stress test", ["Relatório do PC"] = "PC report",
        ["Saúde do hardware"] = "Hardware health", ["Dispositivos"] = "Devices",
        ["Todos os jogos instalados, de qualquer loja, num só lugar."] = "Every installed game, from any store, in one place.",
        ["FPS, tempo de quadro e uso do hardware por cima do jogo."] = "FPS, frame time and hardware usage on top of the game.",
        ["Teste de estabilidade de CPU e memória com conferência de erros."] = "CPU and memory stability test with error checking.",
        ["Discos, bateria e erros de hardware lidos do Windows."] = "Disks, battery and hardware errors read from Windows.",
        ["Dispositivos fantasmas, drivers com problema e portas COM."] = "Ghost devices, driver problems and COM ports.",
        ["Procurar jogos de novo"] = "Scan for games again",
        // ---------- Visual Aurora (barra superior e Início) ----------
        ["Otimizar"] = "Optimize", ["Jogos"] = "Games", ["Conta e configurações"] = "Account and settings", ["Tema"] = "Theme",
        ["Temperaturas"] = "Temperatures", ["Discos"] = "Disks", ["Estabilidade"] = "Stability", ["Drivers"] = "Drivers",
        ["Lendo…"] = "Reading…", ["Sem sensor"] = "No sensor", ["Com problema"] = "Problem", ["Atenção"] = "Warning", ["Sem dados"] = "No data",
        ["Saudáveis"] = "Healthy", ["Sem erros"] = "No errors", ["Em dia"] = "Up to date", ["Normal"] = "Normal", ["Ver detalhes"] = "See details",
        ["Analisar o PC"] = "Analyze the PC", ["Revisar {0} ajustes"] = "Review {0} tweaks", ["Ver meu PC"] = "See my PC", ["SAÚDE"] = "HEALTH",
        ["DISCO DO SISTEMA"] = "SYSTEM DRIVE", ["% livre"] = "% free", ["Carga no último minuto"] = "Load in the last minute",
        ["Carga nos últimos {0} minutos"] = "Load in the last {0} minutes", ["CPU e memória com conferência de erros"] = "CPU and memory with error checking",
        ["Aurora"] = "Aurora", ["Desempenho"] = "Performance", ["Ferramentas"] = "Tools", ["Sua conta"] = "Your account", ["Páginas da categoria"] = "Category pages",
        ["Otimização inteligente, ajustes revisados item por item, serviços, aplicativos, inicialização e drivers. Você revisa tudo antes de aplicar."] = "Smart Optimize, tweaks reviewed one by one, services, apps, startup and drivers. You review everything before applying.",
        ["Laboratório de FPS, stress test, overlay no jogo e rede."] = "FPS lab, stress test, in-game overlay and network.",
        ["Modo Jogo, biblioteca com todos os seus jogos e perfis por jogo."] = "Game Mode, a library with all your games and per-game profiles.",
        ["Diagnóstico, saúde do hardware, dispositivos, BIOS, correções e pontos de restauração."] = "Diagnostics, hardware health, devices, BIOS, fixes and restore points.",
        ["Cada categoria mostra as páginas dela aqui, logo abaixo das abas."] = "Each category shows its pages here, right below the tabs.",
        ["Configurações, licença, atualizações, tema e o modo compacto ficam neste menu."] = "Settings, license, updates, theme and compact mode are in this menu.", ["Novo visual do Qrztweaks"] = "New Qrztweaks look",
        ["O tema Aurora (preto com azul) foi feito para o novo design. Seu tema atual continua disponível em Configurações → Aparência."] = "The Aurora theme (black and blue) was made for the new design. Your current theme is still available in Settings → Appearance.", ["Abrir a pasta do jogo"] = "Open the game folder", ["Tirar da biblioteca"] = "Remove from library",
    };
}
