# Guia de segurança

O Qrztweaks roda como administrador. Este guia resume o que o protege e o que o usuário controla.

## Princípios

1. **Nada sem revisão.** Toda otimização é escolhida item a item. Arriscados e não reversíveis pedem confirmação
   própria, com a lista exata.
2. **Backup antes de mudar.** O valor original de cada registro, serviço, tarefa e plano de energia é gravado antes
   da mudança, numa pasta só de administradores. O ponto de restauração do Windows é complemento, não garantia.
3. **Verificar depois.** O Smart Optimize e a tela ao vivo leem o estado de novo e só marcam como concluído o que o
   Windows confirma.
4. **Recuperação.** Se o app fechar no meio de uma otimização, a próxima abertura avisa e leva à reversão. O mesmo
   vale para o Modo Jogo e para uma atualização que não terminou.

## O que o app nunca faz sozinho

- Gravar na BIOS. O BIOS Advisor só recomenda; o editor grava apenas o que você escolher, com cópia original
  validada, valores conferidos pelo tamanho do campo e bloqueio se a versão da BIOS mudou (HIICrc32).
- Desligar o Windows Defender. Exclusões amplas (unidade inteira, pasta do Windows, Downloads, Temp, pastas de rede,
  Program Files ou o perfil inteiro) são recusadas, e a tela deixa claro que a exclusão vale até ser removida.
- Injetar código em jogos. Modo compacto e ativação por jogo só leem a lista de processos.
- Executar scripts vindos de perfis: perfis são só dados e passam por validação ao importar.

## Proteções contra adulteração

| Risco | Proteção |
|---|---|
| Executável plantado com nome de ferramenta do Windows | `sc.exe`, `powercfg.exe`, PowerShell etc. sempre por System32 |
| Pasta de dados criada por outra conta ou junction | Pastas reprotegidas a cada uso; links recusados; dono = Administradores |
| Troca do SCEWIN/PresentMon entre a escolha e a execução | Cópia em pasta protegida; só a cópia é executada |
| Comando encadeado no desinstalador (HKCU) | Executável e argumentos separados, sem `cmd` |
| Backup de arquivo adulterado | Quarentena de arquivos de outros donos; reversão só restaura itens de listas permitidas |
| Release comprometida no GitHub | `SHA256SUMS.txt` assinado com a chave de releases; sem assinatura válida não há instalação automática |
| Instalador trocado após o download | Pasta só de administradores; hash calculado no mesmo handle travado até a execução |

## Hash × assinatura

O SHA-256 prova que o instalador chegou inteiro. Quem garante a origem é a **assinatura RSA** do arquivo de hashes,
feita com uma chave que fica fora do Git. Não é uma assinatura Authenticode (que exigiria um certificado pago):
o Windows ainda pode mostrar o aviso do SmartScreen no primeiro download manual.

## Limitações conhecidas

- A trava contra atrasar o relógio e a lista de revogação ficam realmente fortes só com um servidor de licenças.
- Desinstaladores de entradas HKCU continuam rodando elevados (o app inteiro é elevado), mas sem shell.
- Temperatura da CPU indisponível sem driver de kernel (decisão de segurança).

Relatório completo da auditoria: [../auditoria/ETAPA-A.md](../auditoria/ETAPA-A.md).
