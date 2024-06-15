using Amazon.S3;
using Amazon.S3.Transfer;
using CliWrap;
using CliWrap.Buffered;
using Coravel.Invocable;
using Cronos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AwsBackup.BackupService;

public class BackupService : IInvocable
{
    private readonly IAmazonS3 _s3Client;
    private readonly ILogger<BackupService> _logger;
    private readonly string _picturesPath;
    private readonly string _tempPath;
    private readonly string _s3BucketName;
    private readonly string _gpgPassphrase;
    private readonly string _cronExpression;
    private readonly string _findCommand;
    public BackupService(IAmazonS3 s3Client, ILogger<BackupService> logger, IConfiguration configuration)
    {
        _s3Client = s3Client;
        _logger = logger;
        _picturesPath = configuration.GetValue<string>("PicturesPath");
        _tempPath = configuration.GetValue<string>("TempPath");
        _gpgPassphrase = configuration.GetValue<string>("GpgPassphrase");
        _s3BucketName = configuration.GetValue<string>("S3BucketName");
        _cronExpression = configuration.GetValue<string>("CronExpression");
        _findCommand = configuration.GetValue<string>("FindCommand");
    }

    public async Task Invoke()
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        var archivePath = Path.Combine(_tempPath, $"{date}-pics.tar");
        var encryptedArchivePath = $"{archivePath}.gpg";
        var daysSinceLastRun = GetDaysSinceLastRun(_cronExpression);

        _logger.LogInformation("Starting backup on {date}.  Last run was {daysSinceLastRun} days ago", date, daysSinceLastRun);

        var daysModified = $"-mtime -{daysSinceLastRun}";
        var fullCommand = $"{_findCommand} {daysModified} -print0 | tar --null -cvf {archivePath} --files-from -";

        _logger.LogInformation("Running find + tar command - {command}", fullCommand);

        // Tar and gzip
        var tarResult = await Cli.Wrap("/bin/sh")
            .WithWorkingDirectory(_picturesPath)
            .WithArguments(new[] { "-c", fullCommand })
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (tarResult.ExitCode != 0)
        {
            _logger.LogError($"Tar operation failed: {tarResult.StandardOutput}{tarResult.StandardError}");
            return;
        }

        _logger.LogInformation("Running gpg command - {command}", fullCommand);

        var gpgResult = await Cli.Wrap("gpg")
            .WithArguments($"--batch --passphrase-fd 0 -c {archivePath}")
            .WithStandardInputPipe(PipeSource.FromString(_gpgPassphrase))
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (gpgResult.ExitCode != 0 || !File.Exists(encryptedArchivePath))
        {
            _logger.LogError($"Gpg operation failed: {gpgResult.StandardOutput}{gpgResult.StandardError}");
            return;
        }

        var tarHash = await ComputeSha256Hash(archivePath);
        var gpgHash = await ComputeSha256Hash(encryptedArchivePath);

        _logger.LogInformation("TAR file is {size} bytes, with SHA-256 hash: {tarHash}", new FileInfo(archivePath).Length, tarHash);
        _logger.LogInformation("GPG file is {size} bytes, with SHA-256 hash: {tarHash}", new FileInfo(encryptedArchivePath).Length, gpgHash);

        _logger.LogInformation("Start uploading to S3");

        var transferUtility = new TransferUtility(_s3Client);
        var uploadRequest = new TransferUtilityUploadRequest
        {
            FilePath = encryptedArchivePath,
            BucketName = _s3BucketName,
            Key = Path.GetFileName(encryptedArchivePath),
            StorageClass = S3StorageClass.DeepArchive,
        };

        uploadRequest.Metadata.Add("x-amz-meta-sha256", gpgHash);

        int lastLoggedPercent = 0;

        uploadRequest.UploadProgressEvent += (sender, args) =>
        {
            int currentPercent = args.PercentDone;

            if (currentPercent >= lastLoggedPercent + 10)
            {
                _logger.LogInformation($"Upload Progress: {currentPercent}%");
                lastLoggedPercent = currentPercent;
            }
        };

        await transferUtility.UploadAsync(uploadRequest);

        _logger.LogInformation("Finished uploading to S3");

        // Delete files
        File.Delete(archivePath);
        File.Delete(encryptedArchivePath);

        _logger.LogInformation("Finished backup on {date}", date);
    }

    private static async Task<string> ComputeSha256Hash(string filePath)
    {
        var result = await Cli.Wrap("sha256sum")
            .WithArguments(new[] { filePath })
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (result.ExitCode != 0)
            throw new Exception($"Failed to compute SHA-256 hash for {filePath}, error: {result.StandardOutput + result.StandardError}");

        return result.StandardOutput.Split(' ')[0];
    }

    public static int GetDaysSinceLastRun(string cronExpression)
    {
        var cron = CronExpression.Parse(cronExpression);

        // going back 10 minutes allows while loop below to come up with the right value
        // since it gets last occurrence by going back a year and keeps finding the next occurrence
        var now = DateTime.UtcNow.AddMinutes(-10);

        var checkDate = now.AddYears(-1);
        var lastChecked = checkDate;

        while(true)
        {
            var next = cron.GetNextOccurrence(lastChecked).Value;

            if (next > now)
                break;

            lastChecked = next;
        }

        return now.Subtract(lastChecked).Days;
    }

    public static int GetDaysUntilNextRun(string cronExpression)
    {
        var now = DateTime.UtcNow;
        var cron = CronExpression.Parse(cronExpression);
        var next = cron.GetNextOccurrence(now);

        return next.Value.Subtract(now).Days;
    }
}