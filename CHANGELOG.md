# Changelog

As notas completas de cada versão aparecem no app (Patch notes) e nas releases do GitHub.

## 2.0.0 (em desenvolvimento, branch `dev/qrztweaks-2.0`)

### Novo
- **Smart Optimize**: análise do PC, estado real de cada ajuste (recomendado, já aplicado, não aplicável,
  incompatível, requer revisão, falha na leitura), recomendações por objetivo (gaming competitivo, notebook,
  programação, uso diário, personalizado), conflitos bloqueantes e verificação no Windows depois de aplicar.
- **Catálogo de ajustes** com risco, reinício, efeitos colaterais, verificação, reversão e evidência de cada etapa.
- **Performance Lab**: FPS médio, mínimo, 1% low, 0,1% low e frametime P95/P99 pelo PresentMon; sensores por
  segundo; gráficos ao vivo; histórico; comparação antes/depois; exportação CSV, JSON e PDF.
- **Optimization Lab**: experimento com um ajuste por vez, teste t de Welch e limiar prático de 3%.
- **Diagnóstico inteligente** com evidências, confiança, causas, como confirmar e riscos.
- **Command Center**, barra lateral compactável (Ctrl+B), tema Meia-noite, modo compacto para segundo monitor.
- **Perfis personalizados** com importação validada e ativação do Modo Jogo ao abrir o jogo.
- **Modo demonstração** (só leitura) e **pedido de transferência** na tela de ativação.
- Site: seções Smart Optimize, Performance Lab e Segurança; versão e patch notes vindas das releases.

### Segurança e confiabilidade (Etapa A e revisão)
- Atualizações exigem `SHA256SUMS.txt` assinado com a chave de releases; atualização interrompida é avisada.
- Ferramentas do Windows sempre por System32; desinstaladores sem `cmd`; exclusões amplas do Defender recusadas.
- Pastas de dados elevados reprotegidas a cada uso; SCEWIN e PresentMon executados só de cópias confiáveis.
- BIOS: export validado, valores numéricos conferidos pelo tamanho do campo, restauração bloqueada se a BIOS mudou.
- Toda etapa declara o risco; arriscados e não reversíveis pedem confirmação própria.
- Registro de operação interrompida, com aviso na abertura seguinte.
- Atualizador instala direto a versão mais recente (antes passava por cada versão intermediária).
- Ícone redesenhado em vetor, nítido em 16 px.

### Corrigido
- Primeira ativação offline de licença com validade quebrava a validação.
- Backup de grupos de serviços podia ser perdido numa queda; serviço que não iniciava ficava reativado.
- Instalação limpa de driver deixava a busca de drivers desligada se algo falhasse antes do DDU.

## 1.10.4 e anteriores

Veja as [releases no GitHub](https://github.com/PQueirozDev/Optimizer/releases).
