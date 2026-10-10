# Etapa A — Auditoria técnica, classificação de risco e reversão

Branch: `dev/qrztweaks-2.0` · Base: `main` em `7e0139e` (v1.10.4) · Data: 10/10/2026

Escopo das fases 1, 3 e 6 do plano Qrztweaks 2.0. Esta etapa corrige os problemas de segurança e de
reversão encontrados na auditoria e não muda a interface além das mensagens novas.

## Como a auditoria foi feita

1. Leitura do código de licença, atualizador, ponte com o PowerShell, Defender, BIOS/SCEWIN, backups de
   registro e de serviços, instalação limpa de driver e do script `Otimizador_de_PC.ps1`.
2. Uma auditoria independente feita pelo Codex (somente leitura), com 19 apontamentos. **Cada apontamento foi
   conferido no código** antes de entrar aqui; a tabela abaixo diz o que foi corrigido e o que ficou pendente.
3. Testes de linha de base antes de qualquer mudança: PowerShell OK, verificação do app com 264 PASS e 0 falhas.

## O que o projeto já fazia bem

A base é sólida, e a 2.0 deve ser construída em cima dela, sem reescrever nada:

- **Atualizador**: só aceita URLs de release do próprio repositório, baixa numa pasta de ProgramData restrita a
  Administradores/SYSTEM, recusa reparse points e calcula o SHA-256 no mesmo handle que fica travado até a execução.
- **Licença**: RSA-SHA256, vínculo ao `MachineGuid`, lista de revogação assinada e trava contra atrasar o relógio.
- **Backup do script**: snapshot gravado antes de cada item, de forma atômica (temporário + troca), numa pasta com
  ACL só de administradores; arquivos de outros donos vão para quarentena; a reversão só restaura chaves, serviços
  e tarefas de listas permitidas.
- **Ajustes de registro em C#** (`RegistryTweakStore`): backup antes de gravar, estado Pendente/Aplicado/Parcial,
  reversão filtrada por lista permitida.
- **Modo Jogo**: recuperação parcial com checkpoint, testada com falhas simuladas (`RecoveryTests`).
- **BIOS**: o Advisor nunca grava na BIOS; o editor só grava com o SCEWIN escolhido pelo usuário.
- Comandos com valores externos já usam variáveis de ambiente em vez de texto interpolado.

## Problemas encontrados e situação

| # | Severidade | Problema | Situação |
|---|---|---|---|
| 1 | Alta | Desinstalação de programas rodava o `UninstallString` com `cmd /c`. Entradas de HKCU podem ser escritas sem administrador; um `&` ali encadearia outro comando elevado. | **Corrigido**: executável e argumentos são separados e o desinstalador roda direto, sem shell. Executável inexistente é recusado. |
| 2 | Alta | `powershell.exe`, `sc.exe`, `powercfg.exe` etc. eram iniciados pelo nome. O Windows procura primeiro na pasta do app; numa cópia portátil em pasta gravável, um executável plantado rodaria elevado. | **Corrigido**: `SystemTools` resolve as ferramentas em System32; a ponte usa o caminho completo do PowerShell. |
| 3 | Alta | Exclusões do Defender aceitavam qualquer pasta, inclusive `C:\`, o Windows ou a pasta do usuário, e a tela dizia que valiam "enquanto o jogo carrega", mas são permanentes. | **Corrigido**: unidades, Windows, Downloads, Temp, pastas de rede e pastas amplas (Program Files, perfil, AppData, Documentos) são recusadas; o texto agora diz que a exclusão vale até ser removida. |
| 4 | Alta | Pastas de dados elevados (backups, BIOS, drivers) só eram protegidas ao serem criadas. Uma pasta criada antes por outra conta, ou um junction, seria usada como estava, e a instalação limpa de driver executa um script dessa pasta no logon. | **Corrigido**: `EnsureProtectedDirectory` recusa links, passa a pasta para os Administradores e reaplica a ACL sempre. A instalação limpa apaga e recria seus arquivos em vez de sobrescrever. |
| 5 | Alta | Export do SCEWIN com erro podia virar a "cópia original" da BIOS; a restauração só conferia se o arquivo existia, mesmo depois de uma atualização de BIOS. | **Corrigido**: export precisa terminar com código 0 e ter configurações; a cópia original é gravada de forma atômica; a restauração confere se a cópia é válida e se o `HIICrc32` (identificação da BIOS) é o mesmo da BIOS atual. |
| 6 | Alta | Valores numéricos do editor de BIOS iam para o SCEWIN sem validação. | **Corrigido**: formato e tamanho do campo (`Width`) são conferidos antes de gerar o arquivo; nada é gravado se algum valor for inválido. |
| 7 | Alta | O SCEWIN era executado direto da pasta escolhida (ex.: Downloads), que pode ser trocada entre a escolha e a próxima leitura ou gravação. | **Corrigido**: a pasta do SCEWIN é copiada para a pasta protegida e só essa cópia é executada. Quem já tinha configurado é migrado sozinho. |
| 8 | Alta | Licença com validade, ativada sem relógio salvo (primeira ativação offline), quebrava a validação: `DateTime.MinValue - 1 dia` gera exceção. | **Corrigido** e coberto por teste. |
| 9 | Alta | O atualizador confere o SHA-256, mas o hash vem da mesma release que o instalador: prova integridade, não autoria. Não há verificação Authenticode. | **Pendente, precisa de decisão** (ver abaixo). |
| 10 | Média | Backup dos grupos de serviços era gravado sobrescrevendo o arquivo, e um arquivo ilegível era tratado como vazio, então o próximo "desligar" gravaria o estado já desativado como original. | **Corrigido**: gravação atômica; backup ilegível bloqueia a operação com mensagem clara. |
| 11 | Média | Instalação limpa de driver desligava a busca de drivers do Windows Update antes de gravar o script e o RunOnce; uma exceção nesses passos deixava a busca desligada. | **Corrigido**: qualquer falha antes do DDU reiniciar o PC desfaz a trava e o agendamento. |
| 12 | Média | Iniciar um serviço desativado mudava o tipo de início antes de saber se ele iniciaria. | **Corrigido**: se o serviço não iniciar, volta a ficar desativado. |
| 13 | Média | Plano Qrz ignorava em silêncio configurações que falhavam. | **Corrigido**: as que não existem no hardware aparecem como aviso no registro. O plano tem GUID próprio do app, então recriá-lo não apaga nada do usuário. |
| 14 | Média | Modo console: `Remove-Item "$env:TEMP\*" -Recurse` confiava no ambiente. | **Corrigido**: só limpa se a pasta for mesmo uma pasta Temp (nunca raiz, link ou outra pasta). O app não usa esse caminho. |
| 15 | Média | Trava do relógio guardada em dois lugares editáveis pelo usuário; revogação pode voltar para uma lista antiga se o cache for trocado e a rede bloqueada. | **Pendente**: resolver de verdade exige um servidor de licenças (lease assinado). Fica para a Etapa F, sem infraestrutura paga até haver autorização. |
| 16 | Média | O ID antigo da máquina (nome do PC + usuário) continua aceito. | **Mantido de propósito** (regra 4: não invalidar chaves antigas). Plano de migração na Etapa F. |
| 17 | Média | Se a criação do ponto de restauração passar de 5 minutos, o processo é encerrado e a frequência de pontos fica em 0. | **Pendente** (baixo impacto: só afeta o limite de um ponto por dia). |
| 18 | Baixa | Ferramenta que travava em `RunToolOutput` ficava rodando. | **Corrigido**: é encerrada no tempo limite. |
| 19 | — | Desinstaladores de entradas HKCU continuam rodando elevados (o app inteiro é elevado). | **Reduzido** com o item 1; rodar sem elevação exige um processo auxiliar e fica para depois. |

## Classificação de risco (fase 3)

- O app já tinha `StepRisk` (Seguro/Moderado/Arriscado) e `StepEffect` (com backup, pontual, não reversível),
  lidos de marcadores no script. **Problema**: etapa sem marcador aparecia como "Seguro", e 32 etapas não tinham marcador.
- **Agora**: todas as 51 etapas das otimizações Padrão, Avançada, Inteligente e Debloat declaram o risco. Remover
  Cortana e Widgets passou a Moderado (remoção que a reversão não desfaz). Etapa sem marcador conta como
  Moderada, e um teste falha se alguma etapa nova não declarar o risco.
- **Confirmação específica**: aplicar ajustes Arriscados ou Não reversíveis (inclusive por "Selecionar todos")
  abre uma confirmação que lista exatamente esses ajustes.
- Os metadados completos da fase 3 (reinicialização, verificação, método de reversão, evidência) ficam para a
  Etapa B, junto do Smart Optimize, que é quem vai usá-los.

## Recuperação de operações interrompidas (fase 6)

O Modo Jogo já tinha recuperação, mas as otimizações do script não. Se o app fechasse no meio (queda,
falta de energia, Gerenciador de Tarefas), o backup existia, mas ninguém avisava o usuário.

- `OperationJournal` grava um registro antes de cada otimização que muda o sistema e o apaga quando o script termina.
- Na abertura seguinte, se o registro sobrou e o processo dono não existe mais (confere número **e** hora de
  início do processo), o app avisa e oferece abrir **Atividade e reversão**.
- O registro só é exibido, nunca executado. Testado com queda simulada, registro corrompido e operação do
  próprio processo em andamento.
- Ponto de restauração do Windows continua sendo complemento, não garantia: a reversão principal é o backup
  item a item.

## Decisão pendente: autenticidade das atualizações

Hoje, quem conseguir publicar uma release no repositório (token vazado, conta comprometida) consegue publicar
um instalador e o hash dele. Opções:

1. **Assinar o `SHA256SUMS.txt` com uma chave RSA própria** (recomendado). Reaproveita o padrão já usado no banco
   do BIOS Advisor. Custo zero. Exige criar uma chave dedicada, guardar a privada fora do Git e cadastrá-la como
   secret no GitHub (`UPDATE_SIGNING_KEY`). O app passa a recusar atualização sem assinatura válida.
2. **Certificado Authenticode** para assinar o instalador. Resolve também o aviso do SmartScreen, mas é pago.
3. Manter como está.

A opção 1 muda o processo de release (secret novo e um passo no workflow), então não foi aplicada sem autorização.

## Testes executados

| Teste | Antes | Depois |
|---|---|---|
| `tests/Verify-PowerShell.ps1` | OK | OK |
| `dotnet run --project tests/Optimizer.Verification` | 264 PASS, 0 falhas | **302 PASS, 0 falhas** (38 novos em `SafetyTests.cs`) |
| `--branding` | — | OK |
| `--i18n` | 25 textos possivelmente em português | 25, os mesmos (nenhum novo; os existentes são patch notes antigas e uma combinação de textos na tela de revisão) |
| `dotnet build -c Release` | — | 0 avisos, 0 erros |

Pulados como antes: teste de serviço (exige terminal como administrador; roda no GitHub Actions) e NVIDIA (sem GPU NVIDIA nesta máquina).

Novo atalho: `dotnet run --project tests/Optimizer.Verification -- <pasta> --safety` roda só os testes desta etapa.
