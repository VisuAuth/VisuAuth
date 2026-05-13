using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using VisuAuth.Identity.Users;
using Xunit;

namespace VisuAuth.UnitTests.Identity.Users;

public sealed class TemporaryPasswordGeneratorTests
{
    [Fact]
    public void Generate_WithDefaultOptions_ReturnsAtLeastSixteenCharacters()
    {
        var options = new PasswordOptions();

        var password = TemporaryPasswordGenerator.Generate(options);

        // Generator floors at 16 regardless of policy so the result is hard to
        // brute force even when the policy is lax.
        password.Length.Should().BeGreaterThanOrEqualTo(16);
    }

    [Fact]
    public void Generate_WithRequireUppercase_IncludesAtLeastOneUppercaseLetter()
    {
        var options = new PasswordOptions
        {
            RequiredLength = 8,
            RequireUppercase = true,
            RequireLowercase = false,
            RequireDigit = false,
            RequireNonAlphanumeric = false,
        };

        var password = TemporaryPasswordGenerator.Generate(options);

        password.Should().MatchRegex("[A-Z]", "the generator must honour RequireUppercase");
    }

    [Fact]
    public void Generate_WithRequireDigit_IncludesAtLeastOneDigit()
    {
        var options = new PasswordOptions
        {
            RequiredLength = 8,
            RequireUppercase = false,
            RequireLowercase = false,
            RequireDigit = true,
            RequireNonAlphanumeric = false,
        };

        var password = TemporaryPasswordGenerator.Generate(options);

        password.Should().MatchRegex("[0-9]");
    }

    [Fact]
    public void Generate_WithRequireNonAlphanumeric_IncludesAtLeastOneSymbol()
    {
        var options = new PasswordOptions
        {
            RequiredLength = 8,
            RequireUppercase = false,
            RequireLowercase = false,
            RequireDigit = false,
            RequireNonAlphanumeric = true,
        };

        var password = TemporaryPasswordGenerator.Generate(options);

        password.Should().MatchRegex(@"[!@#$%^&*]");
    }

    [Fact]
    public void Generate_OnRepeatedInvocations_ReturnsDistinctPasswords()
    {
        var options = new PasswordOptions();

        var passwords = Enumerable.Range(0, 25)
            .Select(_ => TemporaryPasswordGenerator.Generate(options))
            .ToList();

        // Cryptographic RNG should make collisions astronomically unlikely
        // even in a small sample.
        passwords.Distinct().Should().HaveCount(passwords.Count,
            "each invocation must produce a unique password");
    }

    [Fact]
    public void Generate_NeverIncludesVisuallyAmbiguousCharacters()
    {
        var options = new PasswordOptions
        {
            RequiredLength = 16,
            RequireUppercase = true,
            RequireLowercase = true,
            RequireDigit = true,
            RequireNonAlphanumeric = false,
        };

        // Run many times to make sure the constraint holds across the random
        // surface, not just one happy generation.
        for (var i = 0; i < 50; i++)
        {
            var password = TemporaryPasswordGenerator.Generate(options);

            password.Should().NotContainAny(["I", "O", "l", "0", "1"],
                "admins read these passwords aloud — visually ambiguous characters must not slip in");
        }
    }
}
