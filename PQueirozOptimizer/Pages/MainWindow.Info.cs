using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;
public partial class MainWindow
{
    private void ActivateAdminLicense()
    {
        var service = new LicenseService();
        var activation = new ActivationWindow(service) { Owner = this };
        if (activation.ShowDialog() == true && service.TryGetActiveLicense(out var license, out _) && license is not null)
        {
            (Application.Current as App)?.SetActiveLicense(license);
            PlanAccess.DemoMode = false;
            UpdateLicenseUi();
            // A mesma tela serve para liberar os recursos de administrador e para renovar a licença
            Msg(license.IsAdmin ? "Chave ativada. Os recursos de administrador já estão disponíveis." : "Chave ativada. Sua licença foi atualizada.", "Ativação concluída", MessageBoxButton.OK, MessageBoxImage.Information);
            NavigateTo("dashboard");
        }
    }

    private static readonly (string Version, string Date, string[] Notes)[] PatchNotes =
    {
        ("v1.10.4", "10/10/2026", new[]
        {
            "Executável e identificação do aplicativo no Windows atualizados para Qrztweaks, mantendo licenças, backups e atalhos antigos compatíveis.",
            "Novo ícone violeta com a letra Q e detalhe de raio no app, no instalador e no site.",
            "Revisão dos avisos e textos que ainda usavam o nome antigo.",
        }),
        ("v1.10.3", "10/10/2026", new[]
        {
            "Modo Jogo preserva os itens que não puderam ser restaurados e permite tentar novamente, sem informar sucesso indevido.",
            "Estado de recuperação salvo antes das alterações, com gravação atômica da sessão.",
            "Histórico com lista virtualizada, busca mais leve e atualização automática de novos registros.",
            "Indicadores de restauração pendente, busca adaptável à largura e níveis de atividade destacados.",
            "Corrigida a validação dos planos de otimização em notebooks nos testes de publicação.",
        }),
        ("v1.10.2", "10/10/2026", new[]
        {
            "Modo Jogo preserva os itens que não puderam ser restaurados e permite tentar novamente, sem informar sucesso indevido.",
            "Estado de recuperação salvo antes das alterações, com gravação atômica da sessão.",
            "Histórico com lista virtualizada, busca mais leve e atualização automática de novos registros.",
            "Indicadores de restauração pendente, busca adaptável à largura e níveis de atividade destacados.",
        }),
        ("v1.10.1", "05/10/2026", new[]
        {
            "BIOS Advisor agora lê os valores atuais pelo SCEWIN escolhido pelo usuário, sem gravar alterações na BIOS, e mostra o nome exato da opção, o valor e as opções disponíveis.",
            "Banco de perfis e caminhos da BIOS com atualizações assinadas, sem precisar atualizar o aplicativo para receber novos perfis.",
            "Leituras da BIOS salvas entre aberturas e invalidadas quando a placa ou a versão da BIOS muda; a nota de otimização passa a considerar os valores lidos.",
            "Corrigida a verificação de assinaturas do Windows quando o aplicativo é iniciado pelo PowerShell 7.",
        }),
        ("v1.10.0", "05/10/2026", new[]
        {
            "Novo BIOS Advisor (Sistema): detecta placa-mãe, BIOS, processador, memória e placa de vídeo e mostra o que conferir ou mudar na BIOS, com caminho, risco, impacto térmico e como desfazer. Nada é alterado automaticamente.",
            "Presets Seguro, Desempenho e Competitivo e uma nota de otimização que só conta o que tem evidência: detectado, inferido ou conferido por você na BIOS.",
            "Verificação da versão da BIOS pelo site oficial da fabricante (ASUS), analisador de memória (canais, XMP, slots), monitor de sensores e comparação antes/depois com CSV do PresentMon ou CapFrameX.",
            "Primeiro perfil completo: ASUS TUF GAMING B460M-PLUS com Intel Core i7-10700F, com os caminhos do manual oficial da ASUS.",
        }),
        ("v1.9.2", "05/10/2026", new[]
        {
            "O site oficial agora é qrztwk.vercel.app. Os links de compra e suporte do aplicativo usam o novo endereço.",
        }),
        ("v1.9.1", "05/10/2026", new[]
        {
            "O PQueiroz Optimizer agora se chama Qrztweaks, com logo nova. Licenças, configurações e backups continuam valendo.",
            "O instalador passa a se chamar Qrztweaks-Setup e troca os atalhos antigos pelos novos.",
        }),
        ("v1.9.0", "05/10/2026", new[]
        {
            "Novos planos: Base (R$ 15/mês), Intermediário (R$ 25/mês), Avançado (R$ 29,99/mês, app completo) e Vitalício (R$ 59,99, para sempre). Chaves Mensal já emitidas continuam com o app completo.",
            "Páginas fora do plano mostram o que falta e um botão de upgrade; Atividade e reversão e Pontos de restauração ficam liberados em todos os planos.",
            "Configurações → Minha licença mostra o que o plano libera e um botão de upgrade para cada plano acima do seu.",
            "Personalizar Windows → Efeitos visuais: as Opções de desempenho do Windows dentro do app, com \"Melhor desempenho\", \"Melhor aparência\" e um interruptor por efeito.",
            "A abertura agora aparece sozinha: a janela principal só surge quando a animação termina.",
            "Corrigido o modo translúcido, que não era aplicado ao abrir o app.",
        }),
        ("v1.8.18", "04/10/2026", new[]
        {
            "Corrigido o layout espremido numa coluna estreita, com cartões cortados, na janela normal e maximizada.",
            "Janela maximizada não passa mais por trás da barra de tarefas: a barra de status volta a aparecer, inclusive com dois monitores.",
            "Painel inicial: os cartões ficam lado a lado em telas largas e empilham só quando falta espaço.",
        }),
        ("v1.8.17", "04/10/2026", new[]
        {
            "Otimizações reformuladas: novos ajustes (gravação de jogos em segundo plano, Modo de Jogo, transparência, experiências personalizadas), valores de latência corrigidos e remoção de ações que mais atrapalhavam do que ajudavam.",
            "As otimizações agora aplicam o plano de energia Qrz no lugar do Desempenho Máximo; reverter volta ao plano anterior.",
            "Tela cheia corrigida: a janela maximizada respeita a barra de tarefas, sem bordas cortadas, e o conteúdo fica centralizado em telas largas.",
            "Corrigido o erro ao ligar/desligar grupos em Serviços e interruptores de Apps e Personalizar Windows que podiam fechar o app.",
            "Proteção contra Adulteração detectada: o interruptor do Defender explica o motivo e abre a Segurança do Windows.",
            "Inicialização: apps da Microsoft Store aparecem e podem ser desligados; o botão de abrir as configurações do Windows voltou a funcionar.",
            "Correções de permissão na pasta de atualizações, na limpeza de cache de shaders e na restauração de serviços.",
        }),
        ("v1.8.16", "04/10/2026", new[]
        {
            "Interruptores: a animação aparece sempre, e a página não trava nem volta ao topo ao ligar/desligar em Aparência, Personalizar Windows, Serviços, Apps e Inicialização.",
            "Personalizar Windows e Apps aplicam as mudanças em segundo plano, sem congelar a tela.",
            "Nova tela de execução das otimizações: porcentagem real, etapa atual e quantas faltam.",
            "Nova aba \"Etapas\" mostra o que já foi feito, o que está em andamento e o que falta; o registro detalhado fica em outra aba, sem visual de terminal.",
        }),
        ("v1.8.15", "04/10/2026", new[]
        {
            "Removido o idioma espanhol: o app volta a ter português e inglês. Quem usava espanhol passa a ver o app em português.",
        }),
        ("v1.8.14", "04/10/2026", new[]
        {
            "Nova página Personalizar Windows: 24 preferências com liga/desliga aplicadas na hora, como barra de tarefas à esquerda, botão Finalizar tarefa, extensões de arquivo, menu de contexto clássico, Num Lock e aceleração do mouse.",
            "Cada ajuste da revisão mostra o risco (Seguro, Moderado ou Arriscado), e os que não se aplicam a este PC (placa de vídeo, Windows 11, desktop) ficam ocultos.",
            "Atividade e reversão: agora dá para desfazer um ajuste de cada vez, sem reverter os outros.",
            "Novos ajustes: desligar o Recall e o Click To Do, a hibernação, a inicialização rápida, os apps da Store em segundo plano e o ULPS de placas AMD.",
            "Limpeza ampliada: sobras do Windows Update, cache de entrega, relatórios de erro, despejos de travamento e lixeira, com o tamanho de cada um antes de limpar.",
            "Tarefas agendadas: executar, parar e excluir (só as de programas) direto na página Inicialização.",
            "Novo idioma: espanhol.",
            "Abertura nova, com logo animado, nome letra a letra e anel de progresso.",
        }),
        ("v1.8.13", "04/10/2026", new[]
        {
            "Ao ligar a janela translúcida, o app agora liga também os efeitos de transparência do Windows, avisando antes que a mudança vale para o sistema todo.",
        }),
        ("v1.8.12", "04/10/2026", new[]
        {
            "Dois temas novos: Areia, claro e com tom quente, menos ofuscante que o Claro; e Ameixa, escuro com tom vinho.",
            "Janela translúcida: quando os efeitos de transparência do Windows estão desligados, Configurações → Aparência avisa e abre a opção certa; ao ligá-los, o app fica translúcido na hora.",
            "O tema Automático agora acompanha a troca de claro/escuro do Windows sem precisar reabrir o app.",
        }),
        ("v1.8.11", "04/10/2026", new[]
        {
            "Visual mais limpo: saíram os gradientes, os brilhos e o fundo animado, e os cartões ficaram planos, sem sombra e sem subir ao passar o mouse.",
            "Ícones novos e exclusivos para cada otimização; SFC, DISM, CHKDSK e Reparar agora têm cada um o seu.",
            "Tamanhos de texto padronizados e cabeçalho das páginas mais compacto, deixando mais espaço para o conteúdo.",
            "Nova opção em Configurações → Aparência: janela translúcida, com o fundo desfocado do Windows 11 (requer a versão 22H2 ou mais nova).",
        }),
        ("v1.8.10", "03/10/2026", new[]
        {
            "O atualizador agora usa um feed público de releases como fallback quando a API do GitHub atinge o limite de requisições, evitando que novas versões deixem de aparecer.",
        }),
        ("v1.8.9", "03/10/2026", new[]
        {
            "Versão do aplicativo sincronizada com o instalador e com o mecanismo de atualizações para garantir que a nova build seja reconhecida corretamente.",
        }),
        ("v1.8.8", "03/10/2026", new[]
        {
            "A Central de diagnóstico agora é o único acesso principal aos diagnósticos, evitando duas funções com o mesmo nome e mantendo todas as verificações em um só lugar.",
        }),
        ("v1.8.7", "03/10/2026", new[]
        {
            "Nova Central de diagnóstico com verificações de sistema, drivers, serviços, rede, Bluetooth e integridade do Windows.",
            "Saúde do PC com atalhos clicáveis para abrir diretamente os componentes e reparos relacionados.",
            "Histórico de atividade com busca, filtros por resultado, cores e mensagens técnicas disponíveis ao passar o mouse.",
            "Correções com categorias fixas, ordenação por uso ou segurança, descrições recolhíveis e cartões mais compactos.",
        }),
        ("v1.8.6", "03/10/2026", new[]
        {
            "Aviso de atualização redesenhado: versão atual e nova, novidades em cartões, selo de download seguro e ações mais claras para atualizar ou continuar depois.",
            "Página Correções com busca rápida, filtro por categoria e cartões mais destacados para encontrar e executar reparos com menos cliques.",
        }),
        ("v1.8.5", "03/10/2026", new[]
        {
            "VALORANT nas configurações dos jogos, com dois perfis: Otimizado (o máximo de FPS) e Qrz (a configuração usada pelo Qrz). Sensibilidade, mira, teclas e volume continuam os seus, e o original volta com um clique.",
            "Nova correção de Bluetooth em Correções: identifica o adaptador físico, restaura o rádio e os serviços de descoberta, recarrega o dispositivo e instala o driver oficial do TP-Link UB500 somente quando esse modelo é detectado.",
        }),
        ("v1.8.3", "03/10/2026", new[]
        {
            "Corrigido o fechamento do app ao ligar ou desligar grupos em Serviços enquanto outra ação estava em andamento.",
            "As etiquetas ao lado dos interruptores agora dizem o estado do próprio ajuste (Aplicado ou Padrão), sem contradizer o interruptor.",
            "Nova otimização \"Avançada sem parar serviços\" e nova aba Serviços → Estado dos serviços, com a verificação da 2ª etapa (PcaSvc, DPS, DiagTrack, SysMain e EventLog) e botões para iniciar ou parar.",
            "Aparência: 4 temas novos (Claro, Grafite, Oceano e Floresta) e escolha da cor principal e secundária, com amostras ou um código de cor próprio.",
            "Se o app abrir sem permissão de administrador, ele se reabre pedindo o UAC; itens de inicialização protegidos pelo Windows agora explicam o motivo e abrem a tela certa.",
            "As animações dos interruptores aparecem de verdade (a página não é mais redesenhada no meio delas), com um pulso de brilho ao ligar.",
            "Nova janela de atualização com as novidades da versão, o progresso do download e os botões Atualizar agora e Depois.",
        }),
        ("v1.8.2", "03/10/2026", new[]
        {
            "Corrigido o interruptor que aparecia desligado quando estava ligado (acontecia quando uma ação falhava e o interruptor voltava ao estado anterior, como em Serviços).",
            "Animações novas nos controles: o interruptor desliza com um leve quique, a caixa de seleção desenha o ✓ e os botões afundam e voltam suavemente ao clicar.",
        }),
        ("v1.8.1", "03/10/2026", new[]
        {
            "Abertura até 5 vezes mais rápida: a Visão geral fica pronta em menos de 1 segundo (antes, cerca de 4). As informações do sistema agora são lidas direto do Windows, sem PowerShell.",
            "Nova tela de abertura, que acompanha o tema e mostra cada etapa do carregamento, e entrada mais rápida da interface.",
            "Corrigido o aviso de erro que aparecia durante o tutorial ao destacar um item da tela.",
        }),
        ("v1.8.0", "03/10/2026", new[]
        {
            "Nova página Modo Jogo: fecha programas em segundo plano, pausa serviços (Windows Update, indexação, telemetria) e ativa o plano de desempenho enquanto você joga; ao desativar, tudo volta como estava.",
            "Plano de energia Qrz: plano de baixa latência instalado e ativado em um clique, com volta ao plano anterior.",
            "Perfis de jogos: placa de vídeo dedicada, sem otimizações de tela cheia e prioridade de CPU alta sempre que o jogo abrir.",
            "Liberar memória em espera, menos processos svchost e instalação de runtimes (Visual C++, DirectX, .NET, XNA) pelo winget.",
            "Nova página Rede: teste de velocidade com ping, jitter e perda de pacotes, troca de DNS com medição, ajustes de latência reversíveis e reparos (DNS, relógio e reset).",
            "Monitor ao vivo: CPU, GPU, RAM e ping na barra lateral e gráficos em tempo real na visão geral.",
            "Ryzen X3D com dois CCDs: as otimizações mantêm a Game Bar e o plano Equilibrado, necessários para o jogo usar o 3D V-Cache, e o Modo Jogo mostra o que falta (driver de chipset, Game Bar, plano).",
            "Editor de BIOS pelo SCEWIN (BIOS AMI): leitura de todas as configurações, busca, recomendações seguras (Above 4G, Resizable BAR, Spread Spectrum), gravação só do que mudou e restauração da cópia original.",
            "Perfil NVIDIA para jogos gravado direto no driver (gerenciamento de energia, baixa latência, filtragem de textura, otimização segmentada, V-Sync e cache de sombreador), com restauração.",
            "Instalação limpa de driver com o DDU: ponto de restauração, remoção do driver atual, reinício e o instalador novo abre sozinho no próximo logon.",
            "Configurações dos jogos: presets competitivos para Fortnite, Apex Legends, Counter-Strike 2 e Rocket League, com cópia do arquivo original.",
            "Windows Defender: exclusão das pastas dos jogos e liga/desliga da proteção em tempo real.",
            "Visual novo: fundo aurora animado, cartões de vidro com borda em gradiente que sobem ao passar o mouse, botões com brilho, interruptores, abas, entrada em cascata e anel de saúde animado.",
            "Busca rápida (Ctrl+K) para abrir qualquer página ou recurso, e notificações no canto da tela ao concluir cada ação.",
            "Tutorial na primeira abertura: um tour guiado destaca cada área do app, e Recursos, Modo Jogo, Rede, Serviços e BIOS têm um tutorial curto na primeira visita. Reveja em Configurações → Tutoriais.",
            "Nova página Recursos: verificação de corrupção (ChkDsk, SFC e DISM), runtimes, reinstalação limpa do driver, atalhos do Windows, downloads recomendados e ferramentas de benchmark.",
            "Nova página Correções: 13 reparos rápidos para Windows Update, Loja, ícones, Explorer, áudio, pesquisa, impressão, relógio, DNS, rede, shaders, planos de energia e WinSxS.",
            "Novas páginas Pontos de restauração (criar com nome, etiquetas e cor, e restaurar), Serviços (8 grupos com estado original guardado) e Apps (Discord, Spotify e navegadores sem disputar a GPU, e desinstalador).",
            "BIOS por grupos (memória XMP/EXPO, Resizable BAR, Spread Spectrum, PBO, C-States e virtualização), passo a passo para ASUS e ASRock e aviso em notebooks. Win32 Priority com 6 níveis no Modo Jogo.",
            "Interface redesenhada: Visão geral com cartão de saúde e ação principal, métricas com gráficos reais, menu organizado por categorias, última atividade na barra inferior e grades que se ajustam a telas menores.",
            "Aparência em Configurações: tema Escuro, OLED ou Automático, intensidade do roxo, densidade, tamanho dos cards, animações e pré-visualização ao vivo.",
        }),
        ("v1.7.2", "30/09/2026", new[]
        {
            "A animação de abertura agora aparece também em PCs com os efeitos visuais do Windows reduzidos (como após a otimização do próprio app).",
        }),
        ("v1.7.1", "30/09/2026", new[]
        {
            "Animação de abertura: o logo aparece e a interface entra suavemente.",
            "Configurações → Atualizações: mostra a versão instalada e verifica novas versões na hora, com instalação em um clique.",
        }),
        ("v1.7.0", "29/09/2026", new[]
        {
            "Planos Mensal e Vitalício: o plano vem na chave e aparece no rodapé e em Configurações → Minha licença.",
            "Minha licença mostra titular, plano, validade e ID do computador, com atalhos para renovar, ativar outra chave e pedir suporte.",
            "Renovar pelo Discord: copia o pedido de renovação e abre o servidor; é só abrir um ticket e pagar via Pix.",
            "\"Quero o Vitalício\" gera o pedido de upgrade com o plano desejado.",
        }),
        ("v1.6.0", "29/09/2026", new[]
        {
            "Licença perto de vencer: o painel avisa nos últimos 7 dias, com \"Copiar pedido de renovação\" e \"Ativar nova chave\".",
            "Licença vencida ou recusada: a tela de ativação explica o motivo e já gera o pedido de renovação com o titular.",
            "Chaves revogadas pelo suporte deixam de funcionar ao abrir o aplicativo com internet.",
            "Atrasar o relógio do Windows não estende mais licenças com validade; a hora da internet corrige relógios adiantados.",
            "Os campos da chave e do ID na tela de ativação não cortam mais o texto.",
        }),
        ("v1.5.0", "28/09/2026", new[]
        {
            "Nova página Inicialização no estilo do Autoruns: itens de logon, tarefas agendadas e serviços, com editor, assinatura digital verificada e liga/desliga reversível.",
            "Atalho de modo de energia na Área de Trabalho (Ferramentas): troca entre eficiência, equilibrado e desempenho sem pedir administrador — ideal para notebooks.",
            "Operações longas podem ser canceladas, e erros inesperados não fecham mais o aplicativo.",
            "Painel: discos grandes não aparecem mais com 0% livre, e a memória mostra o valor instalado (16 GB, não 15,9 GB).",
            "Debloat: tarefas de telemetria ausentes no Windows 11 não interrompem mais a etapa, e o OneDrive é removido de verdade no Windows 11.",
            "Versão Padrão: mantém um plano de energia de desempenho já ativo, recria o Alto Desempenho quando apagado e a limpeza de disco agora limpa de fato.",
            "A revisão de ajustes mostra o que tem backup, o que não é reversível e o que é ação pontual; a análise ficou colorida e mais precisa.",
            "Ativação mais fácil: \"Copiar pedido\" gera a mensagem com o ID do computador, e a chave copiada é colada sozinha.",
        }),
        ("v1.4.0", "24/09/2026", new[]
        {
            "Análise do PC mostra placa-mãe, BIOS, memória (XMP/EXPO e dual channel), temperatura e Integridade de Memória.",
            "Nova medição de latência DPC e interrupções na análise e no benchmark, com comparação antes → depois.",
            "Versão Avançada ativa o modo MSI na placa de vídeo (reversível).",
            "Efeitos visuais e precisão do ponteiro agora são aplicados na hora; antes só mudavam o registro.",
            "Serviços já removidos do Windows não geram mais falha, e o plano Desempenho Máximo não é mais duplicado.",
            "O aplicativo abre uma única vez: clicar de novo traz a janela já aberta para frente.",
        }),
        ("v1.3.0", "23/09/2026", new[]
        {
            "Todo o aplicativo agora muda de idioma, incluindo mensagens e a saída das operações.",
            "Saída completa das operações ao vivo, com cores, contadores e botão para copiar o resultado.",
            "Nova página Inicialização do Windows para ativar e desativar programas que abrem com o PC.",
            "Corrige a Versão Avançada, que apagava configurações do agendador multimídia; nova ferramenta Reparar Configurações do Windows.",
            "Novos ajustes reversíveis: anúncios e apps automáticos, Delivery Optimization, jogos em janela, Power Throttling, ID de publicidade, Bing, histórico de atividades e Copilot.",
            "SFC, DISM e CHKDSK exibem a saída corretamente; benchmark de disco usa o winsat (sem cache).",
        }),
        ("v1.2.0", "23/09/2026", new[]
        {
            "Visual novo: tema escuro/claro redesenhado, ícones na navegação e painel com pontuação de saúde.",
            "Atualização automática verificada por SHA256; o aplicativo reabre sozinho ao terminar.",
            "Backups de reversão protegidos contra alteração por outros usuários do Windows.",
            "Rodar várias otimizações seguidas agora reverte para o estado original.",
            "TRIM só em SSDs, GPU Scheduling só em placas compatíveis e SysMain preservado em HDs.",
            "Ponto de restauração criado mesmo quando o Windows já criou um nas últimas 24h.",
            "Licença vinculada ao ID do Windows (chaves antigas continuam válidas).",
        }),
        ("v1.1.1", "17/09/2026", new[]
        {
            "Ativação por chave assinada vinculada ao computador.",
            "Plano de energia com nome Qrztweaks.",
            "Redução de latência para mouse, teclado e USB.",
            "Melhorias nas políticas avançadas de privacidade e desempenho.",
        }),
        ("v1.0.5", "16/09/2026", new[]
        {
            "Modo Avançado com políticas de privacidade e desempenho.",
            "Backups, histórico e reversão de alterações.",
            "Verificação de atualização ao abrir o aplicativo.",
        }),
    };

    private void ShowPatchNotes()
    {
        PageTitle.Text = "Patch notes"; PageBadge.Visibility = Visibility.Visible; PageBadgeText.Text = "Versão atual " + AppVersion;
        var root = new StackPanel { MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        for (var i = 0; i < PatchNotes.Length; i++)
        {
            var (version, date, notes) = PatchNotes[i];
            var content = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            var title = Label(version, 20); title.Margin = new Thickness(0); title.VerticalAlignment = VerticalAlignment.Center;
            title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            head.Children.Add(title);
            if (i == 0) { var latest = Pill("Mais recente", "Success"); latest.Margin = new Thickness(12, 0, 0, 0); head.Children.Add(latest); }
            var when = Label(date, 12, true); when.Margin = new Thickness(12, 0, 0, 0); when.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(when);
            content.Children.Add(head);
            foreach (var note in notes)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var mark = GlyphIcon(Glyphs.Check, 12, i == 0 ? "AccentBrush" : "MutedBrush"); mark.VerticalAlignment = VerticalAlignment.Top; mark.Margin = new Thickness(0, 3, 12, 0);
                row.Children.Add(mark);
                var text = Label(note, 13, i != 0); text.Margin = new Thickness(0);
                row.Children.Add(text);
                content.Children.Add(row);
            }
            var card = Surface(content);
            if (i == 0) card.SetResourceReference(Border.BorderBrushProperty, "AccentSoftBrush");
            root.Children.Add(card);
        }
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
    }

    private void ShowBios()
    {
        PageTitle.Text = "BIOS / UEFI"; PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();

        var hero = new DockPanel();
        var open = IconButton(Glyphs.Refresh, "Reiniciar na BIOS/UEFI", primary: true);
        open.VerticalAlignment = VerticalAlignment.Center; open.Margin = new Thickness(16, 0, 0, 0);
        open.Click += (_, _) =>
        {
            var answer = Msg(
                "O computador será reiniciado AGORA e abrirá diretamente as configurações de firmware (BIOS/UEFI).\n\nSalve seus trabalhos e feche outros programas antes de continuar.\n\nDeseja reiniciar agora?",
                "Reiniciar na BIOS/UEFI", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
            if (_operationRunning) { OperationStatus.Text = "Aguarde a operação em andamento terminar antes de reiniciar."; return; }
            try
            {
                _log.Write("INFO", "Reinício para a BIOS/UEFI solicitado pelo usuário.");
                Process.Start(new ProcessStartInfo("shutdown.exe", "/r /fw /t 5") { UseShellExecute = true, CreateNoWindow = true });
            }
            catch (Exception ex) { Msg("Não foi possível reiniciar na BIOS: " + ex.Message, "BIOS / UEFI", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        DockPanel.SetDock(open, Dock.Right); hero.Children.Add(open);
        var chip = IconChip(Glyphs.Chip, "Accent", 52); DockPanel.SetDock(chip, Dock.Left); hero.Children.Add(chip);
        var heroText = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var ht = Label("Assistente seguro de BIOS", 20); ht.Margin = new Thickness(0, 0, 0, 4);
        heroText.Children.Add(ht);
        var hs = Label("Ajuste a BIOS pelo Windows com o editor abaixo (SCEWIN) ou reinicie direto na BIOS para fazer à mão. Os ajustes que costumam trazer ganho real estão listados no fim da página.", 12.5, true); hs.Margin = new Thickness(0);
        heroText.Children.Add(hs);
        hero.Children.Add(heroText);
        var heroCard = Surface(hero); heroCard.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        root.Children.Add(heroCard);
        root.Children.Add(BiosEditorCard());

        var tips = new[]
        {
            (Glyphs.Memory, "Info", "XMP / EXPO", "Faz a memória rodar na velocidade anunciada. Costuma dar o maior ganho em jogos."),
            (Glyphs.Monitor, "Accent", "Resizable BAR", "Permite à placa de vídeo acessar toda a VRAM de uma vez. Exige CSM desativado."),
            (Glyphs.Speed, "Success", "Modo de energia", "Mantenha C-States habilitados em notebooks; em desktops, o perfil padrão da placa já é bom."),
            (Glyphs.Shield, "Warning", "Atualização de BIOS", "Use apenas o atualizador oficial da fabricante e nunca desligue o PC durante o processo."),
        };
        var grid = Responsive(new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -14, 0) }, 360, 2);
        foreach (var (glyph, tone, title, text) in tips)
        {
            var body = new DockPanel();
            var c = IconChip(glyph, tone, 40); c.VerticalAlignment = VerticalAlignment.Top; DockPanel.SetDock(c, Dock.Left); body.Children.Add(c);
            var t = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
            var tt = Label(title, 14); tt.FontWeight = FontWeights.SemiBold; tt.Margin = new Thickness(0, 0, 0, 4);
            t.Children.Add(tt);
            var td = Label(text, 12, true); td.Margin = new Thickness(0);
            t.Children.Add(td);
            body.Children.Add(t);
            var card = Surface(body); card.Margin = new Thickness(0, 0, 14, 14);
            grid.Children.Add(card);
        }
        root.Children.Add(grid);
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
    }

    #region About Page
    private const string AuthorSiteUrl = "https://pqueiroz.vercel.app/";

    private void ShowAbout()
    {
        PageTitle.Text = _loc.T("Sobre", "About");
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel { MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };

        var hero = new DockPanel();
        var logo = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(20), VerticalAlignment = VerticalAlignment.Top };
        // O logo já tem o próprio bloco arredondado; o fundo de destaque só aparece se a imagem não carregar
        try { logo.Child = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Qrztweaks;component/Assets/app.png")), Width = 72, Height = 72 }; }
        catch (Exception ex) when (ex is System.IO.IOException or UriFormatException) { logo.SetResourceReference(Border.BackgroundProperty, "AccentGradientBrush"); logo.Child = GlyphIcon(Glyphs.Lightning, 30, "OnAccentBrush"); }
        DockPanel.SetDock(logo, Dock.Left); hero.Children.Add(logo);
        var heroText = new StackPanel { Margin = new Thickness(22, 0, 0, 0) };
        var name = new TextBlock { Text = "Qrztweaks", FontSize = 24, FontWeight = FontWeights.Bold };
        name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        heroText.Children.Add(name);
        var badges = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 12) };
        badges.Children.Add(Pill(AppVersion, "Accent"));
        var dev = Pill(_loc.T("Desenvolvido por Pedro Queiroz", "Developed by Pedro Queiroz"), "Info"); dev.Margin = new Thickness(8, 0, 0, 0);
        badges.Children.Add(dev);
        heroText.Children.Add(badges);
        var desc = Label(_loc.T(
            "Otimização, manutenção e gerenciamento do Windows em um só lugar — com revisão de cada ajuste antes de aplicar, backup automático e reversão com um clique.",
            "Windows optimization, maintenance and management in one place — every change is reviewed before applying, with automatic backup and one-click restore."), 13.5, true);
        desc.LineHeight = 21; desc.Margin = new Thickness(0);
        heroText.Children.Add(desc);
        hero.Children.Add(heroText);
        var heroCard = Surface(hero); heroCard.Padding = new Thickness(28); heroCard.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        root.Children.Add(heroCard);

        var grid = Responsive(new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -14, 0) }, 260, 3);
        grid.Children.Add(Card(_loc.T("SEGURANÇA", "SAFETY"), _loc.T("Ponto de restauração e backup antes de cada ajuste", "Restore point and backup before every change"), Glyphs.Shield, "SuccessBrush"));
        grid.Children.Add(Card(_loc.T("TRANSPARÊNCIA", "TRANSPARENCY"), _loc.T("Você escolhe item por item o que aplicar", "You choose exactly what to apply"), Glyphs.Check, "AccentBrush"));
        grid.Children.Add(Card(_loc.T("ATUALIZAÇÕES", "UPDATES"), _loc.T("Instalador verificado por SHA256", "SHA256-verified installer"), Glyphs.Download, "InfoBrush"));
        root.Children.Add(grid);

        var promo = new DockPanel();
        var promoBtn = IconButton(Glyphs.ChevronRight, _loc.T("Visitar site", "Visit website"), primary: true);
        promoBtn.VerticalAlignment = VerticalAlignment.Center; promoBtn.Margin = new Thickness(16, 0, 0, 0);
        promoBtn.Click += (_, _) => OpenAuthorSite();
        DockPanel.SetDock(promoBtn, Dock.Right); promo.Children.Add(promoBtn);
        var promoText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var pt = Label(_loc.T("Precisa de um serviço ou automação?", "Need a service or automation?"), 15); pt.FontWeight = FontWeights.SemiBold; pt.Margin = new Thickness(0, 0, 0, 3);
        promoText.Children.Add(pt);
        var ps = Label(AuthorSiteUrl, 12.5); ps.Margin = new Thickness(0); ps.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        promoText.Children.Add(ps);
        promo.Children.Add(promoText);
        root.Children.Add(Surface(promo));

        var disclaimer = Label(_loc.T(
            "Remoções de arquivos e aplicativos não são desfeitas pelo backup de configurações; consulte os detalhes antes de aplicar.",
            "File and app removals cannot be undone by the configuration backup; review the details before applying."), 12, true);
        disclaimer.Margin = new Thickness(4, 0, 0, 0);
        root.Children.Add(disclaimer);

        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    private void OpenAuthorSite() => OpenUrl(AuthorSiteUrl);
    #endregion
}
