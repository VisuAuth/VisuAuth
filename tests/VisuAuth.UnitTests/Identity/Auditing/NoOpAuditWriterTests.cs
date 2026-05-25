using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Identity.DependencyInjection;
using VisuAuth.Identity.Auditing;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace VisuAuth.UnitTests.Identity.Auditing;

/// <summary>
/// The default writer must accept every event silently — handler code that
/// calls <c>_audit.WriteAsync(...)</c> needs to stay zero-cost when the
/// consumer hasn't opted into <c>AddVisuAuthAuditLog</c>. Also pins down
/// the DI lifecycle so the writer is always present after
/// <c>UseAspNetIdentity</c>.
/// </summary>
public sealed class NoOpAuditWriterTests
{
    [Fact]
    public async Task WriteAsync_AcceptsAnyEvent_AndReturnsCompletedTask()
    {
        var writer = new NoOpAuditWriter();
        await writer.WriteAsync(new AuditEvent
        {
            Action = "X",
            TargetType = "Y",
            TargetId = "z",
        });
        // The act of completing without throwing IS the assertion.
        true.Should().BeTrue();
    }

    [Fact]
    public void AddVisuAuthIdentityAdapter_RegistersDefaultWriter_ResolvedByDi()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddVisuAuthIdentityAdapter<IdentityUser>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var writer = scope.ServiceProvider.GetService<IAuditWriter>();

        writer.Should().NotBeNull("default no-op writer must resolve out of the Identity adapter");
        writer.Should().BeOfType<NoOpAuditWriter>();
    }
}
