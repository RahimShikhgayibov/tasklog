namespace Tasklog.Api.Common;

/// <summary>Collects field errors and turns them into a 400 ValidationProblem.</summary>
public sealed class Validation
{
    private readonly Dictionary<string, string[]> _errors = new();

    public bool IsValid => _errors.Count == 0;

    public Validation Require(bool condition, string field, string message)
    {
        if (!condition) _errors[field] = [message];
        return this;
    }

    public IResult ToResult() => Results.ValidationProblem(_errors);

    public static Validation Title(string? title, string field = "title") =>
        new Validation()
            .Require(!string.IsNullOrWhiteSpace(title), field, "Ad boş ola bilməz.")
            .Require((title?.Trim().Length ?? 0) <= 300, field, "Ad 300 simvoldan uzun ola bilməz.");
}
