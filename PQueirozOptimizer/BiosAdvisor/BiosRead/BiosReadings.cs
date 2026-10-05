using PQueirozOptimizer.Services;

namespace PQueirozOptimizer.BiosAdvisor;

/// <summary>Valor de uma configuração lido da BIOS pelo SCEWIN: o nome exato nesta BIOS, a opção atual e as possíveis.</summary>
public sealed record BiosReading(string Question, string Value, IReadOnlyList<string> Options);

/// <summary>
/// Leitura completa (só leitura: export do SCEWIN, nada é gravado). Vale para a placa + versão de BIOS em que foi feita;
/// se a pessoa mudar algo na BIOS depois, a página mostra a data da leitura e oferece ler de novo.
/// </summary>
public sealed record BiosReadings(string Fingerprint, DateTime ReadAt, int TotalQuestions, Dictionary<string, BiosReading> Values);

/// <summary>Liga as perguntas exportadas pelo SCEWIN às configurações do Advisor, pelos nomes do banco (biosQuestions).</summary>
public static class BiosSettingMapper
{
    public static BiosReadings Map(IReadOnlyList<BiosSetting> settings, BiosDatabase db, string fingerprint, DateTime readAt)
    {
        var values = new Dictionary<string, BiosReading>(StringComparer.Ordinal);
        foreach (var (settingId, patterns) in db.BiosQuestions)
        {
            // A ordem dos padrões é a preferência; a mesma pergunta pode aparecer em telas escondidas: vale a primeira com opções
            foreach (var pattern in patterns)
            {
                var match = settings.FirstOrDefault(s => s.Options.Count > 0 && s.SelectedIndex >= 0 && s.SelectedIndex < s.Options.Count && SafeMatch(pattern, s.Question.Trim()));
                if (match is null) continue;
                values[settingId] = new BiosReading(match.Question.Trim(), match.Options[match.SelectedIndex].Label, match.Options.Select(o => o.Label).ToList());
                break;
            }
        }
        return new BiosReadings(fingerprint, readAt, settings.Count, values);
    }

    private static bool SafeMatch(System.Text.RegularExpressions.Regex regex, string text)
    {
        try { return regex.IsMatch(text); }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return false; }
    }
}
