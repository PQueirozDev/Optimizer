# Arquitetura do Qrztweaks 2.0

Visão de onde cada parte vive e como elas conversam, para quem for evoluir o app.

## Visão geral

```
App.xaml.cs ── licença (LicenseService) ── ActivationWindow / modo demonstração
     │
     └── MainWindow (arquivos parciais por área)
            ├── Pages/MainWindow.*.cs ......... páginas montadas em código
            ├── Engine/ ....................... regras da 2.0 (sem interface)
            ├── Services/ ..................... acesso ao Windows, licença, atualização, tradução
            ├── BiosAdvisor/ .................. detecção de hardware, sensores, regras de BIOS
            └── Otimizador_de_PC.ps1 .......... otimizações em lote (backup e reversão)
```

- **Interface**: WPF montado em código. Os helpers ficam em `MainWindow.Ui.cs` (`Surface`, `Card`, `Pill`,
  `IconButton`, `SectionHeader`...). Temas e cores vêm do `ThemeService`/`AppearanceService` como recursos dinâmicos.
- **Tradução**: a interface é escrita em português; `Translator` percorre a árvore visual e traduz por dicionário
  exato ou padrão (`Translator.*.cs`). As telas da 2.0 ficam em `Translator.V2.cs`. Textos marcados com
  `Translator.SystemDataTag` (nomes de hardware, processos) não são traduzidos. `--i18n` confere tudo.
- **Planos**: `PlanAccess` diz o nível exigido por página e por operação do script. Toda operação passa por ele,
  inclusive no Smart Optimize e no modo demonstração (licença nula = nada liberado além das páginas de leitura).

## Motor de otimização (`Engine/`)

| Arquivo | Papel |
|---|---|
| `TweakCatalog.cs` | Um item por etapa do script, com metadados da fase 3 (risco, reinício, efeitos, verificação, reversão, evidência, objetivos) e a leitura do estado atual. |
| `SmartOptimizer.cs` | Análise (estados, objetivo, bloqueio por plano), avisos do PC, conflitos da seleção, plano de execução por operação e verificação depois de aplicar. Também `LiveSystemState` (registro, serviços, apps da Loja, tarefas) e o histórico. |
| `PerformanceLab.cs` | Métricas de frametime, comparação, histórico, exportação, PresentMon (cópia protegida) e a sessão de captura. |
| `OptimizationLab.cs` | Teste t de Welch e o veredito de um experimento (ganho só com p &lt; 0,05 e diferença ≥ 3%). |
| `DiagnosticsEngine.cs` | Regras de diagnóstico com evidência e confiança, e a coleta dos dados (WMI, contadores, processos). |
| `UserProfiles.cs` | Perfis (só dados), validação de importação, modelos e o observador de jogos. |
| `DisplayInfo.cs` | Resolução e taxa de atualização reais, gravadas nas condições do teste. |

### Por que o catálogo não aplica nada sozinho

O script já resolve backup item a item, ponto de restauração, quarentena de arquivos adulterados, listas permitidas
na reversão e a tela ao vivo. O catálogo só **descreve e verifica**: o Smart Optimize agrupa a seleção por operação
(`padrao`, `debloat`, `gamer`) e chama a mesma execução da tela de revisão. Um teste (`V2Tests`) falha se o catálogo
e o script saírem de sincronia (etapa nova sem item, item sem etapa ou risco diferente do marcador).

### Fluxo do Smart Optimize

1. `HardwareDetector` (WMI) + `SystemInfoService` → `MachineSummary`.
2. `LiveSystemState.LoadAsync` lê apps da Loja e tarefas numa única chamada ao PowerShell.
3. `SmartOptimizer.Analyze` aplica a leitura de cada item, as condições do script (`GetSteps`), o plano e o objetivo.
4. A seleção passa por `SelectionConflicts` (conflitos bloqueantes impedem aplicar) e pela confirmação de risco.
5. `RunLiveAsync` por operação; `OperationJournal` registra cada execução para detectar quedas.
6. `SmartOptimizer.Verify` lê o estado de novo; o resultado vai para `SmartHistory`.

## Segurança das operações elevadas

- Ferramentas do Windows sempre pelo caminho de System32 (`SystemTools`).
- Pastas de dados elevados criadas/reprotegidas por `RegistryTweakStore.EnsureProtectedDirectory` (recusa junctions,
  dono Administradores). SCEWIN e PresentMon são copiados para essas pastas antes de executar.
- Desinstaladores rodam sem `cmd`; exclusões do Defender passam por `DefenderService.ExclusionProblem`.
- Atualizações: URL só do repositório, pasta protegida, SHA-256 no mesmo handle executado e `SHA256SUMS.txt`
  assinado com a chave de releases (`UpdateService.UpdatePublicKeyBlob`).

Detalhes em [guias/SEGURANCA.md](guias/SEGURANCA.md).

## Dados gravados

| Onde | O quê |
|---|---|
| `%ProgramData%\OtimizadorPC\` | Backups do script, registro, serviços, BIOS e drivers (pasta só de administradores). |
| `%ProgramData%\PQueirozOptimizer\` | Atualizações baixadas, registro de operação interrompida, PresentMon. |
| `%LocalAppData%\PQueirozOptimizer\` | Licença, configurações, histórico do Smart Optimize, testes do Performance Lab, perfis, modo compacto. |

## Testes

- `tests/Verify-PowerShell.ps1`: o script.
- `tests/Optimizer.Verification`: todo o resto. Opções: `--safety` (Etapa A), `--v2` (motor 2.0), `--recovery`,
  `--branding`, `--i18n`, `--render`. O padrão roda tudo menos as telas e o i18n.
