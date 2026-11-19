using HealthOS.Core.Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HealthOS.Core.Identity.Data;

internal sealed class UserDirectory(IdentityDbContext db, TimeProvider clock) : IUserDirectory
{
    public async Task<Guid> EnsureUserAsync(ExternalIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Subject);

        var now = clock.GetUtcNow();
        var newId = Guid.CreateVersion7(now);

        // Single atomic upsert: safe under concurrent first sign-ins of the same user.
        var ids = await db.Database.SqlQuery<Guid>($"""
            INSERT INTO core_identity.user_account (id, issuer, subject, display_name, email, created_at, last_sign_in_at)
            VALUES ({newId}, {identity.Issuer}, {identity.Subject}, {identity.DisplayName}, {identity.Email}, {now}, {now})
            ON CONFLICT (issuer, subject) DO UPDATE
               SET display_name = EXCLUDED.display_name,
                   email = EXCLUDED.email,
                   last_sign_in_at = EXCLUDED.last_sign_in_at
            RETURNING id AS "Value"
            """).ToListAsync(cancellationToken).ConfigureAwait(false);

        return ids.Single();
    }
}
