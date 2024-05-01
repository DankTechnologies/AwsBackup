using Hangfire.Dashboard;

namespace AwsBackup.Filters;

public class HangfireAllowFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        return true;
    }
}
