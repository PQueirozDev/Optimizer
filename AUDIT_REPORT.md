# Auditoria do Qrztweaks — reformulação 2.5

Base: `main` em `fd7ee90` (v2.4.1) · Data: 25/10/2026 · Escopo: app WPF (`PQueirozOptimizer/`), script `Otimizador_de_PC.ps1` e testes.

## Como foi feita

1. Linha de base antes de qualquer mudança: build sem erros nem avisos; suíte PowerShell verde; suíte C# com
   **416 PASS** depois de corrigir um teste que dependia do VALORANT estar fechado no PC (ver A1).
2. Quatro auditorias somente leitura em paralelo (onboarding/configurações, dashboard/desempenho do app,
   otimização inteligente/jogos/Laboratório, segurança/histórico/diagnóstico) e uma auditoria independente do Codex.
3. **Todo apontamento foi conferido no código** antes de entrar aqui. O que não se confirmou foi descartado.
   A auditoria anterior (`docs/auditoria/ETAPA-A.md`) foi relida: os itens já corrigidos lá não se repetem.

Severidade: **Alta** = perda de dados do usuário, alteração do Windows sem volta ou falha de segurança;
**Média** = resultado errado mostrado ao usuário, travamento da interface ou recurso que diz ter funcionado sem ter;
**Baixa** = desperdício de recursos ou inconsistência sem efeito no sistema.

## O que o projeto já tem (e não deve ser reescrito)

A maior parte do que o pedido descreve **já existe e funciona**; a reformulação constrói em cima disso:

| Área | Situação |
|---|---|
| Tutorial de primeira execução | Tour com destaque (máscara com recorte), balões, Anterior/Próximo, progresso, Pular, Esc e setas, salvo nas preferências, "Rever tutoriais" nas Configurações, tutoriais curtos por página. |
| Busca de funcionalidades | Paleta Ctrl+K com palavras-chave. |
| Dashboard | CPU, GPU, RAM (uso e total), uso de CPU/GPU ao vivo, temperatura da GPU NVIDIA (NVML; sem sensor = "Sem sensor", nunca inventado), gráficos 1/5/15 min, atalhos, alertas. |
| Otimização inteligente | Análise de hardware e do estado atual de cada ajuste, objetivos (Competitivo, Notebook, Desenvolvimento, Uso diário, Personalizado), risco/efeitos/reversão/evidência por item, confirmação reforçada, só os itens marcados, verificação depois de aplicar. |
| Diagnóstico de gargalos | Achados com evidência, causas, recomendações, como confirmar, riscos e confiança; lista "não avaliado por falta de dados". |
| Laboratório de desempenho | FPS médio/mínimo, 1% e 0,1% low, frametime médio/P95/P99, CPU/GPU/RAM, temperatura e clock; comparação com avisos de condições; laboratório A/B com teste de Welch; histórico; PDF, CSV e JSON. |
| Central de jogos | Biblioteca visual (Steam, Epic, GOG, Ubisoft + manual), configurações por jogo para 17 títulos com backup, perfis NVIDIA por jogo, Modo Jogo com recuperação. |
| Segurança | Backups por item no script, quarentena, listas permitidas na reversão, ferramentas por System32, pastas protegidas, atualizador assinado, nenhuma etapa desliga Defender/Firewall/UAC/SmartScreen/VBS. |

## Problemas encontrados

### Corrigidos nesta fase (Fases 1 e 2)

| # | Sev. | Onde | Problema | Correção |
|---|---|---|---|---|
| A1 | Média | `tests/…/Program.cs`, `GameConfigService` | Teste do VALORANT falhava se o jogo estivesse aberto no PC de quem roda a suíte (dependia de processo real). | Leitura de "jogo aberto" injetável; o teste usa uma falsa. |
| A2 | **Alta** | `ConfigService.Load` | Se `config.json` estivesse travado (outra instância, ex.: atalho de Limpeza Rápida), o app carregava os padrões **e gravava por cima**: perfis, aparência e tutoriais apagados sem cópia. | Tenta ler 3 vezes; se não conseguir, a sessão não grava nada (aviso em `LastSaveError`). |
| A3 | Média | `ConfigService.EnsureDefaults` | `"profiles":[null]`, perfil sem nome, `completedTutorials: null` ou ISO com `localPath: null` derrubavam a inicialização ("ERRO DE INICIALIZAÇÃO"). | Nulos limpos antes de qualquer leitura. |
| A4 | Média | `AppearanceSettings` | Um nome de tema desconhecido (preferência de versão mais nova) invalidava o arquivo inteiro e voltava **todas** as preferências ao padrão. | Conversor tolerante: só o campo inválido volta ao padrão. |
| A5 | **Alta** | `NvidiaProfileService` | Backup ilegível lido como vazio + gravação não atômica: a próxima aplicação guardava o estado já otimizado como "original". | `SafeFile`: backup ilegível bloqueia a operação; gravação por troca atômica. |
| A6 | **Alta** | `NetworkService.SetDnsAsync` | Mesmo problema no backup do DNS. | Igual ao A5. |
| A7 | **Alta** | `NetworkService.SetDnsAsync` (auto) | Voltar ao DNS automático apagava o backup inteiro, inclusive de adaptadores desconectados que não foram restaurados. | Só sai do backup o adaptador restaurado. |
| A8 | **Alta** | `PowerPlanService.Install` | Plano anterior gravado **depois** de ativar o Qrz: uma queda no meio deixava o Qrz ativo sem saber para onde voltar. | Grava (atômico, pasta protegida) antes de ativar. |
| A9 | **Alta** | `RegistryTweakStore.Revert` | Itens fora da lista permitida eram pulados e o backup era apagado: o valor original se perdia para sempre e a tela dizia "revertido". | Itens pulados ficam no backup e a reversão avisa. |
| A10 | Média | `ServiceGroupsService.DisableAsync` | Sucesso registrado e grupo marcado "Aplicado" mesmo quando nenhum `sc config` funcionou. | Nenhum alterado = erro e backup desfeito; parcial = aviso com os nomes. |
| A11 | Média | Página Serviços | "Bloquear o Windows Update", "Desligar o acesso remoto" e "Serviços do Xbox" aplicavam com um clique; o aviso só estava no texto. | Confirmação com o aviso antes de desligar. |
| A12 | Média | `RestorePointService.CreateAsync` (pendência #17 da Etapa A) | Tempo limite ou Cancelar matavam o PowerShell antes do `finally`: o limite de um ponto por dia ficava em 0. | O app lê o valor antes e devolve num `finally` próprio. |
| A13 | Baixa | `PowerShellBridge.RunScriptAsync` | Falha ao encerrar o processo trocava o `TimeoutException` por outro erro. | Exceções de `Kill` tratadas como no `RunScriptFileAsync`. |
| A14 | Média | Histórico | Um item malformado no backup derrubava o painel inteiro e escondia o "Desfazer" dos outros. | Leitura tolerante por item. |
| A15 | Média | Exportar atividade | Log exportado com caminho do perfil, nome da conta e do PC, pilhas de erro. | Exportação com pasta do usuário, conta, PC, e-mails e chaves trocados por marcadores. |
| A16 | Média | `NetworkService.ResetNetworkAsync` / `FlushDnsAsync` | "Rede redefinida" registrado como sucesso mesmo com `netsh` falhando. | Falhas somadas e informadas. |
| A17 | Média | `AppOptimizerService` (Discord) | Backup feito antes de validar o JSON: com o arquivo inválido o ajuste aparecia como aplicado sem ter mudado nada. | Valida antes do backup. |
| A18 | Média | `BiosAdvisorStore` | Histórico do Assistente de BIOS gravado sem troca atômica; arquivo ilegível virava estado vazio e o próximo Save apagava confirmações e testes. | Gravação atômica; arquivo ilegível preservado como `.corrompido-*`. |
| A19 | Média | `DriverService.DownloadDriverAsync` | Download cortado ficava com o nome final e aparecia como instalador pronto. | Baixa em `.part`, confere o tamanho e só então renomeia. |
| A20 | Média | `MainWindow.PerformanceLab` | Parar à mão uma gravação do laboratório A/B salvava o resultado, mas não o entregava ao experimento. | Callback guardado e chamado também na parada manual. |
| A21 | Média | Importar CSV | Log importado recebia as condições **deste** PC: comparar com outro PC não avisava "hardware diferente". | Condições desconhecidas e aviso explícito na comparação. |
| A22 | Média | `OptimizationLab.Evaluate` | Sem FPS, CPU ausente contava como 0 e uma queda de CPU virava "Provável ganho". 1% low antes = 0 gerava variação infinita. | Sem FPS não há veredito de ganho/perda; dado ausente = dados insuficientes; divisão protegida. |
| A23 | Média | Diagnóstico | Usava a captura mais nova, mesmo com meses ou importada de outro PC, e lia o arquivo na thread da interface. | Só capturas deste PC dos últimos 14 dias, lidas fora da interface. |
| A24 | Baixa | `DiagnosticsCollector` | `Process.GetProcesses()` sem liberar os objetos. | Liberados. |
| A25 | Baixa | Perfis | "Aplicar perfil" durante outra operação deixava a seleção guardada e ela vazava para a próxima análise. | Recusado durante operação. |
| A26 | Baixa | Configurações → Modos | "Salvar como novo" com nome existente substituía sem perguntar. | Confirmação. |

Testes novos: `tests/Optimizer.Verification/HardeningTests.cs` (`--hardening`), 13 verificações, e o teste de reversão de registro atualizado.

### Corrigidos nas fases 3 a 8

| # | Sev. | Onde | Problema | Situação |
|---|---|---|---|---|
| B1 | Média | `HardwareMonitorService` + `MainWindow.Hud` | Na bandeja, o monitor continuava lendo a cada 1 s (com ping) e o Início redesenhava gráficos sem ninguém ver. | **Corrigido**: a janela só assina o monitor enquanto aparece; o monitor para se nada mais precisar dele. |
| B2 | Média | `MainWindow.PerformanceLab` | Leitura da captura e `Finish()` (espera o PresentMon) na thread da interface. | **Corrigido**: leitura e encerramento fora da interface; a amostra pendente entra uma vez só; fechar a janela espera a leitura. |
| B3 | Média | `MainWindow.StressTest` | WMI, `Join` e log de eventos na thread da interface. | **Corrigido**: memória pela API nativa; parar e ler o log fora da interface, sem clique duplo. |
| B4 | Baixa | `MainWindow.BiosAdvisorTools` | Sensores lidos com a janela escondida. | **Corrigido** (continua só durante gravação). |
| B5 | Baixa | `CompactMonitorWindow` | `nvidia-smi.exe` a cada 3 s. | **Corrigido**: temperatura do NVML; o objetivo é relido a cada 10 s e aparece como "Objetivo", não "Perfil". |
| B6 | Baixa | `Controls/LineChart` | Canetas e lista recriadas a cada quadro. | **Corrigido**: em cache por pincel do tema. |
| B7 | Baixa | `UpdateLicenseUi` | Eventos duplicados a cada ativação. | **Corrigido**. |
| B8 | Baixa | `MainWindow.HomeAurora` | "Sem sensor" cacheado 10 min. | **Corrigido**: lê o NVML direto se não houver amostra. |
| B9 | Baixa | Splash | ~3,3 s de animação mínima. | **Mantido** (escolha visual); com as animações desligadas ele sai na hora. O Início não espera mais um WMI lento (frequência base). |
| B10 | Média | Tutorial | Não navegava, não retomava, não acompanhava redimensionar. | **Corrigido**: tour pelas páginas reais, retoma do passo salvo, refaz o destaque ao redimensionar/DPI, bloqueia clique no destaque, alvo ausente não trava. Teste `--i18n` desenha os 11 passos e confere cada alvo. |
| B11 | Média | Início | Faltavam Windows, discos, modo ativo, última otimização. | **Corrigido**: cards Este PC e Otimizações (lidos fora da interface). |
| B12 | Média | Navegação | Sem favoritos; ajuda contextual limitada. | **Corrigido**: estrela na busca Ctrl+K (Ctrl+D), favoritos no topo da busca e no Início; "?" com o que faz, quando usar, benefício, riscos e como desfazer em 8 áreas avançadas. |
| B13 | Média | Configurações | Sem frequência do monitor, notificações, exportar/importar; arquivo corrompido só no log. | **Corrigido**: intervalo 1/2/5 s, avisos de sucesso, exportar/importar validado (tamanho, formato, ajustes desconhecidos e links descartados), aviso do arquivo corrompido. |
| B14 | Média | Otimização inteligente | Sem tipo de disco; histórico de execuções invisível. | **Corrigido**: SSD/HDD na análise (aviso para Windows em HDD); execuções no Histórico com os ajustes não confirmados. |
| B15 | Média | Central de jogos | Perfil não ligava antes do jogo; Streaming disparava pelo OBS; sem sessões; lojas faltando. | **Corrigido**: com perfil, o Modo Jogo liga antes de abrir pela Biblioteca; OBS/Discord não disparam perfis; sessões por perfil; Riot (abre o jogo direto), Battle.net, EA app e Xbox (abrem a loja: não há link estável por jogo). Os ajustes do perfil continuam exigindo revisão na Otimização inteligente: nada é aplicado sozinho. |
| B16 | Baixa | Diagnóstico | Sem frametime nem temperatura. | **Corrigido**: quadros irregulares (P99/médio) e GPU quente, com evidência e confiança; sem quadros, aparece como não avaliado. Atividade de disco continua fora (o Windows não dá uma leitura confiável por jogo). |
| B17 | Baixa | Laboratório | Sem gráficos no resultado salvo. | **Corrigido**: FPS, frametime, CPU e GPU por segundo. |
| B18 | Média | Histórico | Alterações das páginas fora do Histórico. | **Corrigido** em parte: registro em C#, grupos de serviços, NVIDIA, DNS e plano Qrz aparecem com o caminho para desfazer. Exclusões do Defender continuam só na página do Defender; o arquivo de sessões do script ainda lista cópias acumuladas. |
| B19 | Baixa | `GameArtService` | Capas buscadas na internet sem opção. | **Corrigido**: opção em Configurações → Preferências; desligada, nenhuma requisição é feita. |

### Revisões do Codex

- Fase 2: 6 apontamentos, todos confirmados e corrigidos (serviços parados sem backup, item retido reaplicável, salvar em modo somente leitura fingindo sucesso, plano anterior gravado com ativação falha, leitura do limite de pontos de restauração, nomes curtos no log).
- Fases 3–7: 4 apontamentos, todos confirmados e corrigidos (importação sem reversão, leitura da captura durante o fechamento, amostra pendente perdida, leituras de arquivo do Início na interface).

### Mantidos de propósito

- **Trava do relógio e revogação** (Etapa A #15) e **ID antigo da máquina** (#16): dependem de um servidor de licenças; sem mudança.
- **Desinstaladores elevados** (Etapa A #19): exige um processo auxiliar sem elevação.
- **Temperatura da CPU**: o Windows não expõe de forma confiável sem driver de kernel; o app mostra "indisponível" em vez de inventar.
- **Barra lateral**: o código da barra lateral antiga está desligado desde o redesign Aurora (navegação por abas e Ctrl+K). Não foi removido.
- **Arquitetura MVVM**: as páginas continuam montadas em C# nas classes parciais; reescrever para MVVM seria um projeto inteiro sem ganho para o usuário. As regras novas (sessões, preferências, diagnóstico) ficaram em classes testáveis fora da interface.

## Riscos das otimizações existentes

- Nenhuma etapa do script mexe em Defender, Firewall, Windows Update, UAC, SmartScreen ou VBS/HVCI (HVCI só é recomendado, em leitura).
- O debloat desliga `DiagTrack`, `WerSvc` e `PcaSvc` (risco moderado, com backup).
- Ações fora do script com efeito sério: desligar a proteção em tempo real do Defender (com confirmação forte), os grupos de serviços (confirmação adicionada em A11) e o editor de BIOS (só com o SCEWIN escolhido pelo usuário, validado e com cópia).

## Compatibilidade

Windows 10 e 11 x64: as etapas exclusivas do 11 são filtradas por `SystemConditions` (`win11`, build 24H2), e o
efeito translúcido (Acrylic) só é ligado no Windows 11. Nada encontrado que quebre o Windows 10.
