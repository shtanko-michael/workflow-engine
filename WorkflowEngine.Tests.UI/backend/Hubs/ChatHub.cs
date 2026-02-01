using Microsoft.AspNetCore.SignalR;

namespace WorkflowEngine.Tests.UI.Backend.Hubs;

public class ChatHub : Hub
{
    public override Task OnConnectedAsync()
    {
        return base.OnConnectedAsync();
    }
    public Task JoinDialog(string dialogId)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, dialogId);
    }

    public Task LeaveDialog(string dialogId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, dialogId);
    }
}
