using MySql.Data.MySqlClient;

namespace AlabamaWalkabilityApi.Services;

public class WalkabilityDbContext
{
    private readonly IConfiguration _config;

    public WalkabilityDbContext(IConfiguration config) => _config = config;

    public MySqlConnection CreateConnection() =>
        new(_config.GetConnectionString("Default") ?? "Server=localhost;Database=alabama_walkability;Uid=root;Pwd=;");

}
