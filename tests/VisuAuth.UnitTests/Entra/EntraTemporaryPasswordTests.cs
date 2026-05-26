using FluentAssertions;
using VisuAuth.EntraCore.Security;
using Xunit;

namespace VisuAuth.UnitTests.Entra;

/// <summary>
/// Properties of <see cref="EntraTemporaryPassword"/>. The Entra default
/// password policy is 8+ chars, 3 of 4 classes — our generator targets all
/// 4 + length 12. The tests assert the contract holds across many draws so
/// a regression in the alphabet or the shuffle is caught.
/// </summary>
public sealed class EntraTemporaryPasswordTests
{
    [Fact]
    public void Generate_Is12CharsLong()
    {
        for (var i = 0; i < 50; i++)
        {
            EntraTemporaryPassword.Generate().Length.Should().Be(12);
        }
    }

    [Fact]
    public void Generate_ContainsAtLeastOneCharacterFromEachClass()
    {
        for (var i = 0; i < 50; i++)
        {
            var pwd = EntraTemporaryPassword.Generate();
            pwd.Should().Match(p => p.Any(char.IsUpper), "needs at least one uppercase letter to satisfy Entra default policy");
            pwd.Should().Match(p => p.Any(char.IsLower), "needs at least one lowercase letter");
            pwd.Should().Match(p => p.Any(char.IsDigit), "needs at least one digit");
            pwd.Should().Match(p => p.Any(c => !char.IsLetterOrDigit(c)), "needs at least one symbol");
        }
    }

    [Fact]
    public void Generate_AvoidsVisuallyAmbiguousCharacters()
    {
        var ambiguous = new[] { '0', 'O', 'I', 'l', '1' };
        for (var i = 0; i < 50; i++)
        {
            var pwd = EntraTemporaryPassword.Generate();
            pwd.Any(c => ambiguous.Contains(c))
                .Should().BeFalse($"'{pwd}' must avoid 0/O/I/l/1 so the admin can read it out loud");
        }
    }

    [Fact]
    public void Generate_ProducesDistinctValuesAcrossInvocations()
    {
        // CSPRNG-backed → collisions are astronomically unlikely; this
        // catches regressions where someone accidentally seeds Random
        // with a constant. 100 draws of 12-char passwords means a
        // collision would imply something is very wrong.
        var seen = new HashSet<string>();
        for (var i = 0; i < 100; i++)
        {
            seen.Add(EntraTemporaryPassword.Generate());
        }
        seen.Count.Should().BeGreaterThan(95,
            "CSPRNG output should produce essentially all-unique 12-char passwords across 100 draws");
    }
}
