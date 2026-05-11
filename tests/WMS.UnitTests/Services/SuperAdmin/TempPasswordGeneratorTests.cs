using WMS.BLL.Services.Security;
using WMS.BLL.Services.SuperAdmin;

namespace WMS.UnitTests.Services.SuperAdmin;

// Phase 27 — verify the generator produces passwords that pass
// PasswordPolicy + has the expected character class coverage. Twenty-
// pass loop confirms class guarantees across multiple runs (each
// invocation re-shuffles).
public class TempPasswordGeneratorTests
{
    [Fact]
    public void Generate_DefaultLength_Is16()
    {
        Assert.Equal(16, TempPasswordGenerator.Generate().Length);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(20)]
    [InlineData(32)]
    public void Generate_CustomLength_MatchesRequested(int length)
    {
        Assert.Equal(length, TempPasswordGenerator.Generate(length).Length);
    }

    [Fact]
    public void Generate_LengthBelowMinimum_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TempPasswordGenerator.Generate(8));
    }

    [Fact]
    public void Generate_AlwaysPassesPasswordPolicy()
    {
        for (int i = 0; i < 50; i++)
        {
            var pw = TempPasswordGenerator.Generate();
            Assert.Null(PasswordPolicy.Validate(pw));
        }
    }

    [Fact]
    public void Generate_ContainsAllCharacterClasses()
    {
        // Twenty runs to keep flakiness vanishing — generator
        // guarantees ≥2 from each class by construction.
        for (int i = 0; i < 20; i++)
        {
            var pw = TempPasswordGenerator.Generate();
            Assert.Contains(pw, c => char.IsUpper(c));
            Assert.Contains(pw, c => char.IsLower(c));
            Assert.Contains(pw, c => char.IsDigit(c));
            Assert.Contains(pw, c => "!@#$%^&*?".Contains(c));
        }
    }

    [Fact]
    public void Generate_ProducesUniqueOutputsAcrossCalls()
    {
        // 100 cryptographic 16-char passwords from a pool size of >60
        // chars — birthday-paradox collision probability is negligible.
        var set = new HashSet<string>();
        for (int i = 0; i < 100; i++)
            set.Add(TempPasswordGenerator.Generate());
        Assert.Equal(100, set.Count);
    }

    [Fact]
    public void Generate_ExcludesConfusableCharacters()
    {
        // 50 runs — confirm 0/O/1/l/I never appear (intentional
        // exclusion to reduce email/clipboard typing errors).
        for (int i = 0; i < 50; i++)
        {
            var pw = TempPasswordGenerator.Generate();
            Assert.DoesNotContain('0', pw);
            Assert.DoesNotContain('O', pw);
            Assert.DoesNotContain('1', pw);
            Assert.DoesNotContain('l', pw);
            Assert.DoesNotContain('I', pw);
        }
    }
}
