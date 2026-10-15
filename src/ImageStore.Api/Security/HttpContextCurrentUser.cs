using System.Security.Claims;
using ImageStore.Application.Common;

namespace ImageStore.Api.Security;

/// <summary>Reads the user identifier from the authenticated cookie principal.</summary>
internal sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var principal = httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            return Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
        }
    }
}
