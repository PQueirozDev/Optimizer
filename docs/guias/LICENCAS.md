# Licenças, planos e venda

## Como a licença funciona

- A chave (`PQO1-...`) é um conteúdo JSON assinado com RSA-SHA256. A chave pública está no app
  (`LicenseService.PublicKeyBlob`); a privada fica só com o emissor (License Manager), fora do repositório.
- Campos: produto, titular, ID do computador, papel (Standard/Admin), validade (ausente = vitalícia) e plano.
- **ID do computador**: hash do `MachineGuid` do Windows. Muda ao reinstalar o Windows. O ID antigo (nome do PC +
  usuário) continua aceito para não invalidar chaves já emitidas.
- **Revogação**: lista assinada pelo mesmo emissor em `site/revocations.json`, guardada para uso offline.
- **Relógio**: a data mais recente de uso fica guardada; atrasar o relógio bloqueia licenças com validade. Com
  internet, a hora do servidor corrige.

## Planos (preços atuais, iguais no app e no site)

| Plano | Preço | Libera |
|---|---|---|
| Base | R$ 15/mês | Otimizações, Smart Optimize (itens do Base), Inicialização, Correções, Ferramentas, Limpeza |
| Intermediário | R$ 25/mês | + Serviços, Apps, Drivers, Rede, Recursos, Diagnóstico, Performance Lab |
| Avançado | R$ 29,99/mês | O app completo: + Modo Jogo, Perfis, Personalizar Windows, BIOS |
| Vitalício | R$ 59,99 único | O app completo, sem vencimento, com reemissão ao formatar |

Atividade e reversão, pontos de restauração e configurações ficam em todos os planos: ninguém fica sem desfazer.

## Modo demonstração

Na tela de ativação, **Explorar sem licença** abre o app em modo só leitura: Command Center, Smart Optimize
(análise), Diagnóstico e Performance Lab. Toda operação continua passando por `PlanAccess` com licença nula, que
nega: a demonstração não aplica nada e não é um caminho para contornar a ativação.

## Troca de hardware ou Windows reinstalado

A tela de ativação detecta uma chave válida de outro computador e oferece **Troquei de PC / reinstalei o Windows**:
o app copia um pedido de transferência com o titular da chave antiga e o ID novo. O emissor reemite a chave.
Nenhuma validação é afrouxada.

## Avisos

- 7 dias antes do vencimento: aviso no Command Center com renovação pelo Discord.
- Licença vencida, revogada, de outro PC ou com relógio atrasado: a tela de ativação explica o motivo.

## Fluxo atual de venda

Manual, pelo Discord: o cliente copia o pedido no app, paga via Pix, o suporte confere o pagamento e emite a chave.

## Planejamento: checkout automático (não implementado)

Nenhum gateway foi integrado; isto é só o desenho para quando houver autorização.

1. **Seleção do plano** no site, com o ID do computador colado pelo cliente (o app já gera o pedido).
2. **Pagamento** num gateway com Pix e cartão (ex.: Mercado Pago, Stripe). O site nunca vê dados de cartão.
3. **Confirmação** somente pelo webhook assinado do gateway, verificado no servidor. **Nunca** emitir licença a
   partir de comprovante enviado pelo cliente.
4. **Emissão** num serviço de servidor (função serverless) que guarda a chave privada num cofre de segredos
   (nunca no site nem no app) e assina a chave com o mesmo formato `PQO1-`.
5. **Entrega** por e-mail e na página do pedido; opcionalmente o app consulta o pedido e ativa sozinho.
6. **Histórico** de pedidos e chaves por cliente, com revogação (publicando a lista assinada) e reemissão.

### Painel administrativo (futuro)

Busca de clientes e chaves, renovação, upgrade, revogação e reemissão; trilha de auditoria; acesso só do dono com
autenticação forte. Exige infraestrutura (servidor e banco), por isso não foi criado.

### Lease de licença (futuro)

Para fortalecer relógio e revogação: o servidor assina um "lease" curto (ex.: 7 dias) com a hora oficial; offline,
o app aceita até o fim do lease. Também exige servidor.
