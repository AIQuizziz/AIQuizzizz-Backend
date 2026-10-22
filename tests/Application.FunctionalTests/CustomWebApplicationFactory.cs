using System.Data.Common;
using CleanArchitectureBase.Application.Command.UnitTests.Fakes;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Infrastructure.Data;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CleanArchitectureBase.Application.Command.UnitTests;

using static Testing;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly DbConnection _connection;
    private readonly string _connectionString;

    public CustomWebApplicationFactory(DbConnection connection, string connectionString)
    {
        _connection = connection;
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services
                .RemoveAll<IUser>()
                .AddTransient(provider => Mock.Of<IUser>(s => s.UserId == GetUserId()));

            services
                .RemoveAll<DbContextOptions<ApplicationDbContext>>()
                .AddDbContext<ApplicationDbContext>((sp, options) =>
                {
                    options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
                    options.UseNpgsql(_connection);
                });

            // media-ai / MinIO / SMTP giả: test không gọi ra ngoài
            services
                .RemoveAll<IMediaAiClient>()
                .AddSingleton<IMediaAiClient, FakeMediaAiClient>();
            services
                .RemoveAll<IMediaUrlSigner>()
                .AddSingleton<IMediaUrlSigner, FakeMediaUrlSigner>();
            services
                .RemoveAll<IEmailService>()
                .AddTransient(_ => Mock.Of<IEmailService>());

            services.RemoveAll<IHostedService>();
            services.AddHangfire(configuration => configuration
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(options =>
                {
                    options.UseNpgsqlConnection(_connectionString);
                }));
            services.AddHangfireServer();
        });
    }
}
