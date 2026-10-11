# Redesign visual — Etapa A: auditoria e arquitetura do design system

Branch: `dev/redesign-ui`. Nada de release ou deploy sem autorização do dono do projeto.

## 1. Direção visual (decisões do dono do projeto)

- **Referência:** `imagem.jpg` (painel escuro de otimizador de PC: cards de estatística com número grande,
  selo de folga, linhas de detalhe e gráfico "ao vivo" com curva suave; barra lateral por seções; barra superior
  com botão "Otimizar", busca com Ctrl K e Configurações; card de destaque com dois botões; biblioteca de jogos).
  Do prompt original continuam valendo o conteúdo pedido (card Meu PC, medidores, acesso rápido, configurações
  rápidas, categorias da barra lateral) e todas as regras (nada inventado, "Indisponível" sem sensor, nenhuma
  função removida).
- **Cores:** azul, branco e preto. Fundo preto, texto branco, azul como destaque (gráficos, seleção, selos,
  indicadores) e botões principais brancos/azulados, como os botões claros da referência. Verde, amarelo e
  vermelho ficam só para estado (saudável/atenção/crítico).
- **Ícone do app:** azul, independente do tema.
- **Idioma:** com o app em português não aparece palavra em inglês na interface (nomes como "Smart Optimize",
  "Performance Lab", "Command Center" e "Patch notes" ganham nome em português; o inglês fica para o modo EN).

## 2. Inventário

**Janela** (`MainWindow.xaml`): barra lateral de 248 px com logo, 5 seções fixas e 25 itens, monitor ao vivo no
rodapé; barra superior com busca larga, chips de estado, idioma, tema e botões da janela; cabeçalho da página com
ícone grande, seção, título e subtítulo; área rolável; barra de status em card no rodapé.

**Páginas** (todas montadas em código, `Pages/` e `MainWindow.*.cs`): Command Center, Performance Lab (4 abas),
Smart Optimize, Otimizações (+ revisão e execução ao vivo), Serviços (2 abas), Apps, Inicialização, Drivers
(+ instalação limpa), Imagens do Windows, Ferramentas, Personalizar Windows, Atividade e reversão, Recursos,
Correções, Central de diagnóstico, Pontos de restauração, Modo Jogo (4 abas), Perfis (+ editor), Rede,
Configurações, BIOS / UEFI, BIOS Advisor (+ guia), Patch notes, Sobre, tela de plano bloqueado.
Janelas e camadas: ativação, seletor de modo de energia, modo compacto, atualização, tutorial, splash.

**Componentes atuais** (`MainWindow.Ui.cs` e outros): `Surface`, `Card`, `IconChip`, `OutlineChip`, `Pill`,
`IconButton`, `Primary`, `ScoreRing`, `SectionHeader`, `Label`/`TypeScale`, `Tabs`, `Segmented`, `SettingRow`,
`Switch`, `MetricCard`/`Sparkline`, `LiveTileView`/`DrawSparkline`, `LineChart`, `Responsive` (UniformGrid);
estilos em `App.xaml` (botões, navegação, interruptor, caixa de seleção, barra de progresso, campos, listas).

## 3. Problemas encontrados

1. **Identidade:** roxo em tudo (destaque, gráficos, anel, seleção); não há a hierarquia da referência (valor
   grande colorido, selo, detalhes, gráfico em caixa interna).
2. **Inconsistência:** seções de Configurações montadas à mão (raio 12, `BorderBrush`, título 18 negrito) ao lado
   de `Surface` (raio 14, `BorderSubtleBrush`, título 18 semibold); três tipos de "card de métrica" diferentes
   (Card, LiveTileView, MetricCard); botões neutros que ganham borda roxa ao passar o mouse em qualquer contexto.
3. **Cabeçalho pesado:** chip de ícone de 44 px + seção + título + subtítulo ocupam ~110 px antes do conteúdo;
   a barra de status em card ocupa mais ~70 px no rodapé.
4. **Gráficos:** linhas retas, sem valor ao passar o mouse, grade visível.
5. **Barra lateral:** seções fixas (não recolhem), nomes longos, "Chave de administrador" como item de menu.
6. **Desempenho:** a interface desenha ~54 quadros/s parada (medido), porque a barra de progresso
   indeterminada da barra de status anima mesmo escondida.
7. **Idioma:** 50 textos com termos em inglês no modo português (auditoria `--ui --pt-audit`), incluindo
   nomes de páginas e mensagens do registro gravadas em inglês pelo BIOS Advisor.

## 4. Medição antes do redesign

Método: `tools/Measure-UiPerformance.ps1` (modo de medição do próprio app, Release, sem administrador,
preferências salvas deste PC: tema Oceano, Vibrante, translúcido, densidade Confortável, cards Grandes).

| Medida | Antes |
|---|---|
| Abertura (1ª execução / mediana das seguintes) | 2.629 ms / 1.471 ms |
| CPU parado na Visão geral (monitor ao vivo) | 0,61 % |
| Quadros/s parado na Visão geral / em Configurações | 53,7 / 55,2 |
| Memória (working set / privada / heap gerenciado) | 229 / 179 / 14 MB |
| Troca de página (mediana / pior) | 67 / 133 ms |
| Working set depois de abrir todas as páginas | 280 MB |

## 5. Arquitetura do design system

- **Tokens de cor** continuam num lugar só (`ThemeService`), como recursos dinâmicos. Novos tokens:
  `CardInsetBrush` (caixa interna dos gráficos), `CardElevatedBrush`, `PrimaryFillBrush`/`OnPrimaryBrush`
  (botão principal claro), `AccentTextBrush` (azul de texto e números), `ChartLineBrush`, `ChartFillBrush`,
  `ChartGridBrush`, `GlowBrush` (brilho azul discreto atrás do conteúdo), `HeroBrush` (gradiente sutil).
- **Temas:** "Azul meia-noite" é o novo padrão (o valor `Dark` salvo continua valendo, então quem usava o
  padrão recebe o visual novo); "Roxo meia-noite" guarda a paleta roxa da 2.0; Grafite, Claro (branco e azul) e
  os demais continuam. Quem escolheu outro tema mantém a escolha e vê uma vez o convite para o tema novo.
- **ResourceDictionaries:** `Themes/Controls.xaml` (botões, navegação, interruptor, campos, listas, menus,
  dicas, rolagem) e `Themes/Typography.xaml` (estilos de texto). `App.xaml` só junta os dicionários e define
  os valores iniciais.
- **Componentes** (`Controls/`, prefixo `Qrz`): `QrzGrid` (grade responsiva com colunas por largura e itens que
  ocupam várias colunas), `QrzGauge` (medidor circular), `LineChart` (curva suave, preenchimento em gradiente,
  ponto final e valor ao passar o mouse), `QrzArt` (grafismos abstratos estáticos dos cards). As funções de
  `MainWindow.Ui.cs` (`Surface`, `Pill`, `IconChip`, `IconButton`, `SectionHeader`, `Label`...) viram a API do
  design system e passam a desenhar o visual novo em todas as páginas de uma vez; as páginas que montavam
  cards à mão passam a usar essas funções.
- **Tipografia:** Segoe UI Variable (Display nos títulos e números, Text no resto); escala 11 · 12 · 13 · 14 ·
  16 · 18 · 22 · 28; título da página 28, título de card 16, valores 26, corpo 13, secundário 12.
- **Movimento:** 120–220 ms (hover, transição de página, interruptor, barra lateral); nada animando parado;
  respeita a opção de animações e a redução de movimento do Windows.
- **Responsivo:** 1280×720 DIP (1920×1080 a 150%, 2560×1440 a 200%) até ultrawide; a grade da Visão geral vai de
  4 para 2 e 1 coluna; a barra lateral recolhe abaixo de 1000 px.

## 6. Próximas etapas

B base visual → C barra lateral e janela → D Visão geral (Início) e Meu PC → E demais telas → F refinamento →
G testes (compilação, navegação, capturas em várias resoluções, i18n PT/EN, medição depois), com relatório por
etapa em `docs/redesign/`.
