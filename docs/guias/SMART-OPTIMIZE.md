# Guia do Smart Optimize

O Smart Optimize analisa o PC, mostra quais ajustes fazem sentido para o seu objetivo e aplica só o que você
marcar. Nada é aplicado sem revisão.

## Passo a passo

1. Abra **Otimizações → Smart Optimize**.
2. Escolha o objetivo:
   - **Gaming competitivo**: frametime estável, menos processos em segundo plano, Modo de Jogo, sem gravação da Game Bar.
   - **Notebook**: temperatura, bateria e ruído. Nada que aumente o consumo vem marcado.
   - **Programação**: preserva Docker, WSL e Hyper-V; só privacidade e estabilidade. Não remove o Teams nem o OneDrive.
   - **Uso diário**: inicialização, limpeza segura e privacidade.
   - **Personalizado**: nada vem marcado.
3. Clique em **Analisar este PC**. A análise só lê o sistema (hardware, registro, serviços, apps e tarefas).
4. Revise a lista. Cada item mostra:
   - **Estado**: Recomendado, Já aplicado, Não aplicável, Incompatível, Requer revisão ou Falha na leitura.
   - **Risco**: Seguro, Moderado ou Avançado (itens avançados nunca entram no Smart Optimize).
   - **Requer reiniciar**, **Ação pontual** e **Não reversível**, quando for o caso.
   - Em **Detalhes**: efeitos colaterais, como é verificado, como reverter e a evidência do benefício.
5. Clique em **Revisar e aplicar**. Ajustes arriscados ou não reversíveis pedem uma confirmação própria.
6. O app cria um ponto de restauração, aplica com backup de cada item e, no fim, **lê o estado de novo**.
   A tela **Resultado verificado** mostra o que o Windows confirmou e o que não mudou (com o motivo).

## Conflitos

Combinações que se desfazem são bloqueadas até você escolher uma delas, por exemplo:

- "Políticas de diagnóstico, nuvem e IA" (telemetria 0) com "Só dados de diagnóstico obrigatórios" (telemetria 1).
- Trocar o plano de energia com o Modo Jogo temporário ativo (ele restauraria o plano anterior ao desligar).

Avisos do PC (não bloqueiam): Ryzen X3D com dois CCDs, virtualização em uso, objetivo Notebook num desktop.

## Planos

O Smart Optimize abre em todos os planos, mas cada ajuste respeita o nível da operação que o aplica: itens do
Debloat exigem o Intermediário e os da Versão Avançada, o Avançado/Vitalício. No modo demonstração a análise
aparece, mas nada pode ser selecionado.

## Desfazer

Tudo que tem backup aparece em **Atividade e reversão**, com **Desfazer** por ajuste. Remoções de apps
(Cortana, Widgets, apps pré-instalados) não voltam pelo backup: reinstale pela Microsoft Store.

## Medir antes de manter

Vários ajustes têm benefício pequeno ou variável (a evidência de cada um diz isso). Para decidir com dados, use o
[Optimization Lab](PERFORMANCE-LAB.md#optimization-lab).
