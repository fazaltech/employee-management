using EmployeeApi.Models;
using EmployeeApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EmployeeApi.Hubs;

[Authorize]
public sealed class SupportHub(SupportChatService chat, ILogger<SupportHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await chat.Authorize(Context.User!);
        await base.OnConnectedAsync();
    }

    public async Task<List<SupportThread>> ListConversations()
        => await chat.List(await chat.Authorize(Context.User!));

    public async Task<List<SupportMessage>> GetMessages(string conversationId, long? before = null)
        => await chat.History(await chat.Authorize(Context.User!), conversationId, before);

    public async Task<SupportMessage> SendMessage(string conversationId, string text)
    {
        var message = await chat.Send(await chat.Authorize(Context.User!), conversationId, text);
        // Only an invalidation is pushed. Fetching any content rechecks current account and membership access.
        try { await Clients.Users(await chat.Recipients(conversationId)).SendAsync("SupportChanged"); }
        catch (Exception ex) { logger.LogWarning("Support notification failed ({ExceptionType}); message was saved.", ex.GetType().Name); }
        return message;
    }
}
