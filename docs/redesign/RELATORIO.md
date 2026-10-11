# Redesign visual — relatório por etapa

Branch `dev/redesign-ui` (a partir da v2.0.0). Nada foi publicado: sem release, sem merge no `main`, sem deploy.
Direção definida pelo dono do projeto durante o trabalho: referência `imagem.jpg` (painel escuro de otimizador),
cores **azul, branco e preto**, ícone **azul** e nenhuma palavra em inglês com o app em português. Depois da
primeira versão do Início, o pedido foi um visual mais bonito, sem "cara de IA", e um Início mais limpo.

## Etapa A — auditoria e arquitetura

Documento: [ETAPA-A.md](ETAPA-A.md). Inventário das telas e componentes, problemas encontrados (roxo em tudo,
cards inconsistentes, cabeçalho pesado, gráficos retos, nomes em inglês) e a arquitetura do design system.
Medição de antes com `tools/Measure-UiPerformance.ps1`.

## Etapa B — base visual

- **Tokens** no `ThemeService`: paleta Azul meia-noite (padrão, valor `Dark` salvo continua valendo), destaque azul
  em todas as intensidades, botão principal claro, secundário azul translúcido, caixa interna dos gráficos,
  card elevado, cores de gráfico, brilho de fundo estático. Roxo meia-noite guarda a paleta roxa da 2.0.
- **ResourceDictionaries**: `Themes/Tokens.xaml` (fontes e escala), `Themes/Typography.xaml` (estilos de texto) e
  `Themes/Controls.xaml` (botões, pílula, botão redondo, navegação, categorias, interruptor, caixa de seleção,
  progresso, campos, listas, menus, dicas). `App.xaml` só junta os dicionários.
- **Componentes** (`Controls/`): `QrzGrid` (grade responsiva com colunas permitidas e itens de várias colunas),
  `QrzGauge` (medidor circular), `LineChart` (curva monotônica sem picos inventados, área em gradiente, ponto final,
  valor ao passar o mouse) e `QrzUi` (raio dos botões). As funções de `MainWindow.Ui.cs` (`Surface`, `Pill`,
  `IconChip`, `IconButton`, `SecondaryButton`, `ChevronButton`, `DetailRow`, `StatusDot`, `EmptyState`,
  `SectionHeader`) são a API visual das páginas.
- **Movimento**: interruptor, botões e transições entre 120 e 220 ms; a barra de progresso indeterminada só anima
  visível.

## Etapa C — barra lateral e janela

- Categorias recolhíveis (estado salvo), item ativo com fundo neutro, barrinha e ícone azuis, foco de teclado
  visível, modo compacto (72 px) com nomes nas dicas, automático abaixo de 1000 px.
- Rodapé: monitor ao vivo compacto, cartão da licença (iniciais, titular, plano), Configurações e menu de temas.
- Barra superior: Otimizar, sino de alertas (avisos reais), busca Ctrl+K, idioma e controles da janela; estado da
  operação e aviso de administrador só quando informam algo.
- Cabeçalho leve (categoria, título, subtítulo, ações da página) e barra de status fina.
- Novos itens: Meu PC, Licença (abre Minha licença; sem chave, a ativação) e Atualizações.

## Etapa D — Início e Meu PC

- **Início** (versão limpa): destaque com saudação, estado do PC, ação principal (revisar os ajustes recomendados),
  nota de saúde e os indicadores temperaturas/saúde/estabilidade/drivers; quatro números ao vivo (processador,
  memória, placa de vídeo, rede) com a curva do último minuto; quatro atalhos; atividade recente.
- **Meu PC**: visão geral, indicadores com a explicação de cada um, especificações completas e manutenção.
- Leituras novas, todas reais: frequência atual da CPU e processos (contadores do Windows), temperatura e memória
  da GPU NVIDIA (NVML do driver), VRAM pelos contadores do Windows nas outras placas, estabilidade pelo registro de
  eventos (desligamentos inesperados e telas azuis em 7 dias), dispositivos com erro, placa-mãe e BIOS, tipo do
  disco. Sem sensor: **Indisponível**.
- O que saiu do Início continua no app: armazenamento e rede detalhada no Meu PC, jogos no Modo Jogo, otimização
  completa passo a passo em Otimizações, configurações rápidas em Configurações, modo compacto no botão de alfinete.

## Etapa E — demais telas e idioma

- Todas as páginas usam os componentes (abas segmentadas no Laboratório, cards padrão em Configurações, histórico
  em linha do tempo, ícones vetoriais no lugar de emojis, texto dos botões seguindo a cor do botão).
- Nomes em português no modo português, com as mesmas regras nas chaves de tradução; linhas antigas do registro em
  inglês aparecem em português; o perfil pronto "Modo Debloat & Privacidade" mantém o nome salvo e ganha um nome de
  exibição em português.

## Etapa F — refinamento

- Visual mais calmo: ícones sem caixa com borda, linhas de detalhe sem ícone, selos sem contorno, sem grafismos
  abstratos, menos textos explicativos repetidos.
- Responsivo: 4/2/1 colunas (nunca 3 + 1), barra lateral compacta abaixo de 1000 px, busca menor abaixo de 900 px,
  conteúdo limitado a 1880 px em telas ultrawide.
- Ícone, favicons e arte do instalador azuis.

## Etapa G — testes

- Compilação sem erros nem avisos.
- `tests/Optimizer.Verification`: 402 verificações passando (segurança, 2.0, BIOS Advisor, PowerShell, telas).
- Varredura do inglês (`--i18n`): nenhum texto em português em nenhuma tela, tutorial ou diálogo.
- Auditoria do português (`--ui --pt-audit`): sobram só termos aceitos em português ("backup", "online", "barra de
  status") e nomes de recursos do Windows.
- Capturas reais da interface (`--ui`, `--tall`) em 980×680, 1280×720 (equivale a 1920×1080 a 150% e 2560×1440 a
  200%), 1366×728, 1600×900, 1920×1040 e no tema Claro. Telas maiores que o monitor desta máquina não podem ser
  abertas pelo Windows, então o layout ultrawide foi conferido pelo limite de largura do conteúdo.
- Revisão independente (Codex): 4 problemas apontados e conferidos; os dois que valiam para o código final foram
  corrigidos (GPU certa em PCs híbridos e botões duplicados ao atualizar o Meu PC); os outros dois sumiram com a
  versão limpa do Início.

## Desempenho

Ver [DESEMPENHO.md](DESEMPENHO.md).

## Pendências

- Testar no PC real com placa NVIDIA (temperatura e memória pela NVML) e em notebook híbrido.
- Conferir à mão a escala de 125%, 150% e 200% num monitor de verdade.
- Publicação: depende de autorização do dono do projeto (versão, release e site).
