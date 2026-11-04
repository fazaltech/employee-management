using System.Security.Claims;
using EmployeeApi.Data;
using EmployeeApi.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Services;

public sealed record SupportActor(string Id, string Name, bool IsAdmin, string? OrganizationId);
public sealed record SupportThread(string Id, string OrganizationName, string UserName, DateTime UpdatedAtUtc);

public sealed class SupportChatService(ApplicationDbContext db, IConfiguration configuration)
{
    public async Task<SupportActor> Authorize(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (user is null || user.MustChangePassword || user.SecurityStamp != principal.FindFirstValue("security_stamp") ||
            (configuration.GetValue("Authentication:RequireConfirmedEmail", true) && !user.EmailConfirmed))
            throw new HubException("Your account cannot access support. Please sign in again.");
        var admin = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                           where ur.UserId == id && r.Name == "PlatformAdmin" select ur).AnyAsync();
        if (admin) return new(user.Id, user.FullName, true, null);
        var org = await (from m in db.OrganizationMemberships join o in db.Organizations on m.OrganizationId equals o.Id
                         where m.UserId == id && m.IsActive && o.Status == "Active" &&
                         (m.Role == OrganizationRoles.Owner || m.Role == OrganizationRoles.Manager)
                         select m.OrganizationId).SingleOrDefaultAsync();
        if (org is null) throw new HubException("Support is available only to organization owners and managers.");
        return new(user.Id, user.FullName, false, org);
    }

    public async Task<List<SupportThread>> List(SupportActor actor)
    {
        if (!actor.IsAdmin)
        {
            // The unique index also protects simultaneous connections from creating duplicate threads.
            var id = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO SupportConversations (Id, OrganizationId, UserId, UpdatedAtUtc) VALUES ({id}, {actor.OrganizationId}, {actor.Id}, {now}) ON CONFLICT (OrganizationId, UserId) DO NOTHING");
        }
        var threads = await (from c in db.SupportConversations.AsNoTracking()
                      join o in db.Organizations on c.OrganizationId equals o.Id
                      join u in db.Users on c.UserId equals u.Id
                      where actor.IsAdmin || (c.UserId == actor.Id && c.OrganizationId == actor.OrganizationId)
                      orderby c.UpdatedAtUtc descending, c.Id
                      select new SupportThread(c.Id, o.Name, u.FullName, c.UpdatedAtUtc)).ToListAsync();
        return threads.Select(t => t with { UpdatedAtUtc = DateTime.SpecifyKind(t.UpdatedAtUtc, DateTimeKind.Utc) }).ToList();
    }

    public async Task<SupportConversation> Conversation(SupportActor actor, string id)
    {
        var conversation = await db.SupportConversations.SingleOrDefaultAsync(x => x.Id == id);
        if (conversation is null || (!actor.IsAdmin && (conversation.UserId != actor.Id || conversation.OrganizationId != actor.OrganizationId)))
            throw new HubException("Conversation is unavailable.");
        return conversation;
    }

    public async Task<List<SupportMessage>> History(SupportActor actor, string id, long? before)
    {
        await Conversation(actor, id);
        var messages = await db.SupportMessages.AsNoTracking().Where(x => x.ConversationId == id && (!before.HasValue || x.Id < before.Value))
            .OrderByDescending(x => x.Id).Take(50).ToListAsync();
        messages.Reverse();
        foreach (var message in messages) message.SentAtUtc = DateTime.SpecifyKind(message.SentAtUtc, DateTimeKind.Utc);
        return messages;
    }

    public async Task<SupportMessage> Send(SupportActor actor, string id, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > 4000)
            throw new HubException("Enter a message between 1 and 4,000 characters.");
        var conversation = await Conversation(actor, id);
        var message = new SupportMessage { ConversationId = id, SenderId = actor.Id, SenderName = actor.Name, FromAdmin = actor.IsAdmin, Text = text.Trim() };
        conversation.UpdatedAtUtc = message.SentAtUtc;
        db.SupportMessages.Add(message);
        await db.SaveChangesAsync();
        return message;
    }

    public async Task<string[]> Recipients(string conversationId)
    {
        var userId = await db.SupportConversations.Where(x => x.Id == conversationId).Select(x => x.UserId).SingleAsync();
        var admins = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                            where r.Name == "PlatformAdmin" select ur.UserId).ToListAsync();
        return admins.Append(userId).Distinct().ToArray();
    }
}
