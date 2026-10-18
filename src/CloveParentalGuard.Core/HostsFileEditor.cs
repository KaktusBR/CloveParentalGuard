using System.Text;
using System.Text.RegularExpressions;

namespace CloveParentalGuard.Core;

public static class HostsFileEditor
{
    private const string Begin = "# BEGIN CloveParentalGuard";
    private const string End = "# END CloveParentalGuard";

    public static string Apply(string currentHosts, IEnumerable<string> domains)
    {
        var cleaned = Remove(currentHosts).TrimEnd('\r', '\n');
        var sb = new StringBuilder();
        if (cleaned.Length > 0) sb.Append(cleaned).Append('\n');
        sb.Append(Begin).Append('\n');
        foreach (var domain in domains)
        {
            var d = domain.Trim();
            if (d.Length == 0) continue;
            sb.Append("0.0.0.0 ").Append(d).Append('\n');
            sb.Append("0.0.0.0 www.").Append(d).Append('\n');
        }
        sb.Append(End).Append('\n');
        return sb.ToString();
    }

    public static string Remove(string currentHosts)
    {
        var pattern = Regex.Escape(Begin) + ".*?" + Regex.Escape(End) + @"\r?\n?";
        var result = Regex.Replace(currentHosts, pattern, "", RegexOptions.Singleline);
        return result;
    }
}
