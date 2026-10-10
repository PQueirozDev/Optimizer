# Qrztweaks

Aplicativo desktop para Windows, feito em C# + WPF (.NET 8), para otimização, manutenção e gerenciamento do Windows.

**Download:** [release mais recente](https://github.com/PQueirozDev/Optimizer/releases/latest) · **Site:** [qrztwk.vercel.app](https://qrztwk.vercel.app/) · **Compra e suporte:** [servidor do Discord](https://discord.gg/pHJ4Waxft)

## Principais funcionalidades

- Visão geral com as informações do sistema e uma nota de saúde do PC.
- Otimizações revisadas item por item antes de aplicar: Versão Padrão, Versão Avançada (jogos), Debloat & Privacidade e manutenção do Windows (SFC, DISM, CHKDSK).
- Ponto de restauração e backup antes dos ajustes, com reversão em um clique; operações longas podem ser canceladas.
- Limpeza de arquivos temporários, sobras do Windows Update, despejos de travamento e lixeira, com o tamanho de cada categoria antes de limpar.
- Personalizar Windows: barra de tarefas, Explorador, área de trabalho, teclado e mouse com liga/desliga aplicados na hora.
- Inicialização no estilo do Autoruns: itens de logon, tarefas agendadas e serviços automáticos, com editor verificado, filtro dos itens do Windows e liga/desliga reversível.
- Atalho de modo de energia na Área de Trabalho (recomendado para notebooks): troca entre eficiência, equilibrado, desempenho e os planos instalados sem pedir permissão de administrador.
- Modo Jogo temporário: fecha programas em segundo plano, pausa serviços e ativa o plano de desempenho enquanto você joga, restaurando tudo ao desativar. Inclui perfis por jogo (GPU dedicada, tela cheia, prioridade), limpeza da memória em espera, runtimes via winget e avisos para Ryzen X3D.
- Plano de energia Qrz, de baixa latência, com volta ao plano anterior em um clique.
- Rede: teste de velocidade (ping, jitter, perda), troca de DNS com medição, ajustes de latência reversíveis e reparos.
- Monitor ao vivo de CPU, GPU, RAM e ping na barra lateral e na visão geral.
- Editor de BIOS pelo SCEWIN (placas com BIOS AMI; o SCEWIN não acompanha o app e é escolhido pelo usuário), com recomendações seguras e restauração da cópia original.
- Perfil NVIDIA gravado direto no driver (NvAPI), instalação limpa de driver com o DDU, presets competitivos para Fortnite, Apex, CS2 e Rocket League e controle do Windows Defender (exclusões e proteção em tempo real).
- Busca rápida (Ctrl+K) e notificações ao concluir cada ação.
- Drivers, utilitários do Windows, assistente de BIOS/UEFI e catálogo de ISOs.
- Recursos (verificação de corrupção, runtimes, reinstalação limpa do driver, atalhos, downloads e benchmarks), Correções rápidas do Windows, Pontos de restauração, grupos de Serviços e otimizador/desinstalador de Apps.
- BIOS Advisor: analisa o hardware e mostra o que conferir ou mudar na BIOS (caminho, risco, como desfazer), com nota baseada em evidência, verificação da versão da BIOS no site oficial, analisador de memória, monitor de sensores e benchmark antes/depois. Nunca altera a BIOS sozinho.
- BIOS por grupos (XMP/EXPO, Resizable BAR, Spread Spectrum, PBO, C-States, virtualização), com instruções para ASUS e ASRock, e Win32 Priority no Modo Jogo.
- Tutorial guiado na primeira abertura e tutoriais curtos por página, que podem ser revistos em Configurações.
- Aparência personalizável: 9 temas (Escuro, OLED, Claro, Grafite, Oceano, Floresta, Ameixa, Areia e Automático, que segue o Windows), intensidade do roxo, densidade, tamanho dos cards, animações e janela translúcida (Windows 11), salvos em `%LocalAppData%\PQueirozOptimizer\config.json`.
- Interface em português e inglês.

## Requisitos

- Windows 10 ou 11, 64 bits.
- Permissão de administrador para instalar e aplicar as otimizações.
- Não é necessário instalar .NET, Node.js, Python nem ferramentas de desenvolvimento.
- Uma chave de acesso (veja [Ativação](#ativação)).

## Instalar

1. Baixe o `Qrztweaks-Setup-vX.Y.Z.exe` da [release mais recente](https://github.com/PQueirozDev/Optimizer/releases/latest).
2. Execute o instalador; escolha a pasta de instalação e, se quiser, marque o atalho na Área de Trabalho.

O instalador cria o atalho no Menu Iniciar e registra o desinstalador no Windows. As versões seguintes são instaladas pelo próprio aplicativo (veja [Atualizações automáticas](#atualizações-automáticas)).

## Ativação

O Qrztweaks só abre depois de validar uma chave de acesso vinculada ao computador. Na primeira abertura, clique em **Copiar pedido** e envie a mensagem pelo [Discord](https://discord.gg/pHJ4Waxft). Ao copiar a chave recebida, ela é colada sozinha na janela de ativação, mesmo que venha dentro de uma mensagem ou quebrada em linhas.

## Executar em desenvolvimento

Instale o .NET 8 SDK e rode:

```powershell
dotnet restore .\PQueirozOptimizer\PQueirozOptimizer.csproj
dotnet run --project .\PQueirozOptimizer\PQueirozOptimizer.csproj
```

## Testes

```powershell
.\tests\Verify-PowerShell.ps1
dotnet run --project .\tests\Optimizer.Verification
```

O projeto de verificação também aceita `--render`, que gera imagens das telas, e `--i18n`, que confere se a interface em inglês está toda traduzida (o esperado é 0 textos em português). O teste que ativa e desativa serviços só roda com o terminal aberto como administrador; no GitHub Actions ele sempre roda.

## Banco do BIOS Advisor

O Advisor pode ler os valores atuais pelo SCEWIN escolhido pelo usuário. Essa leitura apenas exporta as configurações; não grava ajustes na BIOS. Os valores ficam salvos com a identificação da placa e versão da BIOS e deixam de valer quando essa identificação muda.

Os perfis, caminhos documentados e nomes das opções ficam em `bios-db/bios-db.json`, embutido no aplicativo. O app procura atualizações assinadas no GitHub; aceita apenas assinaturas RSA-SHA256 válidas e versões maiores que a atual. Sem conexão, continua usando o banco embutido ou o cache verificado.

Para atualizar o banco, edite os dados com fontes oficiais, aumente `version` e execute `powershell -File .\tools\Sign-BiosDatabase.ps1`. A chave privada fica em `private/`, fora do Git. Para conferir a assinatura com a chave pública do app, execute `powershell -File .\tools\Sign-BiosDatabase.ps1 -Verify`; a verificação não precisa da pasta privada.

Para testar o recurso: `dotnet run --project .\tests\Optimizer.Verification -- artifacts/verification --bios-advisor`.
## Gerar o instalador localmente

Instale o Inno Setup 6:

```powershell
winget install JRSoftware.InnoSetup
```

Depois execute:

```powershell
.\scripts\build-installer.ps1 -Version 1.5.0
```

O executável publicado fica em:

```text
artifacts\publish\win-x64\Qrztweaks.exe
```

O instalador final fica em:

```text
artifacts\installer\Qrztweaks-Setup-v1.5.0.exe
```

## Publicar uma versão

1. Atualize a versão em `Directory.Build.props` e em `installer/PQueirozOptimizer.iss` (`MyAppVersion`).
2. Escreva as notas da versão em `PQueirozOptimizer/Pages/MainWindow.Info.cs` e as traduções em inglês em `PQueirozOptimizer/Services/Translator.Strings.cs`.
3. Rode os [testes](#testes), incluindo a checagem `--i18n`.
4. Faça o commit, crie a tag e envie (exemplo para a 1.6.0):

```powershell
git add .
git commit -m "Release v1.6.0: ..."
git tag v1.6.0
git push origin main
git push origin v1.6.0
```

Ao receber a tag, o GitHub Actions roda os testes, compila o aplicativo, gera o instalador, publica o `SHA256SUMS.txt` e cria a Release com notas automáticas. Pull requests também rodam os testes.

## Atualizações automáticas

Ao abrir, o aplicativo consulta a release mais recente pela API do GitHub, sem login; por isso este repositório precisa continuar público. Quando a release contém o `SHA256SUMS.txt`, o instalador é baixado para uma pasta protegida em `ProgramData`, conferido pelo hash e executado em modo silencioso, e o aplicativo reabre sozinho ao terminar. Releases sem o arquivo de hash abrem apenas a página de download.

## Emitir chaves de acesso (mantenedor)

O jeito recomendado de emitir chaves é o **Qrztweaks License Manager** (repositório separado), que lê o pedido do cliente, gera a chave e monta a mensagem de resposta. Como alternativa por linha de comando, mantenha a pasta `private/` fora do Git e execute:

```powershell
.\tools\New-OptimizerAccessKey.ps1 -Licensee "Nome do cliente" -MachineId "ID-DO-COMPUTADOR" -Plan Avançado
```

O comando imprime a chave a ser enviada ao cliente. `-Plan` é obrigatório (`Base`, `Intermediário`, `Avançado`, `Vitalício` ou `Personalizado`); os planos mensais valem 30 dias se `-ExpiresAtUtc` não for informado, e o Personalizado exige `-ExpiresAtUtc "2027-12-31"`. A chave privada usada para assinar fica em `private/optimizer-license-rsa-private.blob`; faça uma cópia segura dela. Sem essa chave não é possível emitir novas licenças.

## Estrutura

O produto e o executável se chamam **Qrztweaks** (`Qrztweaks.exe`). Os namespaces e a pasta do código mantêm `PQueirozOptimizer`. As pastas de dados, o campo `Product` assinado nas licenças, o mutex, o identificador da barra de tarefas e o `AppId` do instalador também são preservados para manter a compatibilidade. O instalador mantém uma cópia do executável com o nome antigo para atalhos de energia e limpeza já existentes, e cria os atalhos principais apontando para `Qrztweaks.exe`. A release continua publicando `PQueirozOptimizer-Setup-vX.Y.Z.exe` para o atualizador das versões até a 1.9.0. Referências ao nome antigo nas notas históricas são intencionais.

- `PQueirozOptimizer/`: código-fonte WPF.
- `Otimizador_de_PC.ps1`: script PowerShell com as otimizações, executado pelo aplicativo.
- `tests/`: verificações do script (`Verify-PowerShell.ps1`) e do aplicativo (`Optimizer.Verification`).
- `installer/PQueirozOptimizer.iss`: instalador Inno Setup.
- `scripts/build-installer.ps1`: publicação self-contained e geração do instalador.
- `tools/`: emissão de chaves de acesso, geração do ícone, das imagens do instalador (`Create-InstallerArt.ps1`) e do vídeo do site (`Make-SiteVideo.ps1`).
- Fotos e vídeo do site: `dotnet run --project .\tests\Optimizer.Verification -- <pasta> --shots` gera as capturas da interface real; com `--videoshots` gera as cenas e `.\tools\Make-SiteVideo.ps1 -Scenes <pasta>` monta o `site\assets\tour.mp4` (precisa do ffmpeg: `winget install Gyan.FFmpeg`).
- `site/`: site do produto.
- `.github/workflows/release.yml`: testes em pull requests e release automática em tags `vX.Y.Z`.

## Segurança

- A chave privada que assina as licenças fica fora do repositório (`private/`, ignorada pelo Git); o aplicativo contém apenas a chave pública, que só valida chaves.
- Arquivos gerados, credenciais, `.env`, logs, builds e instaladores locais ficam fora do Git pelo `.gitignore`.

## Direitos autorais

© Pedro Queiroz. Todos os direitos reservados. O código está visível para consulta, mas não é distribuído sob uma licença de código aberto.
