using Esatto.Umbraco.Backoffice.SharedPreviewLink;
using Microsoft.AspNetCore.DataProtection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

#if DEBUG
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
#endif

// Persist the DataProtection key ring so shared preview links (and backoffice sessions)
// survive app restarts.
builder.Services.AddDataProtection()
    .SetApplicationName("UmbracoKalaset26")
    .PersistKeysToFileSystem(new DirectoryInfo(
        Path.Combine(builder.Environment.ContentRootPath, "umbraco", "Data", "DataProtectionKeys")));

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

WebApplication app = builder.Build();


await app.BootUmbracoAsync();

// Must run before UseUmbraco() so the preview cookie it sets affects content resolution.
app.UsePreviewLink();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();
