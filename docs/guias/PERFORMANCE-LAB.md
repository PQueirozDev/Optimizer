# Guia do Performance Lab

Mede FPS, frametime e sensores enquanto você joga, guarda o histórico e compara testes.

## O que é medido

| Métrica | Fonte |
|---|---|
| FPS médio, FPS mínimo (pior segundo), 1% low, 0,1% low, frametime médio, P95 e P99 | PresentMon: tempo real entre os quadros apresentados |
| Uso de CPU e GPU, memória | Contadores do Windows (monitor ao vivo) |
| Clock da CPU | Estimado pelo Windows (% de desempenho × clock base) |
| Temperatura, clock e potência da GPU, motivos de redução de clock | `nvidia-smi` (só placas NVIDIA) |
| Limite de desempenho da CPU | Contador "% Performance Limit" do Windows |

**Nada é estimado.** Sem o PresentMon, a captura grava só os sensores e não mostra FPS. A temperatura da CPU
fica indisponível porque exige um driver de kernel que o Qrztweaks não instala.

## Configurar o PresentMon

1. Baixe o **PresentMon 2.x** no GitHub oficial (botão na aba Captura): <https://github.com/GameTechDev/PresentMon/releases>.
2. Clique em **Escolher PresentMon** e selecione o `.exe`. O app copia o arquivo para uma pasta protegida e só
   executa essa cópia.

## Fazer uma captura

1. Abra o jogo.
2. Na aba **Captura**, escolha o processo (ex.: `cs2.exe`), a duração e, se quiser, um nome.
3. Clique em **Iniciar captura** e volte ao jogo. A captura continua em segundo plano e para sozinha.
4. O resultado é salvo no **Histórico** e pode ser exportado em CSV, JSON ou PDF (no PDF, escolha
   "Microsoft Print to PDF" na janela de impressão).

Também dá para **importar** logs do PresentMon ou do CapFrameX.

## Comparar

Na aba **Comparar**, escolha o teste de antes e o de depois. A tabela mostra a diferença absoluta e percentual,
indica melhor/pior em texto e avisa quando hardware, driver, Windows, resolução, taxa de atualização ou plano de
energia mudaram. Uma gravação de cada lado não prova ganho: use o Optimization Lab.

## Optimization Lab

Mede o efeito de **um** ajuste reversível:

1. Escolha o ajuste, o processo e a duração e clique em **Começar experimento**.
2. **Gravar referência** pelo menos 3 vezes, repetindo a mesma cena com as mesmas configurações.
3. **Aplicar o ajuste** (com backup). Se ele exigir reinício, reinicie e continue.
4. **Gravar depois** pelo menos 3 vezes.
5. O resultado usa o teste t de Welch: só é **Provável ganho** (ou perda) com p &lt; 0,05 **e** diferença de pelo
   menos 3%. O resto é **Sem diferença comprovada**.
6. **Manter** ou **Reverter o ajuste**.

Avisos aparecem quando a variação entre gravações passa de 5%, a temperatura da GPU muda mais de 5 °C, o processo,
a resolução ou o driver mudam, o uso de CPU difere muito (outro programa interferindo) ou o Windows informou limite
de desempenho.

## Boas práticas

- PC aquecido (jogue alguns minutos antes da primeira gravação).
- Mesma cena, mesma duração, mesmas configurações do jogo.
- Feche programas em segundo plano ou use o Modo Jogo nas duas fases.
