using System.Text.RegularExpressions;

namespace PQueirozOptimizer.Services;

/// <summary>Traduções da reformulação 2.5 (tutorial que passeia pelas páginas, ajuda contextual, painel e configurações).</summary>
public static partial class Translator
{
    private static readonly Dictionary<string, string> ReformExact = new(StringComparer.Ordinal)
    {
        // ---------- Tutorial ----------
        ["Seu novo centro de controle para personalização, monitoramento e otimização do Windows. Vamos conhecer as ferramentas e aprender a usá-las com segurança. Nada é alterado durante o tutorial."] =
            "Your new control center for customizing, monitoring and optimizing Windows. Let's go through the tools and learn to use them safely. Nothing is changed during the tutorial.",
        ["Painel inicial"] = "Home dashboard",
        ["A saúde do PC, o processador, a placa de vídeo, a memória e o disco, com uso ao vivo. O que o Windows não informa aparece como indisponível, nunca estimado."] =
            "Your PC's health, processor, graphics card, memory and disk, with live usage. Anything Windows doesn't report shows as unavailable, never estimated.",
        ["Menu de otimizações"] = "Optimization menu",
        ["Otimização inteligente, ajustes revisados item por item, serviços, aplicativos, inicialização e drivers."] = "Smart Optimize, tweaks reviewed one by one, services, apps, startup and drivers.",
        ["Escolha o objetivo e clique em Analisar: o app só lê o sistema e sugere ajustes. Cada um mostra o risco (Seguro, Moderado ou Arriscado), os efeitos e como desfazer. Nada é aplicado sem você revisar e confirmar."] =
            "Pick a goal and click Analyze: the app only reads the system and suggests tweaks. Each one shows its risk (Safe, Moderate or Risky), its effects and how to undo it. Nothing is applied until you review and confirm.",
        ["Cuidado com os ajustes avançados"] = "Be careful with advanced tweaks",
        ["Itens Moderados e Arriscados podem ter efeito colateral: um recurso do Windows deixa de funcionar ou é preciso reiniciar. Leia o aviso de cada um; na dúvida, deixe desmarcado."] =
            "Moderate and Risky items can have side effects: a Windows feature stops working or a restart is needed. Read each warning; when in doubt, leave it unchecked.",
        ["Um perfil guarda um objetivo, os ajustes escolhidos e os jogos. Use para aplicar a mesma configuração de novo ou trocar entre o modo competitivo e o uso diário."] =
            "A profile stores a goal, the chosen tweaks and the games. Use it to apply the same setup again or to switch between competitive play and daily use.",
        ["Monitoramento e FPS"] = "Monitoring and FPS",
        ["Grave uma partida para medir FPS médio, 1% low e frametime, e compare antes e depois de um ajuste. Sem dados confiáveis, a comparação avisa em vez de dar um veredito."] =
            "Record a match to measure average FPS, 1% low and frametime, and compare before and after a tweak. Without reliable data, the comparison warns you instead of giving a verdict.",
        ["Desfazer alterações"] = "Undo changes",
        ["Tudo o que o app altera tem backup. Aqui você desfaz um ajuste específico ou restaura tudo de uma vez. Pontos de restauração ficam em Ferramentas."] =
            "Everything the app changes is backed up. Here you undo a specific tweak or restore everything at once. Restore points are under Tools.",
        ["Sua conta e configurações"] = "Your account and settings",
        ["Configurações, tema, licença e atualizações ficam neste menu. O tutorial pode ser refeito em Configurações → Tutoriais."] =
            "Settings, theme, license and updates are in this menu. You can take the tutorial again in Settings → Tutorials.",
        ["Comece pelo painel inicial ou pela Otimização inteligente. Bom jogo!"] = "Start with the home dashboard or Smart Optimize. Have a good game!",

        // ---------- Início: Este PC e Otimizações ----------
        ["Objetivo:"] = "Goal:", ["Este PC"] = "This PC", ["Nenhum disco local lido."] = "No local disk read.", ["Otimizações"] = "Optimizations",
        ["Histórico e reversão"] = "History and undo", ["Modo ativo"] = "Active mode", ["Última otimização"] = "Last optimization", ["Nenhuma ainda"] = "None yet",
        ["Ajustes em vigor"] = "Tweaks in effect", ["Analise o PC para saber"] = "Analyze the PC to find out", ["Backup para desfazer"] = "Backup to undo",
        ["Nenhum pendente"] = "None pending", ["Placa de vídeo"] = "Graphics card",

        // ---------- Configurações → Preferências ----------
        ["Monitoramento, avisos e uma cópia das suas preferências para outro PC ou para depois de reinstalar."] = "Monitoring, notifications and a copy of your preferences for another PC or for after reinstalling.",
        ["A cada 1 s (padrão)"] = "Every 1 s (default)", ["A cada 2 s"] = "Every 2 s", ["A cada 5 s (economia)"] = "Every 5 s (power saving)",
        ["Atualização do monitor ao vivo"] = "Live monitor refresh",
        ["Com que frequência CPU, GPU, memória e rede são lidas. Intervalos maiores usam menos CPU. O monitor já para sozinho com a janela minimizada."] =
            "How often CPU, GPU, memory and network are read. Longer intervals use less CPU. The monitor already stops by itself while the window is minimized.",
        ["Avisos de sucesso"] = "Success notifications",
        ["Mostra um aviso no canto da tela quando uma ação termina bem. Erros e alertas aparecem sempre."] = "Shows a notice in the corner of the screen when an action finishes well. Errors and alerts always appear.",
        ["Favoritos"] = "Favorites", ["Tirar dos favoritos (Ctrl+D)"] = "Remove from favorites (Ctrl+D)", ["Adicionar aos favoritos (Ctrl+D)"] = "Add to favorites (Ctrl+D)",
        ["Até {0} favoritos. Tire um para adicionar outro."] = "Up to {0} favorites. Remove one to add another.",
        ["Marque mais com a estrela na busca (Ctrl+K)."] = "Add more with the star in search (Ctrl+K).",
        ["Capas dos jogos pela internet"] = "Game covers from the internet",
        ["Baixa as capas da Biblioteca e do Modo Jogo da Steam e da Epic, o que envia a elas o número de cada jogo. Desligado, aparecem só as capas já baixadas e os ícones dos jogos."] =
            "Downloads Library and Game Mode covers from Steam and Epic, which sends them each game's number. When off, only covers already downloaded and the games' icons appear.",
        ["Exportar preferências"] = "Export preferences", ["Importar preferências"] = "Import preferences",
        ["Preferências exportadas."] = "Preferences exported.", ["Preferências importadas."] = "Preferences imported.",
        ["Trocar as preferências atuais (aparência, modos, tutoriais e monitor) pelas do arquivo? Nenhuma otimização é aplicada."] =
            "Replace the current preferences (appearance, modes, tutorials and monitor) with the ones in the file? No optimization is applied.",
        ["O arquivo é grande demais para ser um arquivo de preferências do Qrztweaks."] = "The file is too large to be a Qrztweaks preferences file.",
        ["O arquivo não é um arquivo de preferências do Qrztweaks."] = "The file isn't a Qrztweaks preferences file.",
        ["Arquivo de preferências vazio."] = "Empty preferences file.", ["Arquivo não encontrado."] = "File not found.",

        // ---------- Fase 5: análise, jogos e histórico ----------
        ["Windows em HDD"] = "Windows on an HDD",
        ["O Windows está num disco rígido (HDD): a inicialização e o carregamento dos jogos dependem mais do disco do que de qualquer ajuste. Passar o Windows para um SSD é a mudança com mais efeito; enquanto isso, a manutenção do disco (desfragmentação) ajuda."] =
            "Windows is on a hard disk (HDD): startup and game loading depend more on the disk than on any tweak. Moving Windows to an SSD is the change with the biggest effect; meanwhile, disk maintenance (defragmentation) helps.",
        ["Cada execução, com o objetivo e quantos ajustes o Windows confirmou depois. Os ajustes não confirmados aparecem no detalhe."] =
            "Each run, with the goal and how many tweaks Windows confirmed afterwards. Unconfirmed tweaks appear in the details.",
        ["Não confirmados:"] = "Not confirmed:",
        ["Sessões de jogo"] = "Game sessions",
        ["Registradas quando um jogo de um perfil com ativação automática abre e fecha. Só datas: nada do jogo é lido."] =
            "Recorded when a game from a profile with automatic activation opens and closes. Dates only: nothing is read from the game.",
        ["sem registro do fim"] = "no end recorded",
        // Descrição do modelo Streaming de versões anteriores (perfis já salvos guardam o texto antigo)
        ["Jogar e transmitir: mantém o codificador livre, sem fechar o OBS."] = "Play and stream: keeps the encoder free, without closing OBS.",

        // ---------- Diagnóstico: quadros e temperatura ----------
        ["Placa de vídeo quente no teste"] = "Graphics card ran hot in the test",
        ["Fluxo de ar do gabinete fraco, poeira ou curva de ventoinha conservadora."] = "Weak case airflow, dust or a conservative fan curve.",
        ["Limpe a placa e melhore a ventilação do gabinete."] = "Clean the card and improve case ventilation.",
        ["Grave uma partida mais longa e veja se o clock da GPU cai junto com a temperatura."] = "Record a longer match and see whether the GPU clock drops as the temperature rises.",
        ["Compare o clock da GPU no início e no fim de uma gravação longa: queda com a temperatura subindo indica limite térmico."] =
            "Compare the GPU clock at the start and end of a long recording: a drop as the temperature rises points to a thermal limit.",
        ["Quadros irregulares (travadas) no último teste"] = "Uneven frames (stutter) in the last test",
        ["Compilação de shaders nas primeiras partidas depois de atualizar o jogo ou o driver."] = "Shader compilation in the first matches after updating the game or the driver.",
        ["Programas em segundo plano disputando a CPU."] = "Background programs competing for the CPU.",
        ["Memória quase cheia ou disco lento carregando partes do mapa."] = "Memory almost full or a slow disk loading parts of the map.",
        ["V-Sync ou limite de FPS mal combinado com a taxa do monitor."] = "V-Sync or an FPS cap poorly matched to the monitor's refresh rate.",
        ["Repita o teste depois de jogar alguns minutos (shaders já compilados)."] = "Repeat the test after playing a few minutes (shaders already compiled).",
        ["Use um limite de FPS um pouco abaixo da taxa do monitor em vez de V-Sync."] = "Use an FPS cap slightly below the monitor's refresh rate instead of V-Sync.",
        ["Grave duas vezes a mesma cena: se as travadas sumirem na segunda, eram shaders sendo compilados."] = "Record the same scene twice: if the stutter is gone the second time, it was shaders being compiled.",
        ["Sem quadros reais (PresentMon) no último teste: a estabilidade do frametime não foi avaliada."] = "No real frames (PresentMon) in the last test: frametime stability wasn't evaluated.",

        // ---------- Histórico unificado e gráficos do Laboratório ----------
        ["Outras alterações feitas pelo app"] = "Other changes made by the app",
        ["Ajustes aplicados pelas páginas do app, fora da otimização. Cada um guarda o valor anterior e é desfeito na página dele."] =
            "Tweaks applied by the app's pages, outside the optimization. Each one keeps the previous value and is undone on its own page.",
        ["Desfazer em Aplicativos"] = "Undo in Apps", ["Desfazer em Rede"] = "Undo in Network", ["Desfazer em Modo Jogo"] = "Undo in Game Mode",
        ["Desfazer em Serviços"] = "Undo in Services", ["Desfazer em Modo Jogo → NVIDIA"] = "Undo in Game Mode → NVIDIA", ["Desfazer em Modo Jogo → Sistema"] = "Undo in Game Mode → System",
        ["Perfil NVIDIA para jogos"] = "NVIDIA gaming profile", ["DNS alterado"] = "DNS changed", ["Plano de energia Qrz ativo"] = "Qrz power plan active",
        ["FPS a cada segundo"] = "FPS each second", ["Frametime médio de cada segundo"] = "Average frametime of each second",

        // ---------- Ajuda contextual ----------
        ["Como funciona"] = "How it works",
        ["O QUE FAZ"] = "WHAT IT DOES", ["QUANDO USAR"] = "WHEN TO USE IT", ["BENEFÍCIO ESPERADO"] = "EXPECTED BENEFIT", ["RISCOS"] = "RISKS", ["COMO DESFAZER"] = "HOW TO UNDO",
        ["Grupos de serviços"] = "Service groups",
        ["Cada grupo desliga um conjunto de serviços e guarda o estado de antes."] = "Each group turns off a set of services and saves how they were before.",

        ["Desliga grupos de serviços do Windows (atualizações, telemetria, impressão, Bluetooth, acesso remoto...) e guarda o tipo de início de cada um."] =
            "Turns off groups of Windows services (updates, telemetry, printing, Bluetooth, remote access...) and saves each one's startup type.",
        ["Quando você não usa o recurso do grupo. Em PC de trabalho ou compartilhado, prefira deixar como está."] = "When you don't use the group's feature. On a work or shared PC, it's better to leave it as is.",
        ["Menos processos em segundo plano. O ganho de FPS costuma ser pequeno e varia de PC para PC."] = "Fewer background processes. The FPS gain is usually small and varies from PC to PC.",
        ["O recurso do grupo para de funcionar. Bloquear o Windows Update deixa o PC sem correções de segurança enquanto estiver desligado."] =
            "The group's feature stops working. Blocking Windows Update leaves the PC without security fixes while it's off.",
        ["Desmarque o grupo: os serviços voltam ao tipo de início guardado. O estado atual de todos aparece na aba Estado dos serviços."] =
            "Uncheck the group: the services go back to the saved startup type. The current state of all of them is in the Service status tab.",

        ["Troca o servidor DNS dos adaptadores conectados, guardando o DNS de cada um."] = "Changes the DNS server of the connected adapters, saving each one's DNS.",
        ["Quando sites e o login nos jogos demoram para abrir, ou para usar um DNS com filtro (ex.: bloqueio de anúncios)."] = "When websites and game logins are slow to open, or to use a filtering DNS (e.g. ad blocking).",
        ["Resolução de nomes mais rápida. Não muda a velocidade da internet nem o ping dentro da partida."] = "Faster name resolution. It doesn't change your internet speed or your in-match ping.",
        ["Redes corporativas ou com DNS próprio (escola, empresa) podem parar de achar servidores internos."] = "Corporate networks or networks with their own DNS (school, company) may stop finding internal servers.",
        ["Escolha \"Automático (provedor)\": cada adaptador volta ao DNS que tinha."] = "Choose \"Automatic (provider)\": each adapter goes back to the DNS it had.",

        ["Limpa o cache DNS, sincroniza o relógio ou reinicia o Winsock e a pilha TCP/IP."] = "Flushes the DNS cache, syncs the clock or resets Winsock and the TCP/IP stack.",
        ["Quando a internet cai, sites não abrem ou o jogo não conecta, mesmo com a rede ligada."] = "When the internet drops, websites don't open or the game doesn't connect even though the network is on.",
        ["Resolve a maioria dos problemas de configuração de rede corrompida."] = "Fixes most problems caused by a corrupted network configuration.",
        ["Redefinir a rede apaga configurações manuais de IP e de VPN e pede reiniciar o PC."] = "Resetting the network erases manual IP and VPN settings and requires restarting the PC.",
        ["Limpar o DNS e sincronizar o relógio não precisam ser desfeitos. A redefinição não tem volta automática: anote IPs manuais antes."] =
            "Flushing DNS and syncing the clock don't need undoing. The reset can't be undone automatically: write down manual IPs first.",

        ["Exclui pastas de jogos da verificação em tempo real ou desliga a proteção em tempo real."] = "Excludes game folders from real-time scanning or turns real-time protection off.",
        ["Exclusões: quando o jogo trava ao carregar arquivos. Desligar a proteção só em último caso e por pouco tempo."] = "Exclusions: when the game stutters while loading files. Turn protection off only as a last resort and briefly.",
        ["Menos leitura de disco durante o carregamento do jogo."] = "Less disk reading while the game loads.",
        ["Uma pasta excluída não é verificada até a exclusão ser removida. Desligar a proteção deixa o PC sem antivírus em tempo real."] =
            "An excluded folder isn't scanned until the exclusion is removed. Turning protection off leaves the PC without real-time antivirus.",
        ["Remova a exclusão na mesma tela ou religue a proteção. O Windows também religa a proteção sozinho depois de um tempo."] =
            "Remove the exclusion on the same screen or turn protection back on. Windows also turns protection back on by itself after a while.",

        ["Muda o quanto o Windows favorece o programa em primeiro plano (Win32PrioritySeparation)."] = "Changes how much Windows favors the foreground program (Win32PrioritySeparation).",
        ["Para testar se o jogo fica mais estável com a janela em foco. Meça no Laboratório antes e depois."] = "To test whether the game is steadier with its window in focus. Measure in the Lab before and after.",
        ["Pode reduzir pequenas variações de frametime em alguns PCs. Não há ganho garantido."] = "May reduce small frametime variations on some PCs. No gain is guaranteed.",
        ["Programas em segundo plano (gravação, chamada de voz) podem receber menos tempo de CPU."] = "Background programs (recording, voice calls) may get less CPU time.",
        ["\"Restaurar original\" volta ao valor que estava antes. Vale na hora, sem reiniciar."] = "\"Restore original\" goes back to the previous value. Takes effect right away, no restart.",

        ["Grava opções da BIOS com o SCEWIN, só nos grupos escolhidos, mostrando o valor atual e o novo."] = "Writes BIOS options with SCEWIN, only for the chosen groups, showing the current and new value.",
        ["Quando você sabe o que cada opção faz na sua placa e já testou o PC estável."] = "When you know what each option does on your board and have already tested the PC as stable.",
        ["Ajustes de memória, energia e PCIe que o Windows não alcança."] = "Memory, power and PCIe settings Windows can't reach.",
        ["Uma combinação errada pode impedir o PC de ligar até limpar a CMOS. Atualizar a BIOS invalida a cópia original."] =
            "A wrong combination can stop the PC from booting until the CMOS is cleared. Updating the BIOS invalidates the original copy.",
        ["\"Restaurar original\" grava a cópia feita antes da primeira alteração, se a BIOS for a mesma. Em último caso, limpe a CMOS."] =
            "\"Restore original\" writes the copy made before the first change, if the BIOS is the same. As a last resort, clear the CMOS.",

        ["Ajustes que o Windows aplica sempre que o jogo abrir: GPU preferida, otimizações de tela cheia e prioridade de CPU."] = "Settings Windows applies whenever the game opens: preferred GPU, fullscreen optimizations and CPU priority.",
        ["Para um jogo específico que roda na GPU errada ou com prioridade baixa."] = "For a specific game that runs on the wrong GPU or at low priority.",
        ["O jogo usa a placa dedicada e recebe prioridade sem você mexer em nada a cada partida."] = "The game uses the dedicated card and gets priority without you touching anything each match.",
        ["Prioridade alta demais pode deixar outros programas lentos durante o jogo."] = "Too high a priority can make other programs slow during the game.",
        ["Remova o perfil do jogo: as chaves do Windows são apagadas e o jogo volta ao padrão."] = "Remove the game's profile: the Windows keys are deleted and the game goes back to default.",

        ["Plano de energia Qrz, limpeza de arquivos temporários e outros ajustes pontuais."] = "Qrz power plan, temporary file cleanup and other one-off tweaks.",
        ["Plano Qrz: em desktop, para jogar. Em notebook na bateria, prefira o Equilibrado."] = "Qrz plan: on a desktop, for gaming. On a laptop on battery, prefer Balanced.",
        ["Plano Qrz: núcleos não entram em economia, o que reduz atrasos ao acordar a CPU."] = "Qrz plan: cores don't park, which reduces delays when the CPU wakes up.",
        ["Mais consumo e calor. Em Ryzen X3D com dois CCDs, o jogo pode ir para o CCD sem 3D V-Cache."] = "More power use and heat. On dual-CCD Ryzen X3D, the game may land on the CCD without 3D V-Cache.",
        ["Desative o plano Qrz: o plano anterior volta e o Qrz é removido."] = "Turn the Qrz plan off: the previous plan comes back and Qrz is removed.",
    };

    private static readonly (Regex Regex, Func<Match, string> Build)[] ReformPatterns =
    {
        P(@"^Como funciona: (.+)$", m => $"How it works: {T(m, 1)}"),
        P(@"^Temperatura máxima da GPU (\d+) °C durante (\d+) s\. O driver não informou redução de clock\.$", m => $"Max GPU temperature {m.Groups[1].Value} °C over {m.Groups[2].Value} s. The driver didn't report a clock reduction."),
        P(@"^Frametime médio ([\d,.]+) ms, mas 1% dos quadros passou de ([\d,.]+) ms \(([\d,.]+)× o médio\)\.( 1% low (\d+) FPS contra (\d+) FPS de média\.)?$",
            m => $"Average frametime {m.Groups[1].Value} ms, but 1% of frames went over {m.Groups[2].Value} ms ({m.Groups[3].Value}× the average)." + (m.Groups[4].Success ? $" 1% low {m.Groups[5].Value} FPS vs {m.Groups[6].Value} FPS on average." : "")),
        P(@"^(\d+) de (\d+) confirmados$", m => $"{m.Groups[1].Value} of {m.Groups[2].Value} confirmed"),
        P(@"^O arquivo de preferências tem dados inválidos: (.+)$", m => $"The preferences file has invalid data: {m.Groups[1].Value}"),
        P(@"^As preferências salvas não puderam ser lidas na abertura: (.+)$", m => $"The saved preferences couldn't be read at startup: {T(m, 1)}"),
        P(@"^O arquivo de preferências está corrompido: (.+)$", m => $"The preferences file is corrupted: {m.Groups[1].Value}"),
        P(@"^Preferências corrompidas; os padrões foram restaurados\. (.+)$", m => $"Corrupted preferences; the defaults were restored. {m.Groups[1].Value}"),
        P(@"^(\d\d/\d\d/\d{4} \d\d:\d\d) · (\d+) de (\d+) confirmados$", m => $"{m.Groups[1].Value} · {m.Groups[2].Value} of {m.Groups[3].Value} confirmed"),
        P(@"^(\d+) aplicados · (\d+) recomendados \(análise de (\d\d/\d\d)\)$", m => $"{m.Groups[1].Value} applied · {m.Groups[2].Value} recommended (analysis of {m.Groups[3].Value})"),
        P(@"^(\d+) itens( · (\d\d/\d\d/\d{4}))?$", m => $"{m.Groups[1].Value} items" + (m.Groups[2].Success ? " · " + m.Groups[3].Value : "")),
    };
}
