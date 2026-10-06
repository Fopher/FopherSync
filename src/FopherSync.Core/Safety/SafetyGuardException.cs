namespace FopherSync.Core.Safety;

public class SafetyGuardException : Exception
{
    public string RuleName { get; }
    public string OffendingPath { get; }

    public SafetyGuardException(string ruleName, string message, string offendingPath = "")
        : base(message)
    {
        RuleName = ruleName;
        OffendingPath = offendingPath;
    }
}
