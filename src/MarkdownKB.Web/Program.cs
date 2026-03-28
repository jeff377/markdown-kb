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
    // GPT Actions 需要每個 operation 都有 operationId
    c.CustomOperationIds(e =>
        $"{e.ActionDescriptor.RouteValues["controller"]}_{e.HttpMethod}");
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

// GPT Actions 需要 OpenAPI 3.1.0；Swashbuckle 預設輸出 3.0.x，攔截回應做版本替換
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/swagger/v1/swagger.json"))
    {
        var original = ctx.Response.Body;
        using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;

        await next();

        buffer.Position = 0;
        var json = await new StreamReader(buffer).ReadToEndAsync();
        // 將 3.0.x 替換為 GPT Actions 要求的 3.1.0
        json = System.Text.RegularExpressions.Regex.Replace(
            json, @"""openapi""\s*:\s*""3\.0\.\d+""", @"""openapi"":""3.1.0""");

        ctx.Response.Body = original;
        ctx.Response.ContentLength = System.Text.Encoding.UTF8.GetByteCount(json);
        await ctx.Response.WriteAsync(json);
        return;
    }
    await next();
});

app.UseSwagger(c =>
{
    // 動態注入 servers，讓 GPT Actions 透過 ngrok 存取時能拿到正確的 base URL
    c.PreSerializeFilters.Add((swagger, httpReq) =>
    {
        var scheme = httpReq.Headers.ContainsKey("X-Forwarded-Proto")
            ? httpReq.Headers["X-Forwarded-Proto"].ToString()
            : httpReq.Scheme;
        swagger.Servers = [new() { Url = $"{scheme}://{httpReq.Host}" }];
    });
});
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "MarkdownKB API v1"));

app.MapRazorPages();
app.MapControllers();

app.Run();
