using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

// Startet die App in-memory und ersetzt die Systemuhr durch eine feste Zeit,
// damit die Fälligkeitsdaten der Beispiel-Todos in jedem Testlauf gleich sind.
public class TodoApiFactory : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var clock = new FakeTimeProvider(Now);
            clock.SetLocalTimeZone(TimeZoneInfo.Utc);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
    }
}
