using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Players web socket connections

var PlayerMap = new Dictionary<WebSocket, WebSocket>();
var AvailablePlayers = new Stack<WebSocket>();
var Database = new Database();
Database.CreateTable("Players", new Player());

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
// A post api to register the player

app.Map("/registerPlayer", async context =>
{
    if (context.Request.Method == "POST")
    {
        Console.WriteLine("here");
        var body = context.Request.Body;

        // save the data from the request body into a data variable
        var data = new byte[1024];
        var count = await body.ReadAsync(data);
        var jsonString = Encoding.UTF8.GetString(data, 0, count);
        if(jsonString == null) return;
        Player player = JsonSerializer.Deserialize<Player>(jsonString);
        if(player == null) return;
        Database.InsertIntoTable("Players", player);
        Console.WriteLine(player.player_name);
        Console.WriteLine(player.player_password);

        context.Response.StatusCode = 201;
        await context.Response.WriteAsync("Player registered");
    }
});

app.Map("/loginPlayer", async context =>{
    
    if (context.Request.Method == "POST")
    {
        var body = context.Request.Body;

        // save the data from the request body into a data variable
        var data = new byte[1024];
        var count = await body.ReadAsync(data);
        var jsonString = Encoding.UTF8.GetString(data, 0, count);
        if(jsonString == null) return;
        Player player = JsonSerializer.Deserialize<Player>(jsonString);
        if(player == null) return;
        var selectCmd = Database.GetConnection().CreateCommand();
        selectCmd.CommandText = $"SELECT * FROM Players WHERE player_name = '{player.player_name}' AND player_password = '{player.player_password}'";
        var reader = selectCmd.ExecuteReader();
        if(reader.HasRows){
            context.Response.StatusCode = 201;
            await context.Response.WriteAsync("Player logged in");
        }
        else{
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Player not authorized");
        }
    }
});
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
