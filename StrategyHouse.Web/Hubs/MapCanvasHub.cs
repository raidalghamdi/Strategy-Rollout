using Microsoft.AspNetCore.SignalR;

namespace StrategyHouse.Web.Hubs;

/// <summary>
/// SignalR hub for the Movement 2 collaborative canvas. iPads in a session room
/// connect to a session-scoped channel; placements and commitment links propagate
/// in real time to all connected clients (cluster screens mirror this).
/// </summary>
public class MapCanvasHub : Hub
{
    public async Task JoinSession(string sessionCode)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionCode);
    }

    public async Task LeaveSession(string sessionCode)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionCode);
    }

    public async Task BroadcastPlacement(string sessionCode, object placement)
    {
        await Clients.OthersInGroup(sessionCode).SendAsync("PlacementAdded", placement);
    }

    public async Task BroadcastCommitment(string sessionCode, object commitment)
    {
        await Clients.OthersInGroup(sessionCode).SendAsync("CommitmentAdded", commitment);
    }

    public async Task BroadcastSignature(string sessionCode, object signature)
    {
        await Clients.OthersInGroup(sessionCode).SendAsync("SignatureAdded", signature);
    }
}
