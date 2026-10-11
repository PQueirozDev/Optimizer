# Redesign visual — desempenho antes e depois

## Método

`tools/Measure-UiPerformance.ps1` abre o app em Release no modo de medição (sem administrador, fecha sozinho):

- **abertura**: do início do processo até o Início utilizável (5 execuções; a primeira é "fria", as outras dão a
  mediana);
- **app parado no Início** (30 s, monitor ao vivo ligado) e **em Configurações** (15 s): CPU média do processo,
  uso do motor 3D da placa de vídeo pelo próprio app (contadores "GPU Engine" do Windows) e memória;
- **troca de página**: do clique até o quadro seguinte desenhado, nas 23 páginas.

Mesmo PC (Ryzen 7 7735HS, Radeon integrada, 16 GB), mesmas preferências salvas do usuário (tema Oceano,
Vibrante, janela translúcida, densidade Confortável, cards Grandes). O "antes" é a v2.0.0 compilada de uma cópia
separada do código (commit `2526328`) com o mesmo medidor.

### Correção do medidor

A primeira versão do medidor contava quadros por segundo assinando o evento de renderização do WPF. Essa assinatura
obriga a janela a redesenhar a cada quadro (~60/s), o que inflava a CPU medida nas duas versões e mais ainda na
que tem mais elementos na tela. Os números dessa primeira rodada (0,61% → 1,52% de CPU parada e 54 → 61 quadros/s,
registrados em `ETAPA-A.md`) não valem para comparar. O medidor atual não assina nada de renderização e mede o uso
real da GPU pelo processo.

## Resultado

| Medida | Antes (v2.0.0) | Depois (redesign) |
|---|---|---|
| Abertura, 1ª execução | 2.634 ms | 2.256–2.261 ms |
| Abertura, mediana das seguintes | 1.460 ms | 1.348–1.428 ms |
| CPU parado no Início (monitor ao vivo) | 0,35 % | 0,38–0,42 % |
| GPU (motor 3D) do app parado no Início | 0,10 % | 0,09–0,11 % |
| Memória no Início (working set / privada) | 219 / 166 MB | 226–229 / 173–176 MB |
| Heap gerenciado no Início | 11 MB | 8 MB |
| Troca de página (mediana / pior) | 63 / 158 ms | 60–63 / 131–149 ms |
| CPU parado em Configurações | 0,10 % | 0,13–0,17 % |
| Working set depois de abrir todas as páginas | 280 MB | 281 MB |

Duas rodadas do "depois" (a variação entre execuções fica em torno de ±80 ms na abertura).

**Leitura:** a abertura ficou igual ou um pouco mais rápida; CPU e GPU paradas praticamente iguais (o Início
atualiza quatro curvas e quatro números por segundo, só quando está na tela); memória ~7–10 MB maior (ícones,
pincéis e as fontes de texto novas); troca de página igual. Nada anima com a tela parada: as animações são só de
entrada (até 220 ms) e de hover, e a barra de progresso indeterminada escondida não anima mais.
