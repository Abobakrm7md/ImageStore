using ImageStore.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ImageStore.Api.OpenApi;

/// <summary>
/// Documents which operations need the authentication cookie and which need the anti-forgery
/// header. (Swagger UI adds the header automatically from the XSRF-TOKEN cookie.)
/// </summary>
internal sealed class SecurityOperationFilter : IOperationFilter
{
    public const string CookieSchemeName = "cookieAuth";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (!metadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(CookieSchemeName, context.Document)] = [],
            });

            operation.Responses ??= [];
            operation.Responses.TryAdd("401", new OpenApiResponse { Description = "The request is not authenticated." });
        }

        var method = context.ApiDescription.HttpMethod;
        if (method is not null && !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method))
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = AntiforgeryDefaults.HeaderName,
                In = ParameterLocation.Header,
                Required = false,
                Description = "Anti-forgery token: the value of the XSRF-TOKEN cookie (GET /api/auth/csrf-token). Added automatically by Swagger UI.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            });
        }
    }
}
