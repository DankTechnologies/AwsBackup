# DanK AWS Backup

Periodic, gpg-encrypted backups to AWS S3 Glacier Deep Archive

## Overview

**AWS Backup** is a Dockerized .NET application that runs on a cron schedule, chaining together `find`, `tar`, and `gpg` to create encrypted archives of files modified since the previous run.  The encrypted archives get shipped off to a AWS S3 bucket using the *S3 Glacier Deep Archive* storage class.

I use it to backup my family pictures 4 times a year for a reasonable price (currently $0.00099 per GB-month), without having to trust AWS in terms of data privacy.

## Tech Stack

* [Coravel](https://docs.coravel.net/) is used to handle recurring backup jobs by cron expression
* [CliWrap](https://github.com/Tyrrrz/CliWrap) is used to shell out to `find`, `tar`, and `gpg`

## Configuration

[appsettings.json](./appsettings.json) contains sample values that should be [overridden](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration) by `appsettings.Production.json` or runtime ENV VARs

* `PicturesPath` - working directory for the `find` command, also becomes the  `tar` archive root directory
  * **NOTE** - if you're backing up multiple directories, this path should be the nearest ancestor directory that contains all of the target directories
* `TempPath` - needs enough disk space to store the plaintext and encrypted versions of `tar` archive, transiently
* `CronExpression` - to set how often the backups run
* `FindCommand` - test this `find` command from `PicturesPath`.  The sample value shows how to include and exclude files and directories.  The command output should list every file you want backed up
* `GpgPassprhase` - used to derive a symmetric encrypt/decrypt key.  Back this up in Bitwarden or something

## Docker Notes

1. Set `PicturePath` and `TempPath` to container paths, and use volumes to mount the target directories.  For instance, I use `/data/pictures` and `/data/tmp` as the container paths, with `/mnt/user/pictures` and `/mnt/user/downloads` as the host paths

```yaml
    # example Ansible deploy task
    - name: Run Docker container
      docker_container:
        name: awsbackup
        image: awsbackup
        state: started
        restart_policy: unless-stopped
        env:
          PicturesPath: "/data/pictures"
          TempPath: "/data/tmp"
        volumes:
          - "/mnt/user/pictures:/data/pictures"
          - "/mnt/user/downloads:/data/tmp"
```

## Logs

Written to STDOUT, i.e. [the right way](https://12factor.net/logs)