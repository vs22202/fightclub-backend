using System.Net.WebSockets;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Players web socket connections

var PlayerMap = new Dictionary<WebSocket, WebSocket>();
var AvailablePlayers = new Stack<WebSocket>();
// Add services to the container.
builder.WebHost.UseUrls("http://localhost:6969");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseWebSockets();
app.Map("/ws", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        byte[] bytes;
        ArraySegment<byte> sendBuffer;

        if (!PlayerMap.ContainsKey(webSocket))
        {
            if (AvailablePlayers.Count == 0)
            {
                AvailablePlayers.Push(webSocket);
                bytes = Encoding.UTF8.GetBytes("Waiting for player 2");
                sendBuffer = new ArraySegment<byte>(bytes, 0, bytes.Length);
                await webSocket.SendAsync(sendBuffer, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            else
            {
                var player2 = AvailablePlayers.Pop();
                bytes = Encoding.UTF8.GetBytes("You are Player 1");
                sendBuffer = new ArraySegment<byte>(bytes, 0, bytes.Length);
                await player2.SendAsync(sendBuffer, WebSocketMessageType.Text, true, CancellationToken.None);
                bytes = Encoding.UTF8.GetBytes("You are Player 2");
                sendBuffer = new ArraySegment<byte>(bytes, 0, bytes.Length);
                await webSocket.SendAsync(sendBuffer, WebSocketMessageType.Text, true, CancellationToken.None);
                PlayerMap.Add(webSocket, player2);
                PlayerMap.Add(player2, webSocket);
            }
        }
        while (webSocket.State == WebSocketState.Open)
        {
            var buffer = new ArraySegment<byte>(new byte[1024]);
            var result = await webSocket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                PlayerMap.TryGetValue(webSocket, out var player2);
                PlayerMap.Remove(webSocket);
                if (player2 != null)
                {
                    PlayerMap.Remove(player2);
                    AvailablePlayers.Push(player2);
                }

                break;
            }
            else if (result.MessageType == WebSocketMessageType.Text)
            {
                var player2 = PlayerMap[webSocket];
                await player2.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});
await app.RunAsync();
