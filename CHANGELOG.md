# Changelog

As notas completas de cada versão aparecem no app (Novidades) e nas releases do GitHub.

## Redesign visual (em desenvolvimento, branch `dev/redesign-ui`)

### Novo visual
- Tema padrão **Azul meia-noite**: fundo preto, texto branco e azul como destaque; botões principais claros.
  Claro, Grafite, Roxo meia-noite (a paleta roxa da 2.0) e os outros temas continuam; quem escolheu outro tema
  mantém a escolha e vê uma vez o convite para o tema novo.
- **Barra lateral** em categorias recolhíveis (Principal, Otimização, Desempenho, Ferramentas, Sistema), cartão da
  licença com menu de temas, monitor ao vivo compacto e modo só com ícones.
- **Barra superior** com Otimizar, alertas (sino), busca Ctrl+K e idioma; o estado da operação e o aviso de
  administrador só aparecem quando informam algo.
- **Início** limpo: estado do PC com nota de saúde e indicadores reais (temperatura da GPU pelo driver NVIDIA,
  estabilidade pelo registro de eventos, dispositivos com erro), quatro números ao vivo com curva do último
  minuto, atalhos e atividade recente.
- Nova página **Meu PC**: especificações lidas na hora (processador, placa de vídeo e driver, memória, placa-mãe e
  BIOS, sistema, rede, armazenamento), indicadores explicados e **Copiar resumo** para o suporte.
- Gráficos com curva suave, área em gradiente e valor ao passar o mouse; medidor circular; grade responsiva
  (4, 2 ou 1 coluna); histórico em linha do tempo; abas segmentadas; animações de 120 a 220 ms.
- Ícone do app, favicon e arte do instalador em azul.

### Idioma
- Com o app em português não aparecem mais nomes em inglês: Otimização inteligente, Laboratório de desempenho,
  Laboratório de otimização, Assistente de BIOS, Novidades, Histórico, Aplicativos, Jogos competitivos.
  O modo inglês continua com Smart Optimize, Performance Lab etc.

### Correções
- Modo demonstração: Início, Meu PC, Configurações, Novidades e Sobre abrem (antes mostravam "recurso bloqueado").
- Botões principais com texto definido por string ficavam com o texto na cor errada em temas claros.
- A barra de progresso indeterminada escondida não anima mais.

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
