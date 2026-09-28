# PQueiroz Optimizer

Aplicativo desktop nativo para Windows feito em C# + WPF (.NET 8) para otimizacao, manutencao e gerenciamento do Windows.

## Principais funcionalidades

- Dashboard com informacoes do sistema.
- Perfis de otimizacao para uso padrao, gamer, manutencao e privacidade.
- Execucao das rotinas existentes do `Otimizador_de_PC.ps1` pela interface grafica.
- Limpeza rapida de arquivos temporarios.
- Inicialização no estilo do Autoruns: itens de logon, tarefas agendadas e serviços automáticos, com editor verificado, filtro dos itens do Windows e liga/desliga reversível.
- Atalho de modo de energia na Área de Trabalho (recomendado para notebooks): troca entre eficiência, equilibrado, desempenho e os planos instalados sem pedir permissão de administrador.
- Ferramentas de manutencao do Windows, drivers e catalogo de ISOs.
- Temas claro/escuro e interface em portugues/ingles.

## Requisitos para usuarios

- Windows 10/11 x64.
- Permissao de administrador para instalar e executar recursos de otimizacao.
- Nao e necessario instalar .NET, Node.js, Python ou ferramentas de desenvolvimento.

## Baixar a versao mais recente

1. Abra a pagina do projeto no GitHub.
2. Va em **Releases**.
3. Baixe o arquivo `PQueirozOptimizer-Setup-vX.Y.Z.exe` da versao mais recente.
4. Execute o instalador.
5. Escolha a pasta de instalacao, se desejar.
6. Marque a opcao de atalho na Area de Trabalho, se quiser.

O instalador cria atalho no Menu Iniciar e registra o desinstalador no Windows.

## Executar em desenvolvimento

Instale o .NET 8 SDK e rode:

```powershell
dotnet restore .\PQueirozOptimizer\PQueirozOptimizer.csproj
dotnet run --project .\PQueirozOptimizer\PQueirozOptimizer.csproj
```

## Gerar o instalador localmente

Instale o Inno Setup 6:

```powershell
winget install JRSoftware.InnoSetup
```

Depois execute:

```powershell
.\scripts\build-installer.ps1 -Version 1.0.0
```

O executavel publicado fica em:

```text
artifacts\publish\win-x64\PQueirozOptimizer.exe
```

O instalador final fica em:

```text
artifacts\installer\PQueirozOptimizer-Setup-v1.0.0.exe
```

## Criar uma release no GitHub

1. Atualize a versao em `Directory.Build.props` (ex.: `1.2.0`) e as notas em `PQueirozOptimizer/Pages/MainWindow.Info.cs`.
2. Rode os testes localmente:

```powershell
.\tests\Verify-PowerShell.ps1
dotnet run --project .\tests\Optimizer.Verification
```

3. Crie e envie a tag:

```powershell
git add .
git commit -m "Prepare release v1.2.0"
git tag v1.2.0
git push origin main
git push origin v1.2.0
```

Ao receber a tag, o GitHub Actions roda os testes, compila o aplicativo, gera o instalador, publica o `SHA256SUMS.txt` e cria a Release com notas automaticas. Pull requests tambem rodam os testes.

## Atualizacoes automaticas

O aplicativo consulta a release mais recente ao abrir. Quando a release contem `SHA256SUMS.txt`, o instalador e baixado para uma pasta protegida em `ProgramData`, conferido pelo hash e executado em modo silencioso; o aplicativo reabre sozinho ao terminar. Releases sem o arquivo de hash abrem apenas a pagina de download.

## Emitir chaves de acesso

O Optimizer só abre após validar uma chave assinada. Na primeira abertura, o aplicativo mostra o **ID deste computador**; o botão **Copiar pedido** copia uma mensagem pronta com esse ID para o cliente enviar. A chave recebida é colada automaticamente quando está na área de transferência, mesmo que venha dentro de uma mensagem ou quebrada em linhas.

O jeito recomendado de emitir chaves é o **PQueiroz License Manager** (repositório separado), que lê o pedido do cliente, gera a chave e monta a mensagem de resposta. Como alternativa por linha de comando, mantenha a pasta `private/` fora do Git e execute:

```powershell
.\tools\New-OptimizerAccessKey.ps1 -Licensee "Nome do cliente" -MachineId "ID-DO-COMPUTADOR"
```

O comando imprime a chave a ser enviada ao cliente. Para uma licença com validade, acrescente `-ExpiresAtUtc "2027-12-31"`. A chave privada usada para assinar fica em `private/optimizer-license-rsa-private.blob`; faça uma cópia segura dela. Sem essa chave não é possível emitir novas licenças.

## Estrutura importante

- `PQueirozOptimizer/`: codigo-fonte WPF.
- `Otimizador_de_PC.ps1`: script PowerShell usado pelo app.
- `installer/PQueirozOptimizer.iss`: instalador Inno Setup.
- `scripts/build-installer.ps1`: publicacao self-contained e geracao do instalador.
- `.github/workflows/release.yml`: build automatico em tags `vX.Y.Z`.

## Observacoes de seguranca

Arquivos gerados, credenciais, `.env`, logs, builds e instaladores locais ficam fora do Git por `.gitignore`.
