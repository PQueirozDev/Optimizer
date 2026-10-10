# Desenvolvimento, build e publicação

## Requisitos

- Windows 10/11 x64, .NET 8 SDK.
- Inno Setup 6 para gerar o instalador (`winget install JRSoftware.InnoSetup`).
- Terminal como administrador só para o teste que mexe em serviços (no GitHub Actions ele sempre roda).

## Rodar

```powershell
dotnet restore .\PQueirozOptimizer\PQueirozOptimizer.csproj
dotnet run --project .\PQueirozOptimizer\PQueirozOptimizer.csproj
```

## Testes

```powershell
.\tests\Verify-PowerShell.ps1
dotnet run --project .\tests\Optimizer.Verification                 # tudo (exceto telas e i18n)
dotnet run --project .\tests\Optimizer.Verification -- <pasta> --safety  # Etapa A (segurança e reversão)
dotnet run --project .\tests\Optimizer.Verification -- <pasta> --v2      # motor 2.0
dotnet run --project .\tests\Optimizer.Verification -- <pasta> --i18n    # interface em inglês (esperado: só os textos antigos)
dotnet run --project .\tests\Optimizer.Verification -- <pasta> --branding
dotnet build .\PQueirozOptimizer\PQueirozOptimizer.csproj -c Release
```

Atenção: `--no-build` roda a cópia do app que está na pasta do projeto de testes. Depois de mudar o app, rode os
testes sem `--no-build` pelo menos uma vez.

## Regras para mexer no código

- **Etapas do script**: toda etapa de Padrão, Avançada e Debloat precisa de `Risco = "..."` (ou `# risco: ...`) e de
  um item no `TweakCatalog` (ou entrada em `TweakCatalog.Excluded` com o motivo). Os testes conferem.
- **Textos novos na interface**: escreva em português e adicione a tradução em `Translator.V2.cs` (dicionário exato,
  sem chaves repetidas, ou padrão para textos com números). Rode `--i18n`.
- **Operações elevadas**: ferramentas por `SystemTools`, pastas por `EnsureProtectedDirectory`, nada de `cmd /c`
  com texto vindo do registro ou do usuário.
- **Sem números inventados**: o que não foi medido fica nulo e aparece como indisponível.

## Gerar o instalador localmente

```powershell
.\scripts\build-installer.ps1 -Version 2.0.0
```

Saída: `artifacts\installer\Qrztweaks-Setup-v2.0.0.exe`.

## Ícone

`tools/Create-AppIcon.ps1` desenha o ícone em vetor e gera app.png, app.ico e os ícones do site;
`tools/Create-InstallerArt.ps1` refaz as imagens do instalador. Veja `tools/logo/README.md`.

## Chaves e assinaturas

| Chave | Para quê | Onde fica a privada | Ferramenta |
|---|---|---|---|
| Licenças | Assinar chaves `PQO1-` e a lista de revogação | License Manager (fora do repositório) | License Manager |
| Banco do BIOS Advisor | `bios-db/bios-db.json.sig` | `private/bios-db-rsa-private.blob` | `tools/Sign-BiosDatabase.ps1` |
| Releases | `SHA256SUMS.txt.sig` | `private/update-rsa-private.blob` + secret `UPDATE_SIGNING_KEY` | `tools/Sign-ReleaseChecksums.ps1` |

`private/` está no `.gitignore`. Guarde cópias seguras das chaves privadas: perder a de releases obriga a publicar
uma versão com chave nova manualmente.

### Configurar o secret de releases (uma vez)

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("private\update-rsa-private.blob")) | Set-Clipboard
```

Cole em GitHub → Settings → Secrets and variables → Actions → New repository secret → `UPDATE_SIGNING_KEY`.
Sem o secret, o workflow de release falha de propósito.

## Publicar uma versão

1. Versão em `Directory.Build.props` e `installer/PQueirozOptimizer.iss` (`MyAppVersion`).
2. Notas em `PQueirozOptimizer/Pages/MainWindow.Info.cs` e as traduções (`Translator.V2.cs`).
3. Testes, incluindo `--i18n`.
4. `scripts/publish-release.ps1` (na `main`): roda os testes, cria o commit e a tag e envia.
5. O GitHub Actions compila, gera o instalador, o `SHA256SUMS.txt`, assina, verifica a assinatura e cria a release.

Atualizar uma Release ou o site é publicação: só com autorização do dono do projeto.
