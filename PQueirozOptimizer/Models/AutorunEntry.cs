using System.Windows.Media;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer.Models;

/// <summary>Grupos da página Inicialização, como as abas do Autoruns.</summary>
public enum AutorunCategory { Logon, Tasks, Services }

/// <summary>NotApplicable: não há arquivo para verificar (ex.: manipulador COM sem DLL registrada).</summary>
public enum SignatureStatus { Pending, Verified, NotVerified, NotSigned, NotApplicable }

/// <summary>Algo que o Windows executa sozinho: item de logon, tarefa agendada ou serviço automático.</summary>
public sealed class AutorunEntry
{
    public required AutorunCategory Category { get; init; }
    public required string Name { get; init; }
    /// <summary>Cabeçalho do grupo na lista: chave do registro, pasta, pasta de tarefas ou chave de serviços.</summary>
    public required string Location { get; init; }
    /// <summary>Identifica o item ao ativar/desativar: nome do valor, arquivo, caminho da tarefa ou nome do serviço.</summary>
    public required string Key { get; init; }
    public string Command { get; init; } = "";
    /// <summary>Executável ou DLL que de fato roda (ou null quando não há arquivo, como em alguns manipuladores COM).</summary>
    public string? ImagePath { get; init; }
    /// <summary>Informação extra da origem: gatilhos da tarefa, tipo de início do serviço, "executa uma vez".</summary>
    public string Detail { get; init; } = "";
    public bool Enabled { get; set; }
    public bool CanToggle { get; init; } = true;
    /// <summary>Chave do registro para "Ir para a entrada" (itens de logon).</summary>
    public string? RegistryPath { get; init; }
    /// <summary>Onde o Windows guarda se o item de logon está ativo (mesmo mecanismo do Gerenciador de Tarefas).</summary>
    public StartupSource? Source { get; init; }

    public string Description { get; set; } = "";
    public string Company { get; set; } = "";
    public bool FileExists { get; set; } = true;
    public SignatureStatus Signature { get; set; } = SignatureStatus.Pending;
    public string Signer { get; set; } = "";
    /// <summary>Parte do próprio Windows (assinado pela Microsoft como Windows). Fica oculto por padrão, como no Autoruns.</summary>
    public bool IsWindows { get; set; }
    public ImageSource? Icon { get; set; }
}
