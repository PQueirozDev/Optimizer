# Qrztweaks site

Site estático do Qrztweaks, com tema escuro e página inicial compacta. Não exige build: publique a pasta `site/` em Vercel, Netlify ou GitHub Pages.

Publicado em [qrztwk.pqueirozdev.com.br](https://qrztwk.pqueirozdev.com.br/). Os links de download apontam para a release mais recente do repositório `PQueirozDev/Optimizer` (`releases/latest`), então não precisam mudar a cada versão.

## Conteúdo dinâmico

`landing.js` consulta a release mais recente do GitHub e preenche a versão e os links diretos do instalador oficial. O resultado fica em cache na sessão por até dez minutos. Sem resposta, os botões levam à página de download oficial.

Em `recursos.html`, `script.js` também preenche as notas das versões a partir das releases. O texto é inserido como texto, nunca como HTML.

## Organização

- `index.html`: apresentação, três benefícios, download, quatro planos e dúvidas frequentes.
- `recursos.html`: recursos completos, galeria, comparações e novidades.
- `termos.html`: orientações de compra, reembolso e garantia, preservados da versão anterior.
- `theme.css`: tema escuro compartilhado. As páginas de apoio também usam `styles.css` e `showcase.css`.
- A compra e a emissão da chave continuam pelo Discord. Nenhum checkout Stripe está ativo nesta versão.

## Regras de conteúdo

- **Resultados de desempenho**: a tabela da seção Performance Lab só recebe resultados verificáveis, com hardware,
  jogo, configurações, resolução, metodologia, FPS médio, 1% low e variação. Nunca publique números estimados.
- **Preços e planos**: os mesmos de `LicensePlans.ForSale` no app e do JSON-LD no `<head>`; mude os três juntos.
- **Política de reembolso**: texto jurídico; só altere com revisão do responsável.

## Acessibilidade e SEO

Link "Pular para o conteúdo", `aria-*` nos controles, imagens com `alt`, `prefers-reduced-motion` respeitado,
metadados Open Graph/Twitter, `canonical` e dados estruturados `SoftwareApplication`.

Publicar o site (Vercel) é uma ação do dono do projeto.
