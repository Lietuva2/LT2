using System.Text.RegularExpressions;

namespace Lt2.ArchitectureTests;

/// <summary>
/// Design 6.4: the proof of work's script is the site's own and keeps nothing: no cookie, no storage key, nothing loaded from
/// another host, no dynamic code, no worker the policy would have to name; and its key is a secret of the web container alone.
/// </summary>
public sealed class BotCheckScriptTests
{
    private static readonly DirectoryInfo Root = RepositoryRoot();

    private static string Script() =>
        Regex.Replace(Regex.Replace(File.ReadAllText(Path.Combine(Root.FullName, "src", "Lt2.Web.Client", "wwwroot", "js", "lt2-botcheck.js")), @"/\*.*?\*/", "", RegexOptions.Singleline), @"//[^\n]*", "");

    [Fact]
    public void The_script_keeps_nothing_in_the_browser()
    {
        Regex.IsMatch(Script(), @"\b(localStorage|sessionStorage|document\.cookie|indexedDB|caches\.open|navigator\.serviceWorker)\b").ShouldBeFalse();
    }

    [Fact]
    public void The_script_loads_nothing_from_another_host_and_runs_no_dynamic_code()
    {
        var script = Script();
        script.ShouldNotContain("://");
        Regex.IsMatch(script, @"\b(eval|importScripts|XMLHttpRequest|WebSocket|sendBeacon|new Worker|new Function)\b").ShouldBeFalse();
        Regex.IsMatch(script, @"\bimport\s*\(").ShouldBeFalse();
    }

    [Fact]
    public void The_script_asks_only_the_url_it_is_given_and_hashes_with_the_browsers_own_crypto()
    {
        var script = Script();
        Regex.Matches(script, @"\bfetch\(([^,)]*)").Select(m => m.Groups[1].Value.Trim()).ShouldBe(["url"]);
        script.ShouldContain("crypto.subtle.digest(\"SHA-256\"");
    }

    [Fact]
    public void The_key_is_a_secret_of_the_web_container_made_by_the_secrets_script_and_never_a_value_in_the_repository()
    {
        var deploy = Path.Combine(Root.FullName, "deploy");
        File.ReadAllText(Path.Combine(deploy, "init-secrets.py")).ShouldContain("\"bot_check_key\"");
        var compose = File.ReadAllText(Path.Combine(deploy, "compose.yaml"));
        Regex.Count(compose, @"source: bot_check_key\s+target: Security__BotCheckKey").ShouldBe(1);
        Regex.Count(compose, @"Security__BotCheckKey").ShouldBe(1, "web only: the worker neither issues nor checks a proof of work");
        compose.ShouldContain("file: ${LT2_SECRETS_DIR:-./secrets}/bot_check_key");
        File.ReadAllText(Path.Combine(deploy, ".env")).ShouldNotContain("BotCheckKey");
    }

    private static DirectoryInfo RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LT2.sln")))
            {
                return dir;
            }
        }

        throw new InvalidOperationException("LT2.sln not found above " + AppContext.BaseDirectory);
    }
}
