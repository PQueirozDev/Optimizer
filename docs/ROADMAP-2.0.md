# Qrztweaks 2.0: plano incremental

Branch de trabalho: `dev/qrztweaks-2.0`. Cada etapa termina com testes, commits organizados e um resumo, e a
próxima só começa com autorização. Nada de deploy, release ou infraestrutura paga sem pedido explícito.

## Arquitetura atual (resumo)

- **App WPF (.NET 8)**: `MainWindow` dividida em arquivos parciais por página (`Pages/MainWindow.*.cs`), com a
  interface montada em código e os estilos/temas em `App.xaml` + `ThemeService`/`AppearanceService`.
- **Serviços** (`Services/`): cada área (energia, serviços, rede, NVIDIA, Defender, BIOS, apps, inicialização,
  licença, atualização) tem um serviço próprio. Ajustes de registro feitos em C# passam pelo `RegistryTweakStore`.
- **Script `Otimizador_de_PC.ps1`**: as otimizações em lote (Padrão, Avançada, Debloat, Inteligente, reversão,
  manutenção). O app lê as etapas e os marcadores (`Risco`, `Requer`) do próprio script e o executa com `-UiMode`.
- **BIOS Advisor** (`BiosAdvisor/`): detecção de hardware, regras por plataforma, banco assinado, leitura via
  SCEWIN, sensores e benchmark de frametime (CSV do PresentMon).
- **Testes**: `tests/Verify-PowerShell.ps1` e `tests/Optimizer.Verification` (testes, telas renderizadas, i18n).
- **Entrega**: Inno Setup + GitHub Actions; o app se atualiza pelo GitHub Releases com SHA-256.

## O que já existe para cada fase

Muitas fases do plano 2.0 já têm uma base no app. A regra é **evoluir o que existe**, não criar uma segunda versão.

| Fase | Base existente | O que falta |
|---|---|---|
| 2. Smart Optimize | `Otimizar-Inteligente`, `Analisar-PC`, `SystemConditions` (marcador `Requer`), tela de revisão item a item | Ler o estado atual de cada ajuste (já aplicado, alterado por fora), detector de conflitos, uma tela única de recomendações |
| 3. Risco | `StepRisk`/`StepEffect`, marcadores no script, confirmação extra (Etapa A) | Metadados completos: reinicialização, verificação, reversão, evidência |
| 4. Objetivos | Perfis de energia, Modo Jogo, grupos de serviços (Hyper-V/WSL avisados) | Assistente por objetivo usando o catálogo de ajustes |
| 5. Optimization Lab | Benchmark antes/depois do BIOS Advisor, `FrametimeAnalyzer` | Fluxo por ajuste com aviso de variância |
| 6. Reversão | Snapshot do script, `RegistryTweakStore`, recuperação do Modo Jogo, registro de operação interrompida (Etapa A) | Estado verificado depois de aplicar, item a item, na tela ao vivo |
| 7. Performance Lab | `BenchmarkRun`, `FrametimeAnalyzer` (PresentMon), `SensorReader`, `HardwareMonitorService` | Captura ao vivo por processo, histórico, exportação CSV/JSON |
| 8. Diagnóstico | Página Diagnósticos, nota de saúde, `Analisar-PC` | Evidência + confiança por diagnóstico |
| 9. Command Center | `MainWindow.Dashboard.cs` | Redesenho com cards e gráficos reais |
| 10–12. Visual | 9 temas, intensidade, densidade, tamanho dos cards, animações, janela translúcida | Cor de destaque livre, sidebar compactável, redução de movimento do Windows |
| 13. HUD | `MainWindow.Hud.cs` | Modo compacto para segundo monitor (janela externa, sem injeção) |
| 14. Execução ao vivo | `MainWindow.LiveRun*.cs` (timeline, cancelar, falhas) | Marcar concluído só após verificar o resultado |
| 15. Perfis | `GameConfigService` (presets por jogo), perfis de aparência | Perfis completos importáveis/exportáveis, sem scripts |
| 16. Licença | RSA, planos, revogação, trava do relógio | Tela de ativação melhor, aviso de expiração, troca de hardware |
| 17. Atualizador | GitHub Releases + SHA-256 + pasta protegida | Autenticidade (decisão pendente), pular direto para a versão mais recente |
| 18–19. Site | `site/` (Vercel) | Versão automática (o HTML ainda cita v1.8.0), benchmarks reais com metodologia |
| 20. Demonstração | Planos por nível (`PlanAccess`) | Modo sem chave só com leitura |

## Etapas

- **A: Auditoria e segurança** (fases 1, 3 e 6): concluída nesta branch. Ver `docs/auditoria/ETAPA-A.md`.
- **Pedidos extras do usuário, antes da Etapa B** (concluídos):
  1. Atualizador: com mais de uma versão nova, instala direto a mais recente em vez de uma por uma
     (confere de novo na hora de instalar). Versões já instaladas recebem a correção na próxima atualização.
  2. Novo ícone do app, desenhado em vetor e renderizado por tamanho (`tools/Create-AppIcon.ps1`).
- **B: Motor de otimização** (fases 2, 4 e 5): concluída. `Engine/TweakCatalog.cs`, `SmartOptimizer.cs`,
  `OptimizationLab.cs`; página Smart Optimize.
- **C: Medição e diagnóstico** (fases 7 e 8): concluída. Performance Lab (PresentMon, sensores, histórico,
  comparação, CSV/JSON/PDF) e diagnóstico inteligente.
- **D: Interface** (fases 9 a 14): concluída. Command Center, barra lateral compactável, tema Meia-noite, redução de
  movimento opcional, transições de 200–250 ms, modo compacto e verificação na tela ao vivo.
- **E: Perfis** (fase 15): concluída. Perfis só com dados, importação validada, ativação por jogo.
- **F: Produto** (fases 16 a 20): concluída no que não exige infraestrutura paga. Tela de ativação com planos e
  transferência, modo demonstração, atualizações assinadas, site 2.0. Checkout e painel administrativo planejados
  em [guias/LICENCAS.md](guias/LICENCAS.md).
- **G: Qualidade** (fases 21 e 22): testes `--safety` e `--v2`, i18n das telas novas e a documentação em `docs/`.
- **Revisão independente** (Codex) das etapas B–F: 9 problemas confirmados e corrigidos.

## Decisões e pendências do dono do projeto

1. **Secret `UPDATE_SIGNING_KEY`**: a chave de releases foi criada em `private/` (fora do Git). Guarde uma cópia
   segura e cadastre o secret no GitHub antes da próxima release (o workflow falha sem ele).
2. **Servidor de licenças**: a trava do relógio e a revogação só ficam realmente fortes com um servidor (lease
   assinado). Isso é infraestrutura e precisa de autorização.
3. **ID de máquina antigo**: continua aceito para não invalidar chaves. A migração exige reemitir as chaves antigas.
4. **Certificado Authenticode** (pago): eliminaria o aviso do SmartScreen no download manual.
5. **Testes manuais** em PCs reais: aplicar pelo Smart Optimize, capturar com o PresentMon e a ativação por jogo
   (os testes automáticos não alteram o sistema).
6. **Publicação**: release 2.0.0 e deploy do site ficam por conta do dono do projeto.
