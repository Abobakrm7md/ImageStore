using Microsoft.AspNetCore.Identity;

namespace ImageStore.Infrastructure.Identity;

/// <summary>ASP.NET Core Identity user with a GUID key.</summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public DateTime CreatedAtUtc { get; set; }
}
