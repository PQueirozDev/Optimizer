# PQueiroz Optimizer

Aplicativo desktop nativo para Windows feito em C# + WPF (.NET 8) para otimizacao, manutencao e gerenciamento do Windows.

## Principais funcionalidades

- Dashboard com informacoes do sistema.
- Perfis de otimizacao para uso padrao, gamer, manutencao e privacidade.
- Execucao das rotinas existentes do `Otimizador_de_PC.ps1` pela interface grafica.
- Limpeza rapida de arquivos temporarios.
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

Atualize a versao desejada e crie uma tag:

```powershell
git add .
git commit -m "Prepare release v1.0.0"
git tag v1.0.0
git push origin main
git push origin v1.0.0
```

Ao receber a tag `v1.0.0`, o GitHub Actions compila o aplicativo, gera o instalador e cria uma Release com o `.exe` anexado.

Para novas versoes, repita o processo com `v1.1.0`, `v1.2.0` e assim por diante.

## Atualizacoes futuras

A estrutura usa versionamento semantico e GitHub Releases. O aplicativo consulta a release mais recente ao abrir o dashboard e oferece o link de atualizacao quando uma versao nova esta disponivel.

## Emitir chaves de acesso

O Optimizer só abre após validar uma chave assinada. Na primeira abertura, o aplicativo mostra o **ID deste computador**; use-o para vincular a licença ao equipamento do cliente.

No computador de emissão, mantenha a pasta `private/` fora do Git e execute:

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
