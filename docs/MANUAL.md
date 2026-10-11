# Manual do Qrztweaks

## Primeiros passos

1. Instale com `Qrztweaks-Setup-vX.Y.Z.exe` e abra o app (ele pede permissão de administrador).
2. Na tela de ativação, cole sua chave. Sem chave, clique em **Explorar sem licença** para ver o app em modo
   demonstração (nada é aplicado). Para pedir uma chave, use **Copiar pedido** e envie no Discord.
3. Comece pelo **Início**: o estado do PC com a nota de saúde, o botão para revisar os ajustes recomendados, os
   números ao vivo (processador, memória, placa de vídeo e rede) e os atalhos.

## Barra lateral

As páginas ficam em cinco categorias, que recolhem com um clique no nome (a escolha fica salva):

| Categoria | Páginas |
|---|---|
| Principal | Início, Meu PC |
| Otimização | Otimização inteligente, Otimizações, Serviços, Aplicativos, Inicialização, Drivers, Personalizar Windows |
| Desempenho | Laboratório (de desempenho), Modo Jogo, Perfis, Rede |
| Ferramentas | Diagnóstico, Assistente de BIOS, Editor de BIOS, Restauração, Correções, Utilitários, Recursos, Histórico |
| Sistema | Configurações, Licença, Atualizações, Novidades, Sobre |

No rodapé ficam o monitor ao vivo (CPU, GPU, RAM e ping), o cartão da licença, o atalho para as Configurações e o
menu de temas. A barra superior tem o botão **Otimizar**, os alertas (sino), a busca (**Ctrl+K**) e o idioma.

## Páginas

| Página | Para quê |
|---|---|
| Início | Estado do PC, nota de saúde e indicadores (temperaturas, saúde, estabilidade, drivers), números ao vivo e atalhos. |
| Meu PC | Especificações lidas agora (processador, placa de vídeo e driver, memória, placa-mãe e BIOS, sistema, rede, armazenamento), indicadores explicados, atalhos de manutenção e **Copiar resumo** para o suporte. |
| Laboratório de desempenho | Medir FPS, tempo de quadro e sensores; comparar testes; Laboratório de otimização. [Guia](guias/PERFORMANCE-LAB.md) |
| Otimização inteligente | Ajustes recomendados pelo objetivo do PC, com verificação. [Guia](guias/SMART-OPTIMIZE.md) |
| Otimizações | Versão Padrão, Avançada, remoção de aplicativos e manutenção, revisadas item a item, e a otimização completa passo a passo. |
| Serviços, Aplicativos, Inicialização, Drivers | Gerenciamento detalhado de cada área. |
| Personalizar Windows | Barra de tarefas, Explorador, área de trabalho, mouse e teclado. |
| Histórico | Backups, **Desfazer** por ajuste e a linha do tempo do que foi feito. |
| Diagnóstico | Diagnóstico inteligente (gargalos, memória, temperatura, disco, drivers) e verificação do Windows. |
| Restauração | Criar e gerenciar pontos de restauração. |
| Modo Jogo | Sessão temporária para jogar: fecha programas, pausa serviços e restaura tudo ao desligar. |
| Perfis | Perfis por jogo ou uso; aplicam pela Otimização inteligente e podem ligar o Modo Jogo sozinhos. |
| Rede | Velocidade, DNS, latência e reparos. |
| Editor de BIOS e Assistente de BIOS | Editor pelo SCEWIN (com cópia original) e recomendações que nunca são aplicadas sozinhas. |
| Configurações | Aparência, idioma, seções do Início, licença e atualizações. |

Sensores que o Windows não informa (por exemplo, a temperatura da CPU, que exigiria um driver de kernel) aparecem
como **Indisponível**: o app nunca mostra um número estimado.

## Atalhos

- **Ctrl+K**: busca rápida de páginas e ações.
- **Ctrl+B**: barra lateral compacta.
- **Tab/setas**: navegação pela barra lateral.

## Modo compacto

No Início, o botão de alfinete (ou **Ctrl+K → Modo compacto**) abre uma janela pequena com CPU, GPU, RAM,
temperatura da GPU, FPS (durante uma captura), perfil e Modo Jogo. Clique direito: sempre no topo, opacidade e
fechar. É uma janela separada, não um overlay no jogo.

## Desfazer qualquer coisa

1. **Histórico** → **Desfazer** no ajuste (ou reverter tudo da última otimização).
2. Se o app fechar no meio de uma otimização, a próxima abertura avisa e leva até aqui.
3. Último recurso: um ponto de restauração do Windows (criado antes de cada otimização).

Remoções de aplicativos não voltam pelo backup: reinstale pela Microsoft Store.

## Aparência

Configurações → Aparência (ou o menu de temas no rodapé da barra lateral): o tema padrão é o **Azul meia-noite**
(preto, branco e azul); também há Claro, Grafite, OLED, Oceano, Floresta, Ameixa, Roxo meia-noite, Areia e
Automático. Ainda dá para escolher a cor principal e a secundária, a intensidade, a densidade, o tamanho dos cards,
as animações (com opção de seguir a redução de movimento do Windows), a janela translúcida no Windows 11, a barra
lateral compacta e as seções do Início.

## Atualizações

O app avisa quando há uma versão nova e instala direto a mais recente. O instalador só roda se o SHA-256 conferir
e o arquivo de hashes estiver assinado pela chave do Qrztweaks. Configurações e backups são mantidos.

## Mais

- [Segurança](guias/SEGURANCA.md) · [Licenças e planos](guias/LICENCAS.md) · [Arquitetura](ARQUITETURA.md) · [Desenvolvimento](DESENVOLVIMENTO.md) · [Redesign](redesign/ETAPA-A.md)
