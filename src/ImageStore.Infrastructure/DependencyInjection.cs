using ImageStore.Application.Auth;
using ImageStore.Application.Images;
using ImageStore.Application.Images.Storage;
using ImageStore.Application.Posts;
using ImageStore.Application.Social;
using ImageStore.Infrastructure.HealthChecks;
using ImageStore.Infrastructure.Identity;
using ImageStore.Infrastructure.Persistence;
using ImageStore.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ImageStore.Infrastructure;

public static class DependencyInjection
{
    public const string TestingEnvironmentName = "Testing";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath), "Storage:RootPath must be configured.")
            .ValidateOnStart();
        services.AddOptions<StorageMaintenanceOptions>()
            .Bind(configuration.GetSection(StorageMaintenanceOptions.SectionName))
            .Validate(
                options => options.IntervalMinutes > 0 && options.StagingFileMaxAgeMinutes > 0 && options.OrphanedFileMinAgeMinutes > 0,
                "StorageMaintenance intervals and ages must be positive.")
            .ValidateOnStart();

        // Options are resolved lazily so that test hosts and environment overrides are honoured.
        services.AddDbContext<ImageStoreDbContext>(ConfigureDatabase);
        services.AddScoped<ImageStore.Application.Notifications.INotificationInbox, ImageStore.Infrastructure.Notifications.NotificationInbox>();
        services.AddScoped<ImageStore.Infrastructure.Notifications.NotificationDispatcher>();
        services.AddHostedService<ImageStore.Infrastructure.Notifications.NotificationWorker>();
        services.AddScoped<ImageStore.Application.Chat.IChatService, ChatService>();
        services.AddScoped<IImageRepository, ImageRepository>();
        services.AddScoped<ISocialRepository, SocialRepository>();
        services.AddScoped<IPostRepository, PostRepository>();
        services.AddScoped<ICommentRepository, CommentRepository>();
        services.AddScoped<IPostReactionRepository, PostReactionRepository>();

        services.AddSingleton<LocalImageFileStorage>();
        services.AddSingleton<IImageFileStorage>(provider => provider.GetRequiredService<LocalImageFileStorage>());
        services.AddSingleton<StorageMaintenanceService>();
        services.AddHostedService(provider => provider.GetRequiredService<StorageMaintenanceService>());

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 4;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ImageStoreDbContext>()
            .AddSignInManager();
        services.AddScoped<IIdentityService, IdentityService>();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database")
            .AddCheck<StorageHealthCheck>("storage");

        return services;
    }

    private static void ConfigureDatabase(IServiceProvider provider, DbContextOptionsBuilder builder)
    {
        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var environment = provider.GetRequiredService<IHostEnvironment>();

        switch (options.Provider)
        {
            case DatabaseProvider.SqlServer:
                var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString(DatabaseOptions.ConnectionStringName);
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException(
                        $"The '{DatabaseOptions.ConnectionStringName}' connection string is not configured. " +
                        $"Set it with the ConnectionStrings__{DatabaseOptions.ConnectionStringName} environment variable or 'dotnet user-secrets'.");
                }

                builder.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null));
                break;

            case DatabaseProvider.InMemory:
                if (!environment.IsDevelopment() && !environment.IsEnvironment(TestingEnvironmentName))
                {
                    throw new InvalidOperationException("The InMemory database provider is only allowed in the Development and Testing environments.");
                }

                builder.UseInMemoryDatabase(options.InMemoryDatabaseName);
                break;

            default:
                throw new InvalidOperationException($"Unsupported database provider '{options.Provider}'.");
        }
    }
}
