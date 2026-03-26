using MarkdownKB.Core.Services;
using MarkdownKB.Search;
using MarkdownKB.Search.Services;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllers();

builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 100;
});

builder.Services.AddHttpClient<GitHubService>();
builder.Services.AddScoped<MarkdownService>();
builder.Services.AddScoped<TokenService>();

builder.Services.AddDbContext<SearchDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        o => o.UseVector()));

builder.Services.AddScoped<IEmbeddingService, OpenAIEmbeddingService>();
builder.Services.AddScoped<MarkdownChunker>();
builder.Services.AddScoped<IndexingService>();

builder.Services.AddDataProtection();

builder.Services.AddSession();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

app.Run();
