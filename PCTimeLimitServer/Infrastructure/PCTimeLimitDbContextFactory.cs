using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PCTimeLimitServer.Infrastructure;

public sealed class PCTimeLimitDbContextFactory : IDesignTimeDbContextFactory<PCTimeLimitDbContext>
{
    public PCTimeLimitDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PCTimeLimitDbContext>();
        optionsBuilder.UseSqlite("Data Source=pctimelimit.dev.db");
        return new PCTimeLimitDbContext(optionsBuilder.Options);
    }
}
