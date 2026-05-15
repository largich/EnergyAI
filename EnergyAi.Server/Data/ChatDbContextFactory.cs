using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EnergyAi.Server.Data;

// Used by `dotnet ef migrations` at design time only — not part of the runtime DI graph.
public class ChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
    public ChatDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<ChatDbContext>()
            .UseSqlServer("Server=.;Database=Energy;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new ChatDbContext(opts);
    }
}
