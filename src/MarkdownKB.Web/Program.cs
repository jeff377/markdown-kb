using MarkdownKB.AI.Services;
using MarkdownKB.Channels.Line;
using MarkdownKB.Core.Services;
using MarkdownKB.Search;
using MarkdownKB.Search.Services;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title       = "MarkdownKB API",
        Version     = "v1",
        Description = "Markdown 知識庫搜尋與問答 API"
    });
});

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
builder.Services.AddScoped<HybridSearchService>();

// Phase 3 — RAG / Chat
builder.Services.AddSingleton<ConversationService>();
builder.Services.AddScoped<QueryRewriter>();
builder.Services.AddScoped<RagService>();

// Phase 4 — LINE Bot
builder.Services.AddHttpClient<LineReplyClient>();

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

// HttpsRedirection disabled: container runs HTTP only; TLS is terminated at the reverse proxy (ngrok/load balancer)
// app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthorization();

app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "MarkdownKB API v1"));

app.MapRazorPages();
app.MapControllers();

app.Run();
