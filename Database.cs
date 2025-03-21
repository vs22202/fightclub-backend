using Microsoft.Data.Sqlite;
public class Database {
    private readonly SqliteConnection connection; 
    public Database() {
        connection = new SqliteConnection("Data Source=database.db");
        connection.Open();
    }
    public void CreateTable<T>(string tableName, T obj) {
        using var transaction = connection.BeginTransaction();
        var members = typeof(T).GetProperties();
        var memberNames = members.Select(m => m.Name);
        string fieldNames = String.Join(",", memberNames.Select(name => $"{name} TEXT"));

        var createCmd = connection.CreateCommand();
        createCmd.CommandText = $"CREATE TABLE IF NOT EXISTS {tableName} ({fieldNames})";
        createCmd.ExecuteNonQuery();

        // Check for new columns and add them if necessary
        var existingColumnsCmd = connection.CreateCommand();
        existingColumnsCmd.CommandText = $"PRAGMA table_info({tableName})";
        var existingColumns = new List<string>();
        using (var reader = existingColumnsCmd.ExecuteReader()) {
            while (reader.Read()) {
                existingColumns.Add(reader.GetString(1));
            }
        }

        var newColumns = members.Select(member => member.Name).Where(name => !existingColumns.Contains(name));
        foreach (var column in newColumns) {
            var addColumnCmd = connection.CreateCommand();
            addColumnCmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {column} TEXT";
            addColumnCmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }
    public void InsertIntoTable<T>(string tableName , T obj) {
        using var transaction = connection.BeginTransaction();

        var insertCmd = connection.CreateCommand();
        var members = typeof(T).GetProperties();
        var memberNames = members.Select(m => m.Name);
        string fieldNames = String.Join(",",memberNames);
        var valuesArray = members.Select(m => $"'{m.GetValue(obj)}'");
        var values = String.Join(",",valuesArray);

        insertCmd.CommandText = $"INSERT INTO {tableName} ({fieldNames}) VALUES ({values})";
        insertCmd.ExecuteNonQuery();
        transaction.Commit();
    }

    public SqliteConnection GetConnection() {
        return connection;
    }
    ~Database() {
        connection.Close();
    }
}