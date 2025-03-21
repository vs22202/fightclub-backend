public class Player
{
    public string player_id { get; set; }
    public string player_name { get; set; }
    public string player_password { get; set; }

    public Player()
    {
        player_id = Guid.NewGuid().ToString();
        player_name = "Player";
        player_password = "Password";
    }
}