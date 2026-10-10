# Qrztweaks site

Landing page estática para o Qrztweaks e o Windows personalizado. Não exige build: publique a pasta `site/` em Vercel, Netlify ou GitHub Pages.

Publicado em [qrztwk.vercel.app](https://qrztwk.vercel.app/). Os links de download apontam para a release mais recente do repositório `PQueirozDev/Optimizer` (`releases/latest`), então não precisam mudar a cada versão.

## Conteúdo dinâmico

`script.js` consulta a API pública de releases do GitHub ao abrir a página e preenche:

- a versão (`[data-version]`) no topo e no botão de download;
- o link direto do instalador `Qrztweaks-Setup-v*.exe` na seção Baixar;
- as patch notes (`#release-list`), a partir dos itens de lista da descrição de cada release.

Sem conexão ou com o limite da API atingido, fica o conteúdo estático, que não afirma uma versão específica.
O texto das releases é inserido como texto (nunca como HTML).

## Regras de conteúdo

- **Resultados de desempenho**: a tabela da seção Performance Lab só recebe resultados verificáveis, com hardware,
  jogo, configurações, resolução, metodologia, FPS médio, 1% low e variação. Nunca publique números estimados.
- **Preços e planos**: os mesmos de `LicensePlans.ForSale` no app e do JSON-LD no `<head>`; mude os três juntos.
- **Política de reembolso**: texto jurídico; só altere com revisão do responsável.

## Acessibilidade e SEO

Link "Pular para o conteúdo", `aria-*` nos controles, imagens com `alt`, `prefers-reduced-motion` respeitado,
metadados Open Graph/Twitter, `canonical` e dados estruturados `SoftwareApplication`.

Publicar o site (Vercel) é uma ação do dono do projeto.
