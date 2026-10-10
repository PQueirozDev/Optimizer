# Manual do Qrztweaks 2.0

## Primeiros passos

1. Instale com `Qrztweaks-Setup-vX.Y.Z.exe` e abra o app (ele pede permissão de administrador).
2. Na tela de ativação, cole sua chave. Sem chave, clique em **Explorar sem licença** para ver o app em modo
   demonstração (nada é aplicado). Para pedir uma chave, use **Copiar pedido** e envie no Discord.
3. Comece pelo **Command Center**: saúde do PC, alertas, último teste e ações rápidas.

## Páginas

| Página | Para quê |
|---|---|
| Command Center | Visão geral, status, alertas, monitor ao vivo e ações rápidas. |
| Performance Lab | Medir FPS, frametime e sensores; comparar testes; Optimization Lab. [Guia](guias/PERFORMANCE-LAB.md) |
| Smart Optimize | Ajustes recomendados pelo objetivo do PC, com verificação. [Guia](guias/SMART-OPTIMIZE.md) |
| Otimizações | Versão Padrão, Avançada, Debloat e manutenção, revisadas item a item. |
| Serviços, Apps, Inicialização, Drivers | Gerenciamento detalhado de cada área. |
| Personalizar Windows | Barra de tarefas, Explorador, área de trabalho, mouse e teclado. |
| Atividade e reversão | Backups, **Desfazer** por ajuste e o registro do que foi feito. |
| Diagnóstico | Diagnóstico inteligente (gargalos, memória, temperatura, disco, drivers) e verificação do Windows. |
| Pontos de restauração | Criar e gerenciar pontos de restauração. |
| Modo Jogo | Sessão temporária para jogar: fecha programas, pausa serviços e restaura tudo ao desligar. |
| Perfis | Perfis por jogo ou uso; aplicam pelo Smart Optimize e podem ligar o Modo Jogo sozinhos. |
| Rede | Velocidade, DNS, latência e reparos. |
| BIOS / UEFI e BIOS Advisor | Editor pelo SCEWIN (com cópia original) e recomendações que nunca são aplicadas sozinhas. |
| Configurações | Aparência, idioma, Command Center, licença e atualizações. |

## Atalhos

- **Ctrl+K**: busca rápida de páginas e ações.
- **Ctrl+B**: barra lateral compacta.
- **Tab/setas**: navegação pela barra lateral.

## Modo compacto

Em **Ações rápidas → Modo compacto** abre uma janela pequena com CPU, GPU, RAM, temperatura da GPU, FPS (durante uma
captura), perfil e Modo Jogo. Clique direito: sempre no topo, opacidade e fechar. É uma janela separada, não um
overlay no jogo.

## Desfazer qualquer coisa

1. **Atividade e reversão** → **Desfazer** no ajuste (ou reverter tudo da última otimização).
2. Se o app fechar no meio de uma otimização, a próxima abertura avisa e leva até aqui.
3. Último recurso: um ponto de restauração do Windows (criado antes de cada otimização).

Remoções de apps não voltam pelo backup: reinstale pela Microsoft Store.

## Aparência

Configurações → Aparência: 10 temas (inclui Meia-noite), cor principal e secundária, intensidade, densidade,
tamanho dos cards, animações (com opção de seguir a redução de movimento do Windows), janela translúcida no
Windows 11, barra lateral compacta e as seções do Command Center.

## Atualizações

O app avisa quando há uma versão nova e instala direto a mais recente. O instalador só roda se o SHA-256 conferir
e o arquivo de hashes estiver assinado pela chave do Qrztweaks. Configurações e backups são mantidos.

## Mais

- [Segurança](guias/SEGURANCA.md) · [Licenças e planos](guias/LICENCAS.md) · [Arquitetura](ARQUITETURA.md) · [Desenvolvimento](DESENVOLVIMENTO.md)
