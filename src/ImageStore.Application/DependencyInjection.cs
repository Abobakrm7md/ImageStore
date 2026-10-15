using ImageStore.Application.Auth;
using ImageStore.Application.Images;
using ImageStore.Application.Images.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ImageStore.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the application use cases. The host binds <see cref="ImageUploadOptions"/> from
    /// configuration and provides an <see cref="Common.ICurrentUser"/> implementation.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ImageUploadOptions>, ImageUploadOptionsValidator>());
        services.AddScoped<IImageService, ImageService>();
        services.AddScoped<IAuthService, AuthService>();
        return services;
    }
}
