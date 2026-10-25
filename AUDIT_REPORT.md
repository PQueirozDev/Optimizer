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

### Para as próximas fases (confirmados, ainda não corrigidos)

| # | Sev. | Onde | Problema | Fase |
|---|---|---|---|---|
| B1 | Média | `HardwareMonitorService` + `MainWindow.Hud` | Minimizado na bandeja, o monitor continua amostrando a cada 1 s (com ping a cada 5 s) e o Início continua redesenhando gráficos e medidores sem ninguém ver. | 7 |
| B2 | Média | `MainWindow.PerformanceLab` | `Tick()` da captura e `Finish()` (espera o PresentMon fechar, até 5 s) rodam na thread da interface. | 7 |
| B3 | Média | `MainWindow.StressTest` | Iniciar/parar o teste de estresse faz WMI, `Join` e leitura do log de eventos na thread da interface. | 7 |
| B4 | Baixa | `MainWindow.BiosAdvisorTools` | Leitura de sensores a cada 3 s continua com a janela escondida. | 7 |
| B5 | Baixa | `CompactMonitorWindow` | Inicia `nvidia-smi.exe` a cada 3 s, mas a amostra já tem a temperatura pelo NVML. | 7 |
| B6 | Baixa | `Controls/LineChart` | Canetas, geometrias e texto recriados a cada quadro. | 7 |
| B7 | Baixa | `UpdateLicenseUi` | Cada ativação de licença adiciona outro `Closed`/`SizeChanged`/`PreviewKeyDown`. | 7 |
| B8 | Baixa | `MainWindow.HomeAurora` | Verificação de temperatura cacheada 10 min como "sem sensor" se o Início abre antes da 1ª amostra (sem recorrer ao NVML). | 7 |
| B9 | Baixa | Splash | Mínimo de ~3,3 s de animação mesmo com o Início pronto antes. | 7 |
| B10 | Média | Tutorial | Não navega entre páginas; não retoma de onde parou; máscara não acompanha redimensionar; passo do monitor some se a seção estiver desligada. | 4 |
| B11 | Média | Dashboard | Sem versão do Windows, sem espaço livre por disco, sem "perfil ativo", "última otimização" ou "otimizações aplicadas"; o modo compacto chama de "Perfil" o objetivo da última análise. | 3 |
| B12 | Média | Navegação | Sem favoritos; ajuda contextual só na Otimização inteligente e no Assistente de BIOS. | 3 |
| B13 | Média | Configurações | Sem frequência de monitoramento, preferências de notificação, exportar/importar as preferências; arquivo corrompido só aparece no log. | 3 |
| B14 | Média | Otimização inteligente | Tipo de disco (SSD/HDD) não entra na análise; o histórico das execuções é gravado e nunca mostrado. | 5 |
| B15 | Média | Central de jogos | Perfil de jogo não ativa os ajustes salvos antes de abrir o jogo; o perfil "Streaming" dispara ao abrir o OBS, não o jogo; sem histórico de sessões por jogo; Battle.net, EA app e Xbox não detectados. | 5 |
| B16 | Baixa | Diagnóstico | Frametime, temperatura e atividade de disco não entram no diagnóstico de gargalo; confiança depende só da duração. | 5 |
| B17 | Baixa | Laboratório | Sem gráfico de frametime nos resultados salvos. | 6 |
| B18 | Média | Histórico | Ajustes de registro feitos em C#, grupos de serviços e Defender não aparecem no Histórico; o arquivo de sessões lista cópias acumuladas. | 2 (próximo passo) |
| B19 | Baixa | `GameArtService` | Busca a arte dos jogos na Steam/Epic sem perguntar (envia o ID do jogo). | 3 |

### Mantidos de propósito

- **Trava do relógio e revogação** (Etapa A #15) e **ID antigo da máquina** (#16): dependem de um servidor de licenças; sem mudança.
- **Desinstaladores elevados** (Etapa A #19): exige um processo auxiliar sem elevação.
- **Temperatura da CPU**: o Windows não expõe de forma confiável sem driver de kernel; o app mostra "indisponível" em vez de inventar.
- **Barra lateral**: o código da barra lateral antiga está desligado desde o redesign Aurora (navegação por abas e Ctrl+K). Não foi removido nesta fase.

## Riscos das otimizações existentes

- Nenhuma etapa do script mexe em Defender, Firewall, Windows Update, UAC, SmartScreen ou VBS/HVCI (HVCI só é recomendado, em leitura).
- O debloat desliga `DiagTrack`, `WerSvc` e `PcaSvc` (risco moderado, com backup).
- Ações fora do script com efeito sério: desligar a proteção em tempo real do Defender (com confirmação forte), os grupos de serviços (confirmação adicionada em A11) e o editor de BIOS (só com o SCEWIN escolhido pelo usuário, validado e com cópia).

## Compatibilidade

Windows 10 e 11 x64: as etapas exclusivas do 11 são filtradas por `SystemConditions` (`win11`, build 24H2), e o
efeito translúcido (Acrylic) só é ligado no Windows 11. Nada encontrado que quebre o Windows 10.
