using Amazon.S3;
using AwsBackup.BackupService;
using Coravel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

var cronExpression = builder.Configuration.GetValue<string>("CronExpression");
var environmentName = builder.Environment.EnvironmentName;

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(x => {
    x.SingleLine = true;
    x.IncludeScopes = false;
    x.TimestampFormat = "[MM-dd HH:mm:ss] ";
});

builder.Services.AddScheduler();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddSingleton<IAmazonS3, AmazonS3Client>(provider => new AmazonS3Client(Amazon.RegionEndpoint.USEast2));

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Starting AWS Backup in {EnvironmentName} environment", environmentName);

var daysSinceLastRun = BackupService.GetDaysSinceLastRun(cronExpression);
var daysUntilNextRun = BackupService.GetDaysUntilNextRun(cronExpression);

logger.LogInformation("Last run was {daysSinceLastRun} days ago", daysSinceLastRun);
logger.LogInformation("Next run in {daysUntilNextRun} days", daysUntilNextRun);

// Coravel

app.Services.UseScheduler(scheduler => {
    scheduler
        .Schedule<BackupService>()
        .Cron(cronExpression);
}).OnError(x => logger.LogError(x, x.Message));

app.Run();
