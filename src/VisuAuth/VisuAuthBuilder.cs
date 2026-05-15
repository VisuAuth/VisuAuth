using Microsoft.Extensions.DependencyInjection;

namespace VisuAuth;

internal sealed class VisuAuthBuilder(IServiceCollection services) : IVisuAuthBuilder
{
    public IServiceCollection Services { get; } = services;
}
