using MovieAgent.Agent.Abstractions;
using MovieAgent.Agent.Tools;
using MovieAgent.Hosting;

namespace MovieAgent.EntryPoints;

/// <summary>
/// Exercises the <c>sql-shortcut</c> read-only guard against a fixed list of queries, including
/// the ones it must refuse.
/// </summary>
/// <remarks>
/// Written as a command rather than a unit test because this repo has no test project, and a
/// barrier protecting a superuser connection from model-authored SQL should be demonstrable on
/// demand rather than asserted in a comment. The last case deliberately bypasses the text guard
/// to prove the database-level <c>READ ONLY</c> transaction is doing independent work.
/// </remarks>
public sealed class SqlGuardCheckEntryPoint : IAppEntryPoint
{
    private readonly ISqlQueryExecutor _sql;

    public SqlGuardCheckEntryPoint(ISqlQueryExecutor sql)
    {
        _sql = sql;
    }

    private static readonly (string Label, string Sql, bool ShouldAllow)[] _cases =
    [
        ("plain select", "select title from film where film_id = 1", true),
        ("join select", "select f.title, l.name from film f join language l on l.language_id = f.language_id where f.film_id = 1", true),
        ("aggregate", "select count(*) as n from film", true),
        ("trailing semicolon", "select count(*) from film;", true),
        ("cte", "with x as (select film_id from film limit 1) select * from x", true),
        ("literal containing 'update'", "select title from film where title = 'THE UPDATE STORY'", true),
        ("stacked statement", "select 1; drop table film", false),
        ("update", "update film set title = 'x' where film_id = 1", false),
        ("delete", "delete from film where film_id = 1", false),
        ("drop", "drop table film", false),
        ("select into", "select film_id into tmp from film", false),
        ("explain analyze", "explain analyze select 1", false),
        ("banned view", "select * from film_list limit 1", false),
        ("information_schema", "select table_name from information_schema.tables", false),
        ("pg_catalog", "select relname from pg_catalog.pg_class", false),
        ("comment-hidden write", "select 1 /* */ ; update film set title='x'", false),
    ];

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var failures = 0;

        Console.WriteLine("=== text guard ===");
        foreach (var (label, sql, shouldAllow) in _cases)
        {
            var verdict = SqlShortcutGuard.Inspect(sql);
            var ok = verdict.Allowed == shouldAllow;
            failures += ok ? 0 : 1;
            var state = verdict.Allowed ? "ALLOW" : "BLOCK";
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {state,-5} {label,-30} {(verdict.Allowed ? string.Empty : verdict.Reason)}");
        }

        Console.WriteLine();
        Console.WriteLine("=== execution: an allowed query really runs ===");
        try
        {
            var result = await _sql.QueryReadOnlyAsync(
                "select f.title, l.name as language from film f join language l on l.language_id = f.language_id where f.film_id = 1",
                cancellationToken);
            Console.WriteLine("  ok   " + ToolOutputFormat.Rows(result, 5).ReplaceLineEndings(" / "));
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine("  FAIL " + ex.Message);
        }

        Console.WriteLine();
        Console.WriteLine("=== database barrier: a write that bypasses the text guard must still fail ===");
        try
        {
            await _sql.QueryReadOnlyAsync("update film set title = title where film_id = 1", cancellationToken);
            failures++;
            Console.WriteLine("  FAIL the read-only transaction allowed a write");
        }
        catch (Exception ex)
        {
            Console.WriteLine("  ok   database refused it: " + ex.Message.ReplaceLineEndings(" ").Trim());
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ALL GUARD CHECKS PASSED" : $"{failures} GUARD CHECK(S) FAILED");
        return failures == 0 ? 0 : 1;
    }
}
