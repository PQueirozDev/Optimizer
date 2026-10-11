using System.Text.RegularExpressions;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Traduções do redesign (barra lateral por categorias, Início, Meu PC, nomes em português das páginas). Consultadas
/// antes dos outros dicionários: os nomes novos valem também onde um texto antigo tinha outra tradução.
/// </summary>
public static partial class Translator
{
    private static readonly Dictionary<string, string> RedesignExact = new(StringComparer.Ordinal)
    {
        // ---------- Barra lateral, páginas e cabeçalho ----------
        ["PRINCIPAL"] = "MAIN", ["OTIMIZAÇÃO"] = "OPTIMIZATION", ["DESEMPENHO"] = "PERFORMANCE", ["FERRAMENTAS"] = "TOOLS", ["SISTEMA"] = "SYSTEM",
        ["Principal"] = "Main", ["Otimização"] = "Optimization", ["Desempenho"] = "Performance", ["Ferramentas"] = "Tools", ["Sistema"] = "System",
        ["Início"] = "Home", ["Meu PC"] = "My PC", ["Otimização inteligente"] = "Smart Optimize", ["Otimizações"] = "Optimizations", ["Aplicativos"] = "Apps",
        ["Imagens do Windows"] = "Windows images", ["Laboratório"] = "Lab", ["Laboratório de desempenho"] = "Performance Lab", ["Laboratório de otimização"] = "Optimization Lab",
        ["Diagnóstico"] = "Diagnostics", ["Assistente de BIOS"] = "BIOS Advisor", ["ASSISTENTE DE BIOS"] = "BIOS ADVISOR", ["Editor de BIOS"] = "BIOS editor",
        ["Restauração"] = "Restore", ["Utilitários"] = "Utilities", ["Histórico"] = "History", ["Licença"] = "License", ["Atualizações"] = "Updates", ["Novidades"] = "What's new",
        ["Drivers e utilitários"] = "Drivers & utilities", ["Monitor ao vivo"] = "Live monitor",
        ["Visão geral do desempenho e da saúde do seu computador."] = "Overview of your computer's performance and health.",
        ["Hardware, sistema e estado do seu computador, lidos agora."] = "Your computer's hardware, system and state, read right now.",
        ["FPS, tempo de quadro e sensores reais, com histórico e comparação antes e depois."] = "Real FPS, frame time and sensors, with history and before/after comparison.",
        ["Ajuste Discord, navegadores e outros aplicativos e remova o que não usa."] = "Tune Discord, browsers and other apps and remove what you don't use.",
        ["Aparência, licença, idioma, tutoriais e atualizações."] = "Appearance, license, language, tutorials and updates.",
        ["Remoção de aplicativos e privacidade"] = "Debloat & privacy", ["Modo remoção de aplicativos e privacidade"] = "Debloat & Privacy Mode",
        ["Remoção automática"] = "Automatic debloat",
        ["Tema"] = "Theme", ["Tema: {0}"] = "Theme: {0}", ["Mais opções de aparência"] = "More appearance options",
        ["Azul meia-noite"] = "Midnight Blue", ["Roxo meia-noite"] = "Midnight Purple", ["Azul (padrão)"] = "Blue (default)", ["Roxo"] = "Purple", ["Azul-céu (padrão)"] = "Sky blue (default)",
        ["Modo demonstração"] = "Demo mode", ["Sem licença ativa"] = "No active license",

        // ---------- Barra superior ----------
        ["Otimizar"] = "Optimize", ["Buscar..."] = "Search...", ["Alertas"] = "Alerts", ["1 alerta"] = "1 alert", ["{0} alertas"] = "{0} alerts",
        ["Nenhum alerta. Tudo certo por aqui."] = "No alerts. All good here.", ["Não foi possível ler o sistema agora."] = "Couldn't read the system right now.",
        ["Analisar o PC e revisar os ajustes recomendados"] = "Analyze the PC and review the recommended tweaks",
        ["Ao vivo"] = "Live", ["CPU, placa de vídeo, memória e rede lidas a cada segundo"] = "CPU, graphics card, memory and network read every second",
        ["Atualizar"] = "Refresh", ["Ler o sistema, as unidades e os indicadores de novo"] = "Read the system, drives and indicators again",

        // ---------- Início ----------
        ["Processador"] = "Processor", ["Ver detalhes em Meu PC"] = "See details in My PC", ["Uso"] = "Usage", ["Indisponível"] = "Unavailable",
        ["A temperatura da CPU exige um driver de kernel que o Qrztweaks não instala, por segurança."] = "CPU temperature requires a kernel driver that Qrztweaks doesn't install, for safety.",
        ["Núcleos / threads"] = "Cores / threads", ["Frequência"] = "Frequency", ["Processos"] = "Processes", ["Carga média (1 min)"] = "Average load (1 min)",
        ["ATIVIDADE AO VIVO"] = "LIVE ACTIVITY", ["livre"] = "free", ["livres"] = "free", ["(base)"] = "(base)",
        ["Memória"] = "Memory", ["Em uso"] = "In use", ["Disponível"] = "Available", ["Velocidade"] = "Speed", ["Pico (1 min)"] = "Peak (1 min)",
        ["Placa de vídeo"] = "Graphics card", ["Uso (motor 3D)"] = "Usage (3D engine)", ["Memória de vídeo"] = "Video memory", ["Temperatura"] = "Temperature",
        ["Uso indisponível neste PC"] = "Usage unavailable on this PC", ["Temperatura da GPU lida pelo driver NVIDIA."] = "GPU temperature read by the NVIDIA driver.",
        ["O driver desta placa não informa a temperatura sem um driver extra."] = "This card's driver doesn't report the temperature without an extra driver.",
        ["em uso"] = "in use", ["Bem-vindo de volta"] = "Welcome back", ["ligado há"] = "up for",
        ["Temperaturas"] = "Temperatures", ["Saúde"] = "Health", ["Estabilidade"] = "Stability", ["Drivers"] = "Drivers", ["Verificando..."] = "Checking...",
        ["Nenhuma análise ainda"] = "No analysis yet", ["Última análise"] = "Last analysis", ["Ver meu PC"] = "See my PC", ["Modo Jogo"] = "Game Mode",
        ["Armazenamento"] = "Storage", ["Nenhuma unidade"] = "No drives", ["1 unidade"] = "1 drive", ["livres de"] = "free of",
        ["Sem unidades fixas para mostrar"] = "No fixed drives to show", ["Espaço suficiente em todas as unidades"] = "Enough space on every drive",
        ["Rede"] = "Network", ["Conectado"] = "Connected", ["Sem conexão"] = "Offline", ["Nenhum adaptador conectado"] = "No adapter connected",
        ["Download"] = "Download", ["Upload"] = "Upload", ["Latência"] = "Latency", ["ATIVIDADE DA REDE"] = "NETWORK ACTIVITY", ["sem ping"] = "no ping",
        ["Atividade"] = "Activity", ["Atividade recente"] = "Recent activity", ["Ver todo o histórico"] = "See the full history",
        ["Suas próximas execuções aparecerão aqui."] = "Your next runs will show up here.",
        ["Acesso rápido"] = "Quick access", ["Os recursos mais usados, com o estado de cada um."] = "The most used features, with the state of each one.",
        ["Analisador de desempenho"] = "Performance Analyzer", ["Ferramenta de análise de FPS"] = "FPS analysis tool", ["Nenhum teste ainda"] = "No test yet", ["Último teste"] = "Last test", ["Analisar"] = "Analyze",
        ["Ajustes recomendados em um clique"] = "Recommended tweaks in one click", ["Ainda não analisado"] = "Not analyzed yet",
        ["Central de saúde"] = "Health Center", ["Diagnóstico com evidências"] = "Diagnostics with evidence", ["Diagnosticar"] = "Diagnose",
        ["Central de jogos"] = "Gaming Center", ["Modo Jogo, perfis e presets"] = "Game Mode, profiles and presets", ["Modo Jogo desligado"] = "Game Mode off",
        ["Restauração pendente"] = "Restore pending", ["Modo Jogo ativo"] = "Game Mode active", ["Configurar"] = "Configure",
        ["Configurações"] = "Settings", ["Configurações rápidas"] = "Quick settings", ["Todas as configurações"] = "All settings",
        ["PREFERÊNCIAS GERAIS"] = "GENERAL PREFERENCES", ["MONITORAMENTO"] = "MONITORING",
        ["Animações da interface"] = "Interface animations", ["Entrada das páginas e transições curtas. Os números ao vivo continuam atualizando."] = "Page entrances and short transitions. Live numbers keep updating.",
        ["Barra lateral compacta"] = "Compact sidebar", ["Só os ícones; o nome aparece ao passar o mouse (Ctrl+B)."] = "Icons only; the name shows on hover (Ctrl+B).",
        ["Seguir a redução de movimento do Windows"] = "Follow Windows reduced motion",
        ["Desliga as animações quando \"Mostrar animações no Windows\" estiver desligado."] = "Turns animations off when \"Show animations in Windows\" is off.",
        ["Gráficos ao vivo no Início"] = "Live charts on Home", ["Curvas de CPU, placa de vídeo, memória e rede atualizadas a cada segundo."] = "CPU, graphics card, memory and network curves updated every second.",
        ["Perfis automáticos ao abrir jogos"] = "Automatic profiles when games open", ["Ativa o perfil do jogo quando ele abre e restaura ao fechar."] = "Activates the game's profile when it opens and restores it when it closes.",
        ["Perfis automáticos"] = "Automatic profiles", ["Ligados: o perfil de cada jogo é ativado quando ele abre."] = "On: each game's profile is activated when it opens.", ["Desligados."] = "Off.",
        ["Modo compacto (segundo monitor)"] = "Compact mode (second monitor)", ["Janela pequena com CPU, GPU, RAM e temperaturas."] = "Small window with CPU, GPU, RAM and temperatures.",
        ["agora"] = "now", ["Última otimização"] = "Last optimization", ["Jogos"] = "Games", ["Seus jogos"] = "Your games", ["Perfil"] = "Profile",
        ["Encontrado"] = "Found", ["Não encontrado"] = "Not found", ["Abrir as configurações deste jogo"] = "Open this game's settings",
        ["Clique em um jogo para aplicar o preset competitivo ou um perfil."] = "Click a game to apply the competitive preset or a profile.",
        ["Analise o PC para receber recomendações"] = "Analyze the PC to get recommendations", ["Recomendações da última análise"] = "Recommendations from the last analysis",
        ["A Otimização inteligente lê o hardware e o estado de cada ajuste e recomenda só o que se aplica."] = "Smart Optimize reads the hardware and the state of each tweak and recommends only what applies.",
        ["Abrir a Otimização inteligente"] = "Open Smart Optimize",
        ["Novo visual do Qrztweaks"] = "New Qrztweaks look",
        ["O tema Azul meia-noite (preto, branco e azul) foi feito para o novo design. Seu tema atual continua disponível em Configurações → Aparência."] =
            "The Midnight Blue theme (black, white and blue) was made for the new design. Your current theme stays available in Settings → Appearance.",
        ["Aplicar o tema"] = "Apply the theme", ["Agora não"] = "Not now",

        // ---------- Indicadores (Meu PC e Início) ----------
        ["Sem sensor disponível"] = "No sensor available",
        ["Nenhum sensor de temperatura pôde ser lido sem driver extra. A temperatura da CPU exige um driver de kernel que o Qrztweaks não instala, por segurança."] =
            "No temperature sensor could be read without an extra driver. CPU temperature requires a kernel driver that Qrztweaks doesn't install, for safety.",
        ["Pontuação calculada pelo espaço livre no disco do sistema, pelo tempo desde o último reinício e pela permissão de administrador."] =
            "Score calculated from the system drive's free space, the time since the last restart and the administrator permission.",
        ["Nenhuma falha em 7 dias"] = "No failures in 7 days", ["1 falha em 7 dias"] = "1 failure in 7 days", ["Sem dados"] = "No data",
        ["O registro de eventos do Windows não pôde ser lido agora."] = "The Windows event log couldn't be read right now.",
        ["1 dispositivo com erro"] = "1 device with errors", ["Driver de vídeo com mais de 1 ano"] = "Graphics driver over 1 year old",
        ["Nenhum dispositivo com erro"] = "No devices with errors", ["Nenhum erro no Gerenciador de Dispositivos."] = "No errors in Device Manager.",

        // ---------- Meu PC ----------
        ["Copiar resumo"] = "Copy summary", ["Resumo do PC copiado. Cole no ticket de suporte ou onde precisar."] = "PC summary copied. Paste it in the support ticket or wherever you need.",
        ["Copia processador, placa de vídeo, memória, placa-mãe, sistema e armazenamento em texto"] = "Copies processor, graphics card, memory, motherboard, system and storage as text",
        ["Especificações"] = "Specifications", ["Lidas agora do Windows e do driver de cada componente."] = "Read right now from Windows and each component's driver.",
        ["SAÚDE"] = "HEALTH", ["Seu computador"] = "Your computer", ["Build"] = "Build", ["Tempo ligado"] = "Uptime", ["Permissão"] = "Permission",
        ["Administrador"] = "Administrator", ["Usuário"] = "User", ["Windows instalado em"] = "Windows installed on",
        ["Indicadores do computador"] = "Computer indicators", ["Abrir a Central de diagnóstico"] = "Open the Diagnostics Center",
        ["Não foi possível ler os indicadores agora."] = "Couldn't read the indicators right now.",
        ["Frequência atual"] = "Current frequency", ["Uso agora"] = "Usage now", ["Nome completo"] = "Full name", ["Frequência base"] = "Base frequency",
        ["Driver"] = "Driver", ["Data do driver"] = "Driver date", ["Outra placa"] = "Other card", ["Em uso agora"] = "In use now", ["Total instalado"] = "Total installed",
        ["Tipo"] = "Type", ["Módulos"] = "Modules", ["Placa-mãe e BIOS"] = "Motherboard and BIOS", ["Fabricante"] = "Manufacturer", ["Modelo"] = "Model",
        ["BIOS"] = "BIOS", ["Data da BIOS"] = "BIOS date", ["Versão"] = "Version", ["Arquitetura"] = "Architecture", ["Nome do computador"] = "Computer name",
        ["Download agora"] = "Download now", ["Upload agora"] = "Upload now", ["Latência (1.1.1.1)"] = "Latency (1.1.1.1)", ["Estado"] = "State",
        ["Manutenção"] = "Maintenance", ["Atalhos de manutenção"] = "Maintenance shortcuts", ["Diagnóstico inteligente"] = "Smart diagnostics",
        ["Diagnóstico com evidências e grau de confiança"] = "Diagnostics with evidence and confidence level", ["Pontos de restauração"] = "Restore points",
        ["Criar ou restaurar um ponto de restauração"] = "Create or restore a restore point", ["Placa-mãe"] = "Motherboard", ["Unidade"] = "Drive", ["módulo(s)"] = "module(s)",
        ["Seu PC está em ótima forma"] = "Your PC is in great shape", ["Seu PC está bem, com alguns pontos de atenção"] = "Your PC is fine, with a few points of attention",
        ["Seu PC precisa de atenção"] = "Your PC needs attention",

        // ---------- Configurações → Aparência ----------
        ["Medidores circulares, anel de saúde e indicadores."] = "Circular gauges, health ring and indicators.",
        ["Tela inicial"] = "Home screen", ["Escolha o que aparece no Início."] = "Choose what shows on Home.",
        ["Alertas e recomendações"] = "Alerts and recommendations", ["Recomendações da última análise e alertas do sistema."] = "Recommendations from the last analysis and system alerts.",
        ["Analisador de desempenho, Otimização inteligente, Central de saúde e Central de jogos."] = "Performance Analyzer, Smart Optimize, Health Center and Gaming Center.",
        ["Gráficos ao vivo"] = "Live charts", ["Atividade de CPU, placa de vídeo, memória e rede nos cards, atualizada a cada segundo."] = "CPU, graphics card, memory and network activity in the cards, updated every second.",
        ["Cards de hardware"] = "Hardware cards", ["Processador, memória, placa de vídeo, armazenamento e rede."] = "Processor, memory, graphics card, storage and network.",
        ["Atividade e configurações rápidas"] = "Activity and quick settings", ["Últimas operações, seus jogos e os atalhos de configuração."] = "Latest operations, your games and the settings shortcuts.",
        ["Padrão restaurado: tema Azul meia-noite, cores originais, densidade Padrão, cards Médios e animações ligadas."] =
            "Default restored: Midnight Blue theme, original colors, Default density, Medium cards and animations on.",

        // ---------- Registro do Assistente de BIOS ----------
        ["Assistente de BIOS iniciado"] = "BIOS Advisor initialized", ["Hardware detectado"] = "Hardware detected",
        ["Assistente de BIOS: teste de desempenho de referência salvo"] = "BIOS Advisor: baseline benchmark saved",
        ["Assistente de BIOS: teste de desempenho depois dos ajustes salvo"] = "BIOS Advisor: after benchmark saved",
        ["Jogos competitivos"] = "Competitive gaming", ["jogos competitivos"] = "competitive gaming",

        // ---------- Histórico, planos e textos que estavam sem tradução ----------
        ["Informações"] = "Info", ["Erros"] = "Errors", ["Avisos"] = "Warnings", ["Sucesso"] = "Success",
        ["Nenhum registro corresponde aos filtros."] = "No entries match the filters.",
        ["VITALÍCIO"] = "LIFETIME", ["VITALÍCIA"] = "LIFETIME", ["MENSAL"] = "MONTHLY", ["PERSONALIZADO"] = "CUSTOM", ["INTERMEDIÁRIO"] = "INTERMEDIATE", ["AVANÇADO"] = "ADVANCED", ["BASE"] = "BASE",
        ["Aplicando politicas do Editor de Politica de Grupo (diagnostico, nuvem, IA e Push)"] = "Applying Group Policy Editor policies (diagnostics, cloud, AI and Push)",
        ["Ligada. A Proteção contra Adulteração está ativa; o Windows gerencia esta configuração. Use a Segurança do Windows para revisar a proteção."] =
            "On. Tamper Protection is active; Windows manages this setting. Use Windows Security to review the protection.",
        ["A Central de diagnóstico agora é o único acesso principal aos diagnósticos, evitando duas funções com o mesmo nome e mantendo todas as verificações em um só lugar."] =
            "The Diagnostics Center is now the single main entry point for diagnostics, avoiding two features with the same name and keeping every check in one place.",
        ["Assistente de BIOS agora lê os valores atuais pelo SCEWIN escolhido pelo usuário, sem gravar alterações na BIOS, e mostra o nome exato da opção, o valor e as opções disponíveis."] =
            "BIOS Advisor now reads the current values through the SCEWIN chosen by the user, without writing changes to the BIOS, and shows the exact option name, the value and the available options.",
        ["Aviso de atualização redesenhado: versão atual e nova, novidades em cartões, selo de download seguro e ações mais claras para atualizar ou continuar depois."] =
            "Redesigned update notice: current and new version, what's new in cards, a secure download badge and clearer actions to update or continue later.",
        ["Banco de perfis e caminhos da BIOS com atualizações assinadas, sem precisar atualizar o aplicativo para receber novos perfis."] =
            "BIOS profile and path database with signed updates, without updating the app to get new profiles.",
        ["Correções com categorias fixas, ordenação por uso ou segurança, descrições recolhíveis e cartões mais compactos."] =
            "Fixes with fixed categories, sorting by usage or safety, collapsible descriptions and more compact cards.",
        ["Corrigida a verificação de assinaturas do Windows quando o aplicativo é iniciado pelo PowerShell 7."] = "Fixed the Windows signature check when the app is started from PowerShell 7.",
        ["Histórico de atividade com busca, filtros por resultado, cores e mensagens técnicas disponíveis ao passar o mouse."] =
            "Activity history with search, filters by result, colors and technical messages on hover.",
        ["Leituras da BIOS salvas entre aberturas e invalidadas quando a placa ou a versão da BIOS muda; a nota de otimização passa a considerar os valores lidos."] =
            "BIOS readings saved between launches and invalidated when the board or BIOS version changes; the optimization score now considers the values read.",
        ["Nova Central de diagnóstico com verificações de sistema, drivers, serviços, rede, Bluetooth e integridade do Windows."] =
            "New Diagnostics Center with system, driver, service, network, Bluetooth and Windows integrity checks.",
        ["Nova correção de Bluetooth em Correções: identifica o adaptador físico, restaura o rádio e os serviços de descoberta, recarrega o dispositivo e instala o driver oficial do TP-Link UB500 somente quando esse modelo é detectado."] =
            "New Bluetooth fix in Fixes: identifies the physical adapter, restores the radio and discovery services, reloads the device and installs the official TP-Link UB500 driver only when that model is detected.",
        ["O atualizador agora usa um feed público de releases como fallback quando a API do GitHub atinge o limite de requisições, evitando que novas versões deixem de aparecer."] =
            "The updater now uses a public releases feed as a fallback when the GitHub API hits its rate limit, so new versions keep showing up.",
        ["Página Correções com busca rápida, filtro por categoria e cartões mais destacados para encontrar e executar reparos com menos cliques."] =
            "Fixes page with quick search, category filter and more prominent cards to find and run repairs in fewer clicks.",
        ["Restaura o rádio Bluetooth do Windows, corrige os serviços de descoberta, instala o driver oficial do TP-Link UB500 quando detectado e recarrega o adaptador."] =
            "Restores the Windows Bluetooth radio, fixes the discovery services, installs the official TP-Link UB500 driver when detected and reloads the adapter.",
        ["Saúde do PC com atalhos clicáveis para abrir diretamente os componentes e reparos relacionados."] = "PC health with clickable shortcuts that open the related components and repairs directly.",
        ["Versão do aplicativo sincronizada com o instalador e com o mecanismo de atualizações para garantir que a nova build seja reconhecida corretamente."] =
            "App version synced with the installer and the update engine so the new build is recognized correctly.",
        ["Remove arquivos ou aplicativos: não é desfeito pela reversão."] = "Removes files or apps: not undone by rollback.",
        ["Modos e perfis de visualização"] = "Modes and display profiles",

        // ---------- Início limpo ----------
        ["Bom dia"] = "Good morning", ["Boa tarde"] = "Good afternoon", ["Boa noite"] = "Good evening",
        ["Analisar meu PC"] = "Analyze my PC", ["Revisar {0} ajustes"] = "Review {0} tweaks", ["Analise o PC"] = "Analyze the PC",
        ["{0} ajustes recomendados"] = "{0} recommended tweaks", ["Meça FPS e tempo de quadro"] = "Measure FPS and frame time",
        ["Desligado"] = "Off", ["Ativo"] = "On", ["Ver histórico"] = "See history", ["de"] = "of",
        ["Ler o sistema e os indicadores de novo"] = "Read the system and indicators again", ["Modo compacto (segundo monitor)"] = "Compact mode (second monitor)",
        ["Sem administrador"] = "No admin rights",
        ["O Qrztweaks está sem permissão de administrador: o Windows pode bloquear ajustes, serviços e atualizações."] =
            "Qrztweaks has no administrator permission: Windows may block tweaks, services and updates.",
    };

    private static readonly (Regex Regex, Func<Match, string> Build)[] RedesignPatterns =
    {
        P(@"^(\d+) falhas em 7 dias$", m => $"{m.Groups[1].Value} failures in 7 days"),
        P(@"^(Normal|Quente|Muito quente) · GPU (\d+) °C$", m => $"{(m.Groups[1].Value switch { "Normal" => "Normal", "Quente" => "Hot", _ => "Very hot" })} · GPU {m.Groups[2].Value} °C"),
        P(@"^(\d+) de 100$", m => $"{m.Groups[1].Value} out of 100"),
        P(@"^Saúde (\d+) de 100$", m => $"Health {m.Groups[1].Value} out of 100"),
        P(@"^Pontuação de saúde: (\d+) de 100$", m => $"Health score: {m.Groups[1].Value} out of 100"),
        P(@"^Placa de vídeo a (\d+) °C \(sensor do driver NVIDIA\)\. (.+)$", m => $"Graphics card at {m.Groups[1].Value} °C (NVIDIA driver sensor). {T(m, 2)}"),
        P(@"^Últimos 7 dias no registro de eventos do Windows: (\d+) desligamento\(s\) inesperado\(s\) e (\d+) tela\(s\) azul\(is\)\.$",
            m => $"Last 7 days in the Windows event log: {m.Groups[1].Value} unexpected shutdown(s) and {m.Groups[2].Value} blue screen(s)."),
        P(@"^(\d+) dispositivos com erro$", m => $"{m.Groups[1].Value} devices with errors"),
        P(@"^No Gerenciador de Dispositivos: (.+)$", m => $"In Device Manager: {m.Groups[1].Value}"),
        P(@"^O driver da placa de vídeo é de (.+)\. Versões novas costumam corrigir desempenho em jogos recentes\.$", m => $"The graphics driver is from {m.Groups[1].Value}. Newer versions usually fix performance in recent games."),
        P(@"^Driver de vídeo de (.+)\.$", m => $"Graphics driver from {m.Groups[1].Value}."),
        P(@"^(\d+) unidades$", m => $"{m.Groups[1].Value} drives"),
        P(@"^Uso do disco do sistema \((.+)\)$", m => $"System drive usage ({m.Groups[1].Value})"),
        P(@"^Pouco espaço em (.+)$", m => $"Low space on {m.Groups[1].Value}"),
        P(@"^Espaço ficando curto em (.+)$", m => $"Space running low on {m.Groups[1].Value}"),
        P(@"^(\d+) ajustes aplicados$", m => $"{m.Groups[1].Value} tweaks applied"),
        P(@"^Revisar (\d+) ajustes$", m => $"Review {m.Groups[1].Value} tweaks"),
        P(@"^(\d+) ajustes recomendados$", m => $"{m.Groups[1].Value} recommended tweaks"),
        P(@"^(\d+) ajustes$", m => $"{m.Groups[1].Value} tweaks"),
        P(@"^Análise de (\d\d/\d\d \d\d:\d\d) para o objetivo (.+)\.$", m => $"Analysis from {m.Groups[1].Value} for the {T(m, 2)} goal."),
        P(@"^há (\d+) min$", m => $"{m.Groups[1].Value} min ago"),
        P(@"^há (\d+) h$", m => $"{m.Groups[1].Value} h ago"),
        P(@"^há (\d+) dias$", m => $"{m.Groups[1].Value} days ago"),
        P(@"^há (\d+) s$", m => $"{m.Groups[1].Value} s ago"),
        P(@"^Placa-mãe detectada: (.+)$", m => $"Motherboard detected: {m.Groups[1].Value}"),
        P(@"^Processador detectado: (.+) \((\d+) núcleos/(\d+) threads\)$", m => $"CPU detected: {m.Groups[1].Value} ({m.Groups[2].Value}C/{m.Groups[3].Value}T)"),
        P(@"^Memória detectada: (\d+) módulo\(s\), (.+)$", m => $"Memory detected: {m.Groups[1].Value} module(s), {m.Groups[2].Value}"),
        P(@"^Placa de vídeo detectada: (.+)$", m => $"GPU detected: {m.Groups[1].Value}"),
        P(@"^Regras carregadas: (\d+)$", m => $"Rules loaded: {m.Groups[1].Value}"),
        P(@"^Recomendações geradas: (\d+) \((.+), pontuação (.+), banco v(.+)\)$",
            m => $"Recommendations generated: {m.Groups[1].Value} ({m.Groups[2].Value}, score {(m.Groups[3].Value == "sem dados" ? "n/a" : m.Groups[3].Value)}, database v{m.Groups[4].Value})"),
        P(@"^Assistente de BIOS: BIOS lida pelo SCEWIN \((\d+) de (\d+) configurações reconhecidas\)$", m => $"BIOS Advisor: BIOS read via SCEWIN ({m.Groups[1].Value} of {m.Groups[2].Value} settings recognized)"),
        P(@"^Assistente de BIOS: banco atualizado para a v(.+)$", m => $"BIOS Advisor: database updated to v{m.Groups[1].Value}"),
        P(@"^Assistente de BIOS: falha na detecção: (.+)$", m => $"BIOS Advisor: detection failed: {m.Groups[1].Value}"),
        P(@"^Assistente de BIOS: (.+)$", m => $"BIOS Advisor: {m.Groups[1].Value}"),
    };

    /// <summary>
    /// Linhas antigas do registro gravadas em inglês por versões anteriores (o Assistente de BIOS registrava em
    /// inglês; a Otimização inteligente e o Laboratório usavam os nomes antigos), mostradas em português.
    /// </summary>
    private static readonly (Regex Regex, Func<Match, string> Build)[] LegacyLog =
    {
        P(@"^BIOS Advisor initialized$", _ => "Assistente de BIOS iniciado"),
        P(@"^Hardware detected$", _ => "Hardware detectado"),
        P(@"^Motherboard detected: (.+)$", m => $"Placa-mãe detectada: {m.Groups[1].Value}"),
        P(@"^CPU detected: (.+) \((\d+)C/(\d+)T\)$", m => $"Processador detectado: {m.Groups[1].Value} ({m.Groups[2].Value} núcleos/{m.Groups[3].Value} threads)"),
        P(@"^Memory detected: (\d+) module\(s\), (.+)$", m => $"Memória detectada: {m.Groups[1].Value} módulo(s), {m.Groups[2].Value}"),
        P(@"^GPU detected: (.+)$", m => $"Placa de vídeo detectada: {m.Groups[1].Value}"),
        P(@"^Rules loaded: (\d+)$", m => $"Regras carregadas: {m.Groups[1].Value}"),
        P(@"^Recommendations generated: (\d+) \((.+), score (.+), database v(.+)\)$",
            m => $"Recomendações geradas: {m.Groups[1].Value} ({m.Groups[2].Value}, pontuação {(m.Groups[3].Value == "n/a" ? "sem dados" : m.Groups[3].Value)}, banco v{m.Groups[4].Value})"),
        P(@"^BIOS Advisor: BIOS read via SCEWIN \((\d+) of (\d+) settings recognized\)$", m => $"Assistente de BIOS: BIOS lida pelo SCEWIN ({m.Groups[1].Value} de {m.Groups[2].Value} configurações reconhecidas)"),
        P(@"^BIOS Advisor: database updated to v(.+)$", m => $"Assistente de BIOS: banco atualizado para a v{m.Groups[1].Value}"),
        P(@"^BIOS Advisor: benchmark (baseline|after) saved$", m => m.Groups[1].Value == "baseline" ? "Assistente de BIOS: teste de desempenho de referência salvo" : "Assistente de BIOS: teste de desempenho depois dos ajustes salvo"),
        P(@"^BIOS Advisor: (.+)$", m => $"Assistente de BIOS: {m.Groups[1].Value}"),
        P(@"^Smart Optimize: (.+)$", m => $"Otimização inteligente: {m.Groups[1].Value}"),
        P(@"^Performance Lab: (.+)$", m => $"Laboratório de desempenho: {m.Groups[1].Value}"),
    };

    /// <summary>Mensagem do registro de atividade para exibir: traduzida no inglês; em português, as linhas antigas em inglês viram português.</summary>
    public static string LogText(string message)
    {
        // Linhas antigas em português que citavam "Atividade e reversão" (hoje Histórico)
        message = message.Replace("Consulte a atividade e a reversão", "Consulte o Histórico").Replace("Atividade e reversão", "Histórico");
        if (Active) return Tr(message);
        foreach (var (regex, build) in LegacyLog)
        {
            var m = regex.Match(message);
            if (m.Success) return build(m);
        }
        return message;
    }
}
