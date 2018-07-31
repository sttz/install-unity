using System;
using Microsoft.Extensions.Logging;

namespace sttz.ConsoleLogger
{

[ProviderAlias("Console")]
public class ConsoleLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    static readonly Func<string, LogLevel, bool> trueFilter = (cat, level) => true;

    Func<string, LogLevel, bool> filter;
    bool includeScopes;
    IExternalScopeProvider scopeProvider;

    public ConsoleLoggerProvider(Func<string, LogLevel, bool> filter = null, bool includeScopes = false)
    {
        this.filter = filter ?? trueFilter;
        this.includeScopes = includeScopes;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new ConsoleLogger(categoryName, filter, includeScopes ? scopeProvider : null);
    }

    public void Dispose()
    {
        // NOP
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        this.scopeProvider = scopeProvider;
    }
}

public static class ConsoleLoggerExtensions
{
    public static ILoggerFactory AddConsole(this ILoggerFactory factory)
    {
        return factory.AddConsole(includeScopes: false);
    }

    public static ILoggerFactory AddConsole(this ILoggerFactory factory, bool includeScopes)
    {
        factory.AddConsole((n, l) => l >= LogLevel.Information, includeScopes);
        return factory;
    }

    public static ILoggerFactory AddConsole(this ILoggerFactory factory, LogLevel minLevel)
    {
        factory.AddConsole(minLevel, includeScopes: false);
        return factory;
    }

    public static ILoggerFactory AddConsole(
        this ILoggerFactory factory,
        LogLevel minLevel,
        bool includeScopes)
    {
        factory.AddConsole((category, logLevel) => logLevel >= minLevel, includeScopes);
        return factory;
    }

    public static ILoggerFactory AddConsole(
        this ILoggerFactory factory,
        Func<string, LogLevel, bool> filter)
    {
        factory.AddConsole(filter, includeScopes: false);
        return factory;
    }

    public static ILoggerFactory AddConsole(
        this ILoggerFactory factory,
        Func<string, LogLevel, bool> filter,
        bool includeScopes)
    {
        factory.AddProvider(new ConsoleLoggerProvider(filter, includeScopes));
        return factory;
    }
}

}