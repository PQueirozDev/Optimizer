param(
    [Parameter(Mandatory)] [string] $Licensee,
    [Parameter(Mandatory)] [string] $MachineId,
    # O plano vai assinado na chave e decide o que o Optimizer libera. Chave sem plano libera tudo (compatibilidade), por isso é obrigatório aqui.
    [Parameter(Mandatory)] [ValidateSet('Base', 'Intermediário', 'Avançado', 'Vitalício', 'Personalizado')] [string] $Plan,
    [datetime] $ExpiresAtUtc
)

$monthly = $Plan -in 'Base', 'Intermediário', 'Avançado'
if ($Plan -eq 'Vitalício' -and $PSBoundParameters.ContainsKey('ExpiresAtUtc')) { throw "O plano Vitalício não tem data de validade." }
# Planos mensais: 30 dias a partir de hoje, até o fim do dia, quando a validade não for informada
if ($monthly -and -not $PSBoundParameters.ContainsKey('ExpiresAtUtc')) { $ExpiresAtUtc = (Get-Date).Date.AddDays(31).AddSeconds(-1); $PSBoundParameters['ExpiresAtUtc'] = $ExpiresAtUtc }
if ($Plan -eq 'Personalizado' -and -not $PSBoundParameters.ContainsKey('ExpiresAtUtc')) { throw "Informe -ExpiresAtUtc para o plano Personalizado." }

$privateKeyPath = Join-Path $PSScriptRoot '..\private\optimizer-license-rsa-private.blob'
if (-not (Test-Path -LiteralPath $privateKeyPath)) {
    throw "Chave privada do emissor não encontrada em: $privateKeyPath"
}

$payload = [ordered]@{
    Product = 'PQueirozOptimizer'
    Licensee = $Licensee.Trim()
    MachineId = ($MachineId.ToUpperInvariant() -replace '[^0-9A-F]', '')
    Role = 'Standard'
    ExpiresAtUtc = if ($PSBoundParameters.ContainsKey('ExpiresAtUtc')) { $ExpiresAtUtc.ToUniversalTime().ToString('o') } else { $null }
    Plan = $Plan
} | ConvertTo-Json -Compress

function To-Base64Url([byte[]] $Bytes) {
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

$data = [Text.Encoding]::UTF8.GetBytes($payload)
$rsa = [Security.Cryptography.RSACryptoServiceProvider]::new()
try {
    $rsa.ImportCspBlob([IO.File]::ReadAllBytes($privateKeyPath))
    $signature = $rsa.SignData($data, [Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'))
    "PQO1-$(To-Base64Url $data).$(To-Base64Url $signature)"
}
finally {
    $rsa.PersistKeyInCsp = $false
    $rsa.Dispose()
}
