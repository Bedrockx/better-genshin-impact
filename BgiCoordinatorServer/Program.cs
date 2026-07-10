using BgiCoordinatorServer.Hubs;
using BgiCoordinatorServer.Services;

var builder = WebApplication.CreateBuilder(args);

// 读取环境变量配置
var maxRooms = int.TryParse(Environment.GetEnvironmentVariable("MAX_ROOMS"), out var mr) ? mr : 50;
var playerTimeoutSeconds = int.TryParse(
    Environment.GetEnvironmentVariable("PLAYER_TIMEOUT_SECONDS"), out var pts) ? pts : 120;

// 注册服务
builder.Services.AddSingleton(_ => new RoomManager(maxRooms));
builder.Services.AddHostedService<HeartbeatMonitor>();
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB，支持大量路线文件上报
});

// 配置 CORS（开发阶段允许所有来源）
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();

// 映射 SignalR Hub
app.MapHub<CoordinatorHub>("/hub");

app.MapGet("/", () => Results.Ok(new { status = "BgiCoordinatorServer running", maxRooms, playerTimeoutSeconds }));

app.Run();
