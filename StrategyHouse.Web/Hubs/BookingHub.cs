using Microsoft.AspNetCore.SignalR;

namespace StrategyHouse.Web.Hubs;

/// <summary>
/// SignalR hub for the public slot booking page. All clients (admins + department
/// heads) join the global "booking" group; when a slot is booked, released, or
/// edited, every connected client receives a "SlotsChanged" event and refreshes
/// the visible status (e.g. "1/2 محجوز" → "محجوز بالكامل").
/// </summary>
public class BookingHub : Hub
{
    public const string GroupName = "booking";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName);
        await base.OnDisconnectedAsync(exception);
    }
}
