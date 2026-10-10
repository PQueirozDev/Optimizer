using System.Text.RegularExpressions;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer.Models;

public sealed record ActivityEntry(string Raw, string Time, string Level, string Message)
{
    public string Heading => Time + " · " + Translator.Tr(Level switch
    { "ERROR" => "Erros", "WARN" => "Avisos", "SUCCESS" => "Sucesso", _ => "Informações" });

    public static ActivityEntry Parse(string line)
    {
        var match = Regex.Match(line, @"^\[(?<time>[^\]]+)\]\s*\[(?<level>[^\]]+)\]\s*(?<message>.*)$");
        return new(line, match.Success ? match.Groups["time"].Value : "—",
            match.Success ? match.Groups["level"].Value : "INFO",
            Translator.Tr(match.Success ? match.Groups["message"].Value : line));
    }

    public bool Matches(string query, string category) =>
        (category.Length == 0 || Level == category) &&
        (query.Length == 0 || Raw.Contains(query, StringComparison.OrdinalIgnoreCase) ||
         Message.Contains(query, StringComparison.OrdinalIgnoreCase));
}
