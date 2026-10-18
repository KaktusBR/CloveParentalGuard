namespace CloveParentalGuard.Core.Tests;

public class CodeHasherTests
{
    [Fact]
    public void Verify_Returns_True_For_Correct_Code()
    {
        var hashed = CodeHasher.Hash("secret123");
        Assert.True(CodeHasher.Verify("secret123", hashed));
    }

    [Fact]
    public void Verify_Returns_False_For_Wrong_Code()
    {
        var hashed = CodeHasher.Hash("secret123");
        Assert.False(CodeHasher.Verify("wrong", hashed));
    }

    [Fact]
    public void Hash_Uses_Random_Salt_So_Two_Hashes_Differ()
    {
        var a = CodeHasher.Hash("same");
        var b = CodeHasher.Hash("same");
        Assert.NotEqual(a.Salt, b.Salt);
        Assert.NotEqual(a.Hash, b.Hash);
    }

    [Fact]
    public void Plaintext_Never_Appears_In_HashedCode()
    {
        var hashed = CodeHasher.Hash("plaintextcode");
        Assert.DoesNotContain("plaintextcode", hashed.Salt + hashed.Hash);
    }
}
