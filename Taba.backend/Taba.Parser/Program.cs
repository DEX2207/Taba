using Microsoft.EntityFrameworkCore;
using Taba.Infrastucture.Persistence;
using Taba.Parser;
using Taba.Parser.Parsers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient<NineNineNineParser>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", 
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/144.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Add("Origin", "https://999.md");
    client.DefaultRequestHeaders.Add("Referer", "https://999.md/ru/list/computers-and-office-equipment/monitors");
    client.DefaultRequestHeaders.Add("Lang", "ru");
    client.DefaultRequestHeaders.Add("Source", "desktop_redesign");
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();