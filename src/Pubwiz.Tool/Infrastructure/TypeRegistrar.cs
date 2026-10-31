using Microsoft.Extensions.DependencyInjection;

namespace Pubwiz.Tool.Infrastructure;

/// <summary>
/// Lets Spectre.Console.Cli create commands through Microsoft.Extensions.DependencyInjection,
/// so commands get their dependencies through their constructors.
/// </summary>
public sealed class TypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
    public ITypeResolver Build() => new TypeResolver(services.BuildServiceProvider());

    public void Register(Type service, Type implementation) => services.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation) => services.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory) => services.AddSingleton(service, _ => factory());
}

public sealed class TypeResolver(ServiceProvider provider) : ITypeResolver, IDisposable
{
    public object? Resolve(Type? type) => type is null ? null : provider.GetService(type);

    public void Dispose() => provider.Dispose();
}
