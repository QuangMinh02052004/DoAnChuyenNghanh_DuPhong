using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Bloomie.Hubs
{
    [Authorize(Roles = "Admin")]
    public class NotificationHub : Hub
    {
        // Gọi khi client (Admin) kết nối đến Hub
        public override async Task OnConnectedAsync()
        {
            // Thêm client vào nhóm "Admins"
            await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
            Console.WriteLine($"Client {Context.ConnectionId} đã kết nối và được thêm vào nhóm Admins.");
            await base.OnConnectedAsync();
        }

        // Gọi khi client (Admin) ngắt kết nối
        public override async Task OnDisconnectedAsync(Exception exception)
        {
            // Xóa client khỏi nhóm "Admins"
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Admins");
            Console.WriteLine($"Client {Context.ConnectionId} đã ngắt kết nối và được xóa khỏi nhóm Admins.");
            await base.OnDisconnectedAsync(exception);
        }

        // Gửi thông báo đến tất cả Admin trong nhóm "Admins"
        public async Task SendNotification(string message, string link)
        {
            await Clients.Group("Admins").SendAsync("ReceiveNotification", new { message, link });
            Console.WriteLine($"Đã gửi thông báo đến nhóm Admins: {message}");
        }
    }
}