namespace PQueirozOptimizer.Services;

/// <summary>Uma correção rápida: cada etapa é um comando do PowerShell, executado em ordem.</summary>
public sealed record FixDefinition(string Id, string Name, string Description, string Glyph, string Category, bool NeedsRestart, (string Label, string Script)[] Steps);

/// <summary>Resultado de uma correção: quantas etapas deram certo e a primeira mensagem de erro.</summary>
public sealed record FixResult(int Succeeded, int Total, string? FirstError);

/// <summary>Correções rápidas para problemas comuns do Windows (equivalente à página Fixes do Paragon).</summary>
public sealed class FixesService
{
    private readonly ActivityLog _log;
    public FixesService(ActivityLog log) => _log = log;

    public static readonly FixDefinition[] Fixes =
    {
        new("windows-update", "Reparar o Windows Update", "Para os serviços de atualização, renomeia as pastas de cache (SoftwareDistribution e catroot2) e inicia tudo de novo. Resolve atualizações travadas ou com erro.", Glyphs.Refresh, "Windows", false, new[]
        {
            ("Parando serviços de atualização", "foreach ($s in 'wuauserv','bits','cryptsvc','msiserver') { Stop-Service -Name $s -Force -ErrorAction SilentlyContinue }"),
            ("Renomeando o cache de atualizações", "$t = Get-Date -Format 'yyyyMMddHHmmss'; foreach ($p in \"$env:SystemRoot\\SoftwareDistribution\", \"$env:SystemRoot\\System32\\catroot2\") { if (Test-Path $p) { Rename-Item -Path $p -NewName ((Split-Path $p -Leaf) + \".old-$t\") -Force } }"),
            ("Iniciando serviços de atualização", "foreach ($s in 'cryptsvc','bits','wuauserv') { Start-Service -Name $s -ErrorAction SilentlyContinue }"),
        }),
        new("store", "Reparar a Microsoft Store", "Limpa o cache da Loja e registra de novo o aplicativo. Resolve downloads parados e a Loja que não abre.", Glyphs.Package, "Windows", false, new[]
        {
            ("Limpando o cache da Loja", "Start-Process wsreset.exe -ArgumentList '-i' -WindowStyle Hidden -Wait -ErrorAction SilentlyContinue; Start-Sleep 2; Get-Process WinStore.App -ErrorAction SilentlyContinue | Stop-Process -Force"),
            ("Registrando a Loja novamente", "Get-AppxPackage -AllUsers Microsoft.WindowsStore | ForEach-Object { Add-AppxPackage -DisableDevelopmentMode -Register \"$($_.InstallLocation)\\AppXManifest.xml\" -ErrorAction SilentlyContinue }"),
        }),
        new("icons", "Reconstruir cache de ícones e miniaturas", "Apaga os caches de ícones e miniaturas do Explorer e o reinicia. Resolve ícones em branco, trocados ou miniaturas erradas.", Glyphs.Folder, "Explorer", false, new[]
        {
            ("Fechando o Explorer", "Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue; Start-Sleep 1"),
            ("Apagando os caches", "Remove-Item \"$env:LOCALAPPDATA\\IconCache.db\" -Force -ErrorAction SilentlyContinue; Remove-Item \"$env:LOCALAPPDATA\\Microsoft\\Windows\\Explorer\\iconcache_*.db\",\"$env:LOCALAPPDATA\\Microsoft\\Windows\\Explorer\\thumbcache_*.db\" -Force -ErrorAction SilentlyContinue"),
            ("Abrindo o Explorer", "Start-Process explorer.exe"),
        }),
        new("explorer", "Reiniciar o Explorer", "Reinicia a barra de tarefas, o menu Iniciar e as janelas de pastas. Resolve barra de tarefas travada ou que não responde.", Glyphs.Monitor, "Explorer", false, new[]
        {
            ("Reiniciando o Explorer", "Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue; Start-Sleep 1; if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }"),
        }),
        new("audio", "Reparar o áudio", "Reinicia os serviços de áudio do Windows. Resolve som que sumiu, chiado depois de suspender ou dispositivo que não aparece.", Glyphs.Speaker, "Hardware", false, new[]
        {
            ("Reiniciando os serviços de áudio", "Restart-Service -Name AudioEndpointBuilder -Force; Restart-Service -Name Audiosrv -Force"),
        }),
        new("bluetooth", "Reparar o Bluetooth", "Restaura o rádio Bluetooth do Windows, corrige os serviços de descoberta, instala o driver oficial do TP-Link UB500 quando detectado e recarrega o adaptador.", Glyphs.Bluetooth, "Hardware", true, new[]
        {
            ("Restaurando o rádio Bluetooth", @"
$radioPath = 'HKLM:\SYSTEM\CurrentControlSet\Control\RadioManagement\{afd198ac-5f30-4e89-a789-5ddf60a69366}'
if (Test-Path $radioPath) {
    New-ItemProperty -Path $radioPath -Name BluetoothRadioState -PropertyType DWord -Value 1 -Force | Out-Null
}
Set-Service -Name bthserv -StartupType Automatic
if (Get-Service -Name BTAGService -ErrorAction SilentlyContinue) { Set-Service -Name BTAGService -StartupType Manual }
Start-Service -Name bthserv -ErrorAction SilentlyContinue
Start-Service -Name BTAGService -ErrorAction SilentlyContinue
Restart-Service -Name DeviceAssociationService -Force
Get-Service -Name 'BluetoothUserService*' -ErrorAction SilentlyContinue | Restart-Service -Force
"),
            ("Atualizando o driver oficial do adaptador", @"
$adapter = Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue |
    Where-Object { $_.InstanceId -like 'USB\VID_2357&PID_0604*' } |
    Select-Object -First 1
if ($adapter) {
    $work = Join-Path $env:TEMP 'PQueirozOptimizer-UB500'
    $zip = Join-Path $work 'UB500_V3_Win10_Win11.zip'
    $extract = Join-Path $work 'package'
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    Invoke-WebRequest -Uri 'https://static.tp-link.com/upload/driver/2025/202503/20250314/UB500_V3_Win10_Win11.zip' -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath $extract -Force
    $arch = if ([Environment]::OSVersion.Version.Build -ge 22000) { 'Windows_11_64bit' } else { 'Windows_10_64bit' }
    $inf = Join-Path $extract ('UB500V3\BT\plugins\Driver Files\Driver\' + $arch + '\Rtkfilter.inf')
    if (-not (Test-Path $inf)) { throw 'O pacote oficial não contém o driver compatível com este Windows.' }
    & pnputil.exe /add-driver $inf /install | Out-Null
    if ($LASTEXITCODE -ne 0) { throw ""A instalação do driver terminou com código $LASTEXITCODE."" }
} elseif ($adapter) {
    Write-Output ('Adaptador identificado: ' + $adapter.FriendlyName + '. Driver específico não alterado.')
} else {
    throw 'Nenhum adaptador Bluetooth físico foi detectado.'
}
"),
            ("Recarregando o adaptador Bluetooth", @"
$adapter = Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue |
    Where-Object { $_.InstanceId -match '^(USB|PCI|ACPI)\\' } |
    Select-Object -First 1
if ($adapter -and $adapter.InstanceId -like 'USB\VID_2357&PID_0604*') {
    Disable-PnpDevice -InstanceId $adapter.InstanceId -Confirm:$false
    Start-Sleep -Seconds 3
    Enable-PnpDevice -InstanceId $adapter.InstanceId -Confirm:$false
}
& pnputil.exe /scan-devices | Out-Null
Start-Sleep -Seconds 2
Restart-Service -Name bthserv -Force
Get-Service -Name 'BluetoothUserService*' -ErrorAction SilentlyContinue | Restart-Service -Force
"),
        }),
        new("search", "Reparar a pesquisa do Windows", "Reinicia o serviço de pesquisa e reconstrói o índice. Resolve a pesquisa do menu Iniciar que não encontra arquivos ou programas.", Glyphs.Search, "Windows", false, new[]
        {
            ("Parando a pesquisa", "Stop-Service -Name WSearch -Force -ErrorAction SilentlyContinue"),
            ("Pedindo a reconstrução do índice", "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows Search' -Name SetupCompletedSuccessfully -Value 0 -Type DWord"),
            ("Iniciando a pesquisa", "Set-Service -Name WSearch -StartupType AutomaticDelayedStart -ErrorAction SilentlyContinue; Start-Service -Name WSearch"),
        }),
        new("print", "Limpar a fila de impressão", "Remove trabalhos de impressão travados. Resolve a impressora que não imprime nada.", Glyphs.Print, "Hardware", false, new[]
        {
            ("Limpando a fila", "Stop-Service -Name Spooler -Force; Remove-Item \"$env:SystemRoot\\System32\\spool\\PRINTERS\\*\" -Force -ErrorAction SilentlyContinue; Start-Service -Name Spooler"),
        }),
        new("time", "Sincronizar o relógio", "Sincroniza a data e a hora com a internet. Resolve erros de certificado, de login em jogos e de anti-cheat.", Glyphs.Clock, "Rede", false, new[]
        {
            ("Sincronizando o relógio", "Start-Service w32time -ErrorAction SilentlyContinue; w32tm /resync /force | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'O serviço de horário não respondeu.' }"),
        }),
        new("dns", "Limpar o cache de DNS", "Apaga os endereços guardados. Resolve sites e servidores de jogos que não abrem depois de mudarem de endereço.", Glyphs.Globe, "Rede", false, new[]
        {
            ("Limpando o DNS", "Clear-DnsClientCache"),
        }),
        new("network", "Redefinir a rede", "Reinicia Winsock e TCP/IP e limpa o DNS. Use quando a internet não funcionar de jeito nenhum.", Glyphs.Network, "Rede", true, new[]
        {
            ("Reiniciando o Winsock", "netsh winsock reset | Out-Null"),
            ("Reiniciando o TCP/IP", "netsh int ip reset | Out-Null"),
            ("Limpando o DNS", "ipconfig /flushdns | Out-Null"),
        }),
        new("shader", "Limpar caches de shaders", "Apaga os caches de shaders do DirectX, NVIDIA e AMD. Resolve travadas e falhas gráficas depois de atualizar driver ou jogo (os jogos recompilam os shaders).", Glyphs.Game, "Jogos", false, new[]
        {
            ("Limpando caches de shaders", "$deleted=0; $locked=0; $denied=0; foreach($root in @(\"$env:LOCALAPPDATA\\D3DSCache\",\"$env:LOCALAPPDATA\\NVIDIA\\DXCache\",\"$env:LOCALAPPDATA\\NVIDIA\\GLCache\",\"$env:ProgramData\\NVIDIA Corporation\\NV_Cache\",\"$env:LOCALAPPDATA\\AMD\\DxCache\",\"$env:LOCALAPPDATA\\AMD\\DxcCache\",\"$env:LOCALAPPDATA\\AMD\\VkCache\")){ if(Test-Path -LiteralPath $root){ try { $files=@(Get-ChildItem -LiteralPath $root -File -Recurse -Force -ErrorAction Stop); foreach($file in $files){ try { Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop; $deleted++ } catch { if($_.Exception -is [UnauthorizedAccessException]){$denied++}else{$locked++} } } } catch { if($_.Exception -is [UnauthorizedAccessException]){$denied++}else{$locked++} } } }; if(($locked+$denied) -gt 0){ throw \"Limpeza parcial dos caches de shaders: $deleted arquivo(s) removido(s), $locked bloqueado(s), $denied negado(s).\" }; \"Caches de shaders: $deleted arquivo(s) removido(s)\""),
        }),
        new("power-plans", "Restaurar planos de energia padrão", "Recria os planos Equilibrado, Alto desempenho e Economia de energia do Windows. Resolve planos sumidos ou corrompidos.", Glyphs.Battery, "Windows", false, new[]
        {
            ("Restaurando os planos", "powercfg -restoredefaultschemes; if ($LASTEXITCODE -ne 0) { throw \"powercfg terminou com código $LASTEXITCODE\" }"),
        }),
        new("components", "Limpar componentes antigos do Windows", "Remove versões antigas de atualizações guardadas no WinSxS. Libera espaço no disco do sistema (pode levar alguns minutos).", Glyphs.Broom, "Windows", false, new[]
        {
            ("Limpando componentes", "dism.exe /Online /Cleanup-Image /StartComponentCleanup | Out-Null; if ($LASTEXITCODE -ne 0) { throw \"DISM terminou com código $LASTEXITCODE\" }"),
        }),
    };

    /// <summary>Executa as etapas em ordem; uma etapa que falha não impede as seguintes.</summary>
    public async Task<FixResult> RunAsync(FixDefinition fix, IProgress<string>? progress = null)
    {
        var ok = 0;
        string? firstError = null;
        foreach (var (label, script) in fix.Steps)
        {
            progress?.Report(label + "...");
            try { await PowerShellBridge.RunScriptAsync(script, timeout: TimeSpan.FromMinutes(15)); ok++; }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException) { firstError ??= ex.Message.Split('\n')[0]; progress?.Report($"[ERRO] {label}: {firstError}"); }
        }
        _log.Write(ok == fix.Steps.Length ? "SUCCESS" : "WARN", $"Correção \"{fix.Name}\": {ok}/{fix.Steps.Length} etapas concluídas" + (firstError != null ? $" ({firstError})" : ""));
        return new FixResult(ok, fix.Steps.Length, firstError);
    }
}
