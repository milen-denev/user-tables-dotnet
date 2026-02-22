using System;
using Microsoft.Extensions.DependencyInjection;
using UserTables.Client.Configuration;

namespace UserTables.Client.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUserTablesContext<TContext>(
        this IServiceCollection services,
        Action<UserTablesContextOptionsBuilder> configure)
        where TContext : UserTablesDbContext
    {
        var builder = new UserTablesContextOptionsBuilder();
        configure(builder);
        var options = builder.Build();

        services.AddSingleton(options);
        services.AddScoped<TContext>();
        return services;
    }
}