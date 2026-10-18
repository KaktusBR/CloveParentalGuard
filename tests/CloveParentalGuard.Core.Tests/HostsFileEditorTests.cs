namespace CloveParentalGuard.Core.Tests;

public class HostsFileEditorTests
{
    [Fact]
    public void Apply_Adds_Domain_And_Www_Variant()
    {
        var result = HostsFileEditor.Apply("127.0.0.1 localhost\n", new[] { "poki.com" });
        Assert.Contains("0.0.0.0 poki.com", result);
        Assert.Contains("0.0.0.0 www.poki.com", result);
        Assert.Contains("# BEGIN CloveParentalGuard", result);
        Assert.Contains("# END CloveParentalGuard", result);
    }

    [Fact]
    public void Apply_Preserves_Existing_Content()
    {
        var result = HostsFileEditor.Apply("127.0.0.1 localhost\n", new[] { "poki.com" });
        Assert.Contains("127.0.0.1 localhost", result);
    }

    [Fact]
    public void Apply_Twice_Does_Not_Duplicate_Block()
    {
        var once = HostsFileEditor.Apply("127.0.0.1 localhost\n", new[] { "poki.com" });
        var twice = HostsFileEditor.Apply(once, new[] { "crazygames.com" });
        Assert.Single(Occurrences(twice, "# BEGIN CloveParentalGuard"));
        Assert.Contains("crazygames.com", twice);
        Assert.DoesNotContain("poki.com", twice);
    }

    [Fact]
    public void Remove_Strips_Block_And_Keeps_Rest()
    {
        var applied = HostsFileEditor.Apply("127.0.0.1 localhost\n", new[] { "poki.com" });
        var removed = HostsFileEditor.Remove(applied);
        Assert.DoesNotContain("# BEGIN CloveParentalGuard", removed);
        Assert.DoesNotContain("poki.com", removed);
        Assert.Contains("127.0.0.1 localhost", removed);
    }

    private static IEnumerable<int> Occurrences(string text, string token)
    {
        int i = 0;
        while ((i = text.IndexOf(token, i, StringComparison.Ordinal)) != -1) { yield return i; i += token.Length; }
    }
}
