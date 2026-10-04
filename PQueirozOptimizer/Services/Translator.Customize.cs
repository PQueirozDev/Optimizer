using System.Text.RegularExpressions;

namespace PQueirozOptimizer.Services;

/// <summary>Textos da v1.8.14: Personalizar Windows, níveis de risco, limpeza ampliada, tarefas agendadas e reversão por ajuste.</summary>
public static partial class Translator
{
    private static readonly Dictionary<string, string> CustomizeExact = new(StringComparer.Ordinal)
    {
        // ---------- Personalizar Windows ----------
        ["Personalizar Windows"] = "Customize Windows",
        ["Barra de tarefas, Explorador, área de trabalho e outras preferências do Windows."] = "Taskbar, File Explorer, desktop and other Windows preferences.",
        ["Ajustes do dia a dia, aplicados na hora"] = "Everyday settings, applied instantly",
        ["Cada interruptor mostra como o Windows está agora. As mudanças não passam pela revisão de otimizações: para voltar, é só desligar de novo. As marcadas como recomendadas deixam o Windows mais prático e melhor para jogos."] = "Each switch shows how Windows is set right now. Changes don't go through the optimization review: to undo, just switch it back. The recommended ones make Windows handier and better for gaming.",
        ["Aplicar recomendados"] = "Apply recommended",
        ["Reiniciar o Explorer"] = "Restart Explorer",
        ["Algumas mudanças só aparecem depois de reiniciar o Explorer (a barra de tarefas some e volta; janelas de pastas abertas fecham)."] = "Some changes only show up after restarting Explorer (the taskbar disappears and comes back; open folder windows close).",
        ["Explorer reiniciado: a barra de tarefas volta em alguns segundos."] = "Explorer restarted: the taskbar comes back in a few seconds.",
        ["Tudo já está como recomendado."] = "Everything is already as recommended.",
        ["Não foi possível alterar"] = "Couldn't change it",
        ["Recomendado: ligado"] = "Recommended: on", ["Recomendado: desligado"] = "Recommended: off",
        ["Barra de tarefas"] = "Taskbar", ["Explorador de Arquivos"] = "File Explorer", ["Área de trabalho"] = "Desktop", ["Sistema"] = "System", ["Teclado e mouse"] = "Keyboard and mouse",
        ["Ícones alinhados à esquerda"] = "Icons aligned to the left",
        ["Iniciar e os apps ficam no canto esquerdo, como no Windows 10."] = "Start and apps sit in the left corner, like Windows 10.",
        ["Botão \"Finalizar tarefa\""] = "\"End task\" button",
        ["Clique direito num app da barra para fechá-lo na hora, sem abrir o Gerenciador de Tarefas."] = "Right-click an app on the taskbar to close it right away, without opening Task Manager.",
        ["Segundos no relógio"] = "Seconds on the clock", ["Mostra os segundos no relógio da barra de tarefas."] = "Shows seconds on the taskbar clock.",
        ["Botão Visão de Tarefas"] = "Task View button", ["Botão que mostra as janelas abertas e as áreas de trabalho virtuais."] = "Button that shows open windows and virtual desktops.",
        ["Pesquisa só como ícone"] = "Search as an icon only", ["Troca a caixa de pesquisa larga por uma lupa, liberando espaço na barra."] = "Replaces the wide search box with a magnifier, freeing space on the taskbar.",
        ["Mostrar extensões dos arquivos"] = "Show file extensions",
        ["Exibe o .exe, .pdf, .zip no nome: evita abrir um \"documento.pdf.exe\" por engano."] = "Shows .exe, .pdf, .zip in the name: avoids opening a \"document.pdf.exe\" by mistake.",
        ["Mostrar arquivos ocultos"] = "Show hidden files", ["Exibe pastas como AppData e arquivos marcados como ocultos."] = "Shows folders like AppData and files marked as hidden.",
        ["Abrir em \"Este Computador\""] = "Open to \"This PC\"", ["O Explorador abre mostrando as unidades de disco, em vez da página Início."] = "File Explorer opens showing your drives instead of the Home page.",
        ["Menu de contexto clássico"] = "Classic context menu", ["O clique direito abre direto o menu completo, sem precisar de \"Mostrar mais opções\"."] = "Right-click opens the full menu directly, without \"Show more options\".",
        ["Caixas de seleção nos itens"] = "Item check boxes", ["Marque vários arquivos com o mouse, sem segurar Ctrl."] = "Select several files with the mouse, without holding Ctrl.",
        ["\"Este Computador\" na área de trabalho"] = "\"This PC\" on the desktop", ["Lixeira na área de trabalho"] = "Recycle Bin on the desktop",
        ["Pasta do usuário na área de trabalho"] = "User folder on the desktop", ["Rede na área de trabalho"] = "Network on the desktop", ["Painel de Controle na área de trabalho"] = "Control Panel on the desktop",
        ["Mostra ou esconde o ícone na área de trabalho."] = "Shows or hides the icon on the desktop.",
        ["Histórico da área de transferência"] = "Clipboard history", ["Win + V mostra os últimos itens copiados, não só o último."] = "Win + V shows your recent copied items, not just the last one.",
        ["Num Lock ligado ao iniciar"] = "Num Lock on at startup", ["O teclado numérico já começa ligado na tela de login."] = "The number pad is already on at the sign-in screen.",
        ["Caminhos longos (mais de 260 caracteres)"] = "Long paths (over 260 characters)",
        ["Permite pastas e arquivos com caminho muito comprido, comum em projetos e jogos com mods."] = "Allows folders and files with very long paths, common in projects and modded games.",
        ["Agitar a janela minimiza as outras"] = "Shaking a window minimizes the others", ["Segurar a barra de título e sacudir minimiza todas as outras janelas."] = "Holding the title bar and shaking it minimizes all other windows.",
        ["Modo escuro do Windows"] = "Windows dark mode", ["Barra de tarefas, Iniciar, Configurações e apps compatíveis ficam escuros."] = "Taskbar, Start, Settings and supported apps turn dark.",
        ["Menus sem atraso"] = "No menu delay", ["Submenus abrem na hora ao passar o mouse, sem a espera padrão de 0,4 s."] = "Submenus open as soon as you hover, without the default 0.4 s wait.",
        ["Precisão do ponteiro (aceleração do mouse)"] = "Enhance pointer precision (mouse acceleration)",
        ["Com ela ligada, o ponteiro anda mais quanto mais rápido você move o mouse; desligada, a mira fica consistente nos jogos."] = "When on, the pointer travels farther the faster you move the mouse; when off, your aim stays consistent in games.",
        ["Atalho das Teclas de Aderência (Shift 5 vezes)"] = "Sticky Keys shortcut (Shift 5 times)",
        ["Apertar Shift cinco vezes abre o aviso das Teclas de Aderência, que costuma tirar você do jogo. Vale a partir do próximo login."] = "Pressing Shift five times opens the Sticky Keys prompt, which tends to pull you out of the game. Takes effect from the next sign-in.",

        // ---------- Revisão de ajustes ----------
        ["Seguro"] = "Safe", ["Moderado"] = "Moderate", ["Arriscado"] = "Risky",
        ["Pode desligar algo que você usa; aplique só se souber que não precisa."] = "May turn off something you use; apply only if you know you don't need it.",
        ["Pode mudar algum comportamento do Windows; revise antes."] = "May change some Windows behavior; review it first.",
        ["Remoções de apps e arquivos não são desfeitas pelo backup. Alguns ajustes só valem depois de reiniciar o Windows. Cada ajuste mostra o risco: Seguro, Moderado ou Arriscado. Ajustes que não se aplicam a este PC ficam ocultos."] = "App and file removals aren't undone by the backup. Some settings only take effect after restarting Windows. Each setting shows its risk: Safe, Moderate or Risky. Settings that don't apply to this PC are hidden.",
        ["Desativando a inicializacao rapida (Fast Startup)"] = "Turning off Fast Startup",
        ["Desativando a hibernacao (libera o espaco do hiberfil.sys)"] = "Turning off hibernation (frees the hiberfil.sys space)",
        ["Desativando o ULPS da placa de video AMD"] = "Turning off ULPS on the AMD graphics card",
        ["Recall desativado"] = "Recall disabled", ["Click To Do desativado"] = "Click To Do disabled", ["Apps em segundo plano bloqueados"] = "Background apps blocked",
        ["A hibernacao ja esta desativada; nada a fazer."] = "Hibernation is already off; nothing to do.",
        ["Nenhuma placa AMD com ULPS encontrada; nada a fazer."] = "No AMD card with ULPS found; nothing to do.",
        ["Hibernacao religada"] = "Hibernation turned back on",

        // ---------- Atividade e reversão ----------
        ["Desfazer"] = "Undo", ["Ajustes de versões anteriores"] = "Settings from earlier versions",
        ["Feitos antes do backup por ajuste; voltam todos juntos com \"Restaurar configurações\"."] = "Made before per-setting backups; they all go back together with \"Restore settings\".",

        // ---------- Limpeza ----------
        ["Sobras do Windows Update"] = "Windows Update leftovers", ["Cache de entrega de atualizações"] = "Delivery Optimization cache",
        ["Relatórios de erro do Windows"] = "Windows error reports", ["Despejos de travamento"] = "Crash dumps", ["Despejo de memória completo"] = "Full memory dump",
        ["Todas as unidades"] = "All drives",

        // ---------- Novidades da v1.8.14 ----------
        ["Nova página Personalizar Windows: 24 preferências com liga/desliga aplicadas na hora, como barra de tarefas à esquerda, botão Finalizar tarefa, extensões de arquivo, menu de contexto clássico, Num Lock e aceleração do mouse."] = "New Customize Windows page: 24 preferences with on/off switches applied instantly, such as a left-aligned taskbar, End task button, file extensions, classic context menu, Num Lock and mouse acceleration.",
        ["Cada ajuste da revisão mostra o risco (Seguro, Moderado ou Arriscado), e os que não se aplicam a este PC (placa de vídeo, Windows 11, desktop) ficam ocultos."] = "Each setting in the review shows its risk (Safe, Moderate or Risky), and those that don't apply to this PC (graphics card, Windows 11, desktop) are hidden.",
        ["Atividade e reversão: agora dá para desfazer um ajuste de cada vez, sem reverter os outros."] = "Activity and restore: you can now undo one setting at a time without restoring the others.",
        ["Novos ajustes: desligar o Recall e o Click To Do, a hibernação, a inicialização rápida, os apps da Store em segundo plano e o ULPS de placas AMD."] = "New settings: turn off Recall and Click To Do, hibernation, Fast Startup, Store apps in the background and ULPS on AMD cards.",
        ["Limpeza ampliada: sobras do Windows Update, cache de entrega, relatórios de erro, despejos de travamento e lixeira, com o tamanho de cada um antes de limpar."] = "Expanded cleanup: Windows Update leftovers, delivery cache, error reports, crash dumps and Recycle Bin, with the size of each before cleaning.",
        ["Tarefas agendadas: executar, parar e excluir (só as de programas) direto na página Inicialização."] = "Scheduled tasks: run, stop and delete (program tasks only) right from the Startup page.",
        ["Novo idioma: espanhol."] = "New language: Spanish.",
        ["Removido o idioma espanhol: o app volta a ter português e inglês. Quem usava espanhol passa a ver o app em português."] = "Spanish language removed: the app is back to Portuguese and English. Anyone using Spanish now sees the app in Portuguese.",
        ["Abertura nova, com logo animado, nome letra a letra e anel de progresso."] = "New startup screen, with an animated logo, letter-by-letter name and progress ring.",

        // ---------- Tarefas agendadas ----------
        ["Executar agora"] = "Run now", ["Parar"] = "Stop", ["Excluir tarefa"] = "Delete task", ["Tarefa agendada"] = "Scheduled task",
        ["Excluir tarefa agendada"] = "Delete scheduled task", ["Não foi possível"] = "Couldn't do it",
    };

    private static readonly (Regex Regex, Func<Match, string> Build)[] CustomizePatterns =
    {
        P(@"^(\d+) itens · esvaziada de uma vez, em todas as unidades$", m => $"{m.Groups[1].Value} items · emptied at once, on all drives"),
        P(@"^Desfazendo só este ajuste: (.+)\. O restante do backup continua guardado\.$", m => $"Undoing only this setting: {T(m, 1)}. The rest of the backup stays saved."),
        P(@"^(\d+) opções ajustadas para o recomendado\.$", m => $"{m.Groups[1].Value} options set to the recommended value."),
        P(@"^(.+): (executada|parada|excluída)\.$", m => $"{m.Groups[1].Value}: {m.Groups[2].Value switch { "executada" => "started", "parada" => "stopped", _ => "deleted" }}."),
        P(@"^Estas opções vão mudar:\n\n([\s\S]+)\n\nContinuar\?$", m => $"These options will change:\n\n{string.Join("\n", m.Groups[1].Value.Split('\n').Select(l => Regex.Replace(l, @"^• (.+): (ligar|desligar)$", x => $"• {Tr(x.Groups[1].Value)}: {(x.Groups[2].Value == "ligar" ? "turn on" : "turn off")}")))}\n\nContinue?"),
        P(@"^Excluir a tarefa ""(.+)""\?\n\nEla é apagada do Agendador de Tarefas e não volta pela reversão\. Se só quer que ela pare de rodar, desligue o interruptor\.$", m => $"Delete the task \"{m.Groups[1].Value}\"?\n\nIt is removed from Task Scheduler and isn't brought back by restoring. If you just want it to stop running, turn off the switch."),
    };
}
