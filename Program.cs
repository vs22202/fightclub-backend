using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
var builder = WebApplication.CreateBuilder(args);

// Players web socket connections

var PlayerMap = new Dictionary<WebSocket, WebSocket>();
var PlayerToWebSocketMap = new Dictionary<string, WebSocket>();
var WebSocketToPlayerMap = new Dictionary<WebSocket, string>();
var AvailablePlayers = new Stack<WebSocket>();
var Database = new Database();
Database.CreateTable("Players", new Player());

// Add services to the container.
builder.WebHost.UseUrls("http://0.0.0.0:4001");
builder.Services.AddCors();
const string JsonContentType = "application/json";

var app = builder.Build();

// Enable CORS
app.UseCors(policy =>
    policy.AllowAnyOrigin()
          .AllowAnyMethod()
          .AllowAnyHeader());

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
        if (jsonString == null) return;
        Player? player = JsonSerializer.Deserialize<Player>(jsonString);
        if (player == null) return;
        Database.InsertIntoTable("Players", player);
        Console.WriteLine(player.player_name);
        Console.WriteLine(player.player_password);

        context.Response.StatusCode = 201;
        var responseBody = new
        {
            message = "Player registered",
        };
        context.Response.ContentType = JsonContentType;
        var jsonResponse = JsonSerializer.Serialize(responseBody);
        await context.Response.WriteAsync(jsonResponse);
    }
});

app.Map("/loginPlayer", async context =>
{

    if (context.Request.Method == "POST")
    {
        var body = context.Request.Body;

        // save the data from the request body into a data variable
        var data = new byte[1024];
        var count = await body.ReadAsync(data);
        var jsonString = Encoding.UTF8.GetString(data, 0, count);
        if (jsonString == null) return;
        Player? player = JsonSerializer.Deserialize<Player>(jsonString);
        if (player == null) return;
        var selectCmd = Database.GetConnection().CreateCommand();
        selectCmd.CommandText = $"SELECT * FROM Players WHERE player_name = '{player.player_name}' AND player_password = '{player.player_password}'";
        var reader = await selectCmd.ExecuteReaderAsync();
        if (reader.HasRows)
        {
            await reader.ReadAsync();
            context.Response.StatusCode = 200;
            var responseBody = new
            {
                message = "Player logged in",
                player_id = reader["player_id"]
            };
            var jsonResponse = JsonSerializer.Serialize(responseBody);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(jsonResponse);
        }
        else
        {
            context.Response.StatusCode = 400;
            var responseBody = new
            {
                message = "Player not authorized",
            };
            var jsonResponse = JsonSerializer.Serialize(responseBody);
            context.Response.ContentType = JsonContentType;
            await context.Response.WriteAsync(jsonResponse);
        }
    }
});

app.Map("matchWithPlayer", async context =>
{
    if (context.Request.Method == "POST")
    {
        var body = context.Request.Body;
        var data = new byte[1024];
        var count = await body.ReadAsync(data);
        var json_string = Encoding.UTF8.GetString(data, 0, count);
        Player[]? players = JsonSerializer.Deserialize<Player[]>(json_string);
        if (players == null) return;
        var player1_id = players[0].player_id;
        var player2_name = players[1].player_name;
        var selectCmd = Database.GetConnection().CreateCommand();
        selectCmd.CommandText = $"SELECT player_id FROM Players WHERE player_name = '{player2_name}'";
        var reader = await selectCmd.ExecuteReaderAsync();
        if (reader.HasRows)
        {
            await reader.ReadAsync();
            players[1].player_id = reader["player_id"]?.ToString() ?? string.Empty;
        }
        else
        {
            context.Response.StatusCode = 404;
            var responseBody = new
            {
                message = "Player 2 not found",
            };
            var jsonResponse = JsonSerializer.Serialize(responseBody);
            context.Response.ContentType = JsonContentType;
            await context.Response.WriteAsync(jsonResponse);
            return;
        }
        var player2_id = players[1].player_id;


        PlayerToWebSocketMap.TryGetValue(player1_id, out WebSocket? player1_ws);
        PlayerToWebSocketMap.TryGetValue(player2_id, out WebSocket? player2_ws);
        if (player1_ws == null || player2_ws == null) return;

        if (!PlayerMap.TryAdd(player2_ws, player1_ws) && PlayerMap[player2_ws] != player1_ws)
        {
            context.Response.StatusCode = 200;
            var responseBody = new
            {
                message = "Player is already in a match",
            };
            var jsonResponse = JsonSerializer.Serialize(responseBody);
            context.Response.ContentType = JsonContentType;
            await context.Response.WriteAsync(jsonResponse);
        }
        else
        {
            PlayerMap.TryAdd(player1_ws, player2_ws);

            var bytes = Encoding.UTF8.GetBytes("?");
            var sendBuffer = new ArraySegment<byte>(bytes, 0, bytes.Length);
            await player2_ws.SendAsync(sendBuffer, WebSocketMessageType.Text, true, CancellationToken.None);

            context.Response.StatusCode = 200;
            var responseBody = new
            {
                message = "Waiting for player to accept",
            };
            var jsonResponse = JsonSerializer.Serialize(responseBody);
            context.Response.ContentType = JsonContentType;
            await context.Response.WriteAsync(jsonResponse);
        }
    }
});

app.Map("playerAcceptMatch", async context =>
{
    if (context.Request.Method == "POST")
    {

        var body = context.Request.Body;
        var data = new byte[1024];
        var count = await body.ReadAsync(data);
        var response = Encoding.UTF8.GetString(data, 0, count);

        if (!string.IsNullOrEmpty(response))
        {
            var player_id = response[1..];
            var webSocket = PlayerToWebSocketMap[player_id];
            PlayerMap.TryGetValue(webSocket, out var opponent_player);

            var selectCmd = Database.GetConnection().CreateCommand();
            selectCmd.CommandText = $"SELECT player_name FROM Players WHERE player_id = '{player_id}'";
            var reader = await selectCmd.ExecuteReaderAsync();
            string player_name = string.Empty;
            if (reader.HasRows)
            {
                await reader.ReadAsync();
                player_name = reader["player_name"]?.ToString() ?? string.Empty;
            }
            await reader.CloseAsync();
            if (opponent_player != null && WebSocketToPlayerMap.TryGetValue(opponent_player, out var opponent_player_id))
            {
                selectCmd.CommandText = $"SELECT player_name FROM Players WHERE player_id = '{opponent_player_id}'";
            }
            reader = await selectCmd.ExecuteReaderAsync();
            string opponent_player_name = string.Empty;
            if (reader.HasRows)
            {
                await reader.ReadAsync();
                opponent_player_name = reader["player_name"]?.ToString() ?? string.Empty;
            }
            await reader.CloseAsync();
            Console.WriteLine(player_id);
            if (response[0] == 'N')
            {
                var bytes = Encoding.UTF8.GetBytes("R");
                var sendBuffer = new ArraySegment<byte>(bytes, 0, bytes.Length);
                PlayerMap.Remove(webSocket);
                if (opponent_player != null && PlayerMap.Remove(opponent_player))
                {
                    await opponent_player.SendAsync(sendBuffer, WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            else if (response[0] == 'Y')
            {

                if (opponent_player != null)
                {
                    var bytes = Encoding.UTF8.GetBytes("You are Player 1");
                    var sendBuffer = new ArraySegment<byte>(bytes, 0, bytes.Length);
                    await opponent_player.SendAsync(sendBuffer, WebSocketMessageType.Text, true, CancellationToken.None);

                    bytes = Encoding.UTF8.GetBytes("You are Player 2");
                    sendBuffer = new ArraySegment<byte>(bytes, 0, bytes.Length);
                    await webSocket.SendAsync(sendBuffer, WebSocketMessageType.Text, true, CancellationToken.None);

                    bytes = Encoding.UTF8.GetBytes("S" + player_name);
                    sendBuffer = new ArraySegment<byte>(bytes, 0, bytes.Length);
                    await opponent_player.SendAsync(sendBuffer, WebSocketMessageType.Text, true, CancellationToken.None);

                    bytes = Encoding.UTF8.GetBytes("S" + opponent_player_name);
                    sendBuffer = new ArraySegment<byte>(bytes, 0, bytes.Length);
                    await webSocket.SendAsync(sendBuffer, WebSocketMessageType.Text, true, CancellationToken.None);

                }
            }
        }
    }
});

app.Map("matchWithRandomPlayer", async context =>
{
    if (context.Request.Method == "POST")
    {
        var body = context.Request.Body;
        var data = new byte[1024];
        var count = await body.ReadAsync(data);
        var json_string = Encoding.UTF8.GetString(data, 0, count);
        Player? player = JsonSerializer.Deserialize<Player>(json_string);
        if (player == null) return;

        PlayerToWebSocketMap.TryGetValue(player.player_id, out var webSocket);
        if (webSocket == null) return;

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
                PlayerMap.TryAdd(webSocket, player2);
                PlayerMap.TryAdd(player2, webSocket);
            }
        }
    }
});

app.Map("/ws", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

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
                }

                PlayerToWebSocketMap.Remove(WebSocketToPlayerMap[webSocket]);
                break;
            }
            else if (result.MessageType == WebSocketMessageType.Text)
            {
                if (buffer.Array == null) continue;
                var message_text = Encoding.UTF8.GetString(buffer.Array, buffer.Offset, result.Count);
                Console.WriteLine(message_text);
                if (!string.IsNullOrEmpty(message_text))
                {
                    var eventChar = message_text[0];
                    if (eventChar == '_')
                    {
                        var player_id = message_text[1..];
                        PlayerToWebSocketMap.TryAdd(player_id, webSocket);
                        WebSocketToPlayerMap.TryAdd(webSocket, player_id);
                        continue;
                    }
                    else if (eventChar == '*')
                    {
                        PlayerMap.TryGetValue(webSocket, out var opponent_player);
                        PlayerMap.Remove(webSocket);
                        if (opponent_player != null)
                        {
                            PlayerMap.Remove(opponent_player);
                        }

                        continue;
                    }
                }
                var player2 = PlayerMap[webSocket];
                await player2.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        PlayerMap.TryGetValue(webSocket, out var opp_player);
        PlayerMap.Remove(webSocket);
        if (opp_player != null)
        {
            PlayerMap.Remove(opp_player);
        }

        PlayerToWebSocketMap.Remove(WebSocketToPlayerMap[webSocket]);
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});
await app.RunAsync();
