using Amazon.S3;
using AwsBackup.BackupService;
using AwsBackup.Filters;
using Hangfire;
using Hangfire.Redis.StackExchange;

var builder = WebApplication.CreateBuilder(args);

var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
var cronExpression = builder.Configuration.GetValue<string>("CronExpression");
var environmentName = builder.Environment.EnvironmentName;

builder.Services.AddHangfire(config =>
{
    config.UseRedisStorage(redisConnectionString, new RedisStorageOptions
    {
        Prefix = "AwsBackup",
        InvisibilityTimeout = TimeSpan.FromHours(12)
    });
});

builder.Services.AddHangfireServer();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddSingleton<IAmazonS3, AmazonS3Client>(provider => new AmazonS3Client(Amazon.RegionEndpoint.USEast2));

var app = builder.Build();

app.UseHangfireDashboard("/hangfire", new DashboardOptions {
Authorization = new [] { new HangfireAllowFilter() }
});

var recurringJobManager = app.Services.GetRequiredService<IRecurringJobManager>();
recurringJobManager.AddOrUpdate<BackupService>("ArchiveAndUpload", x => x.PerformBackup(), cronExpression);

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Starting AWS Backup in {EnvironmentName} environment", environmentName);

var daysSinceLastRun = BackupService.GetDaysSinceLastRun(cronExpression);

logger.LogInformation("Last run was {daysSinceLastRun} days ago", daysSinceLastRun);

app.UseRouting();

app.Run();
