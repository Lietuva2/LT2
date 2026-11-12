namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>Base for tests over the HTTP API. One application instance is shared by all tests.</summary>
public abstract class ApiTest(DatabaseFixture database)
{
    private static readonly Lock Gate = new();
    private static Lt2Factory? _shared;

    protected DatabaseFixture Database { get; } = database;

    protected Lt2Factory Factory
    {
        get
        {
            lock (Gate)
            {
                return _shared ??= new Lt2Factory(Database);
            }
        }
    }

    protected TestBrowser NewBrowser() => Factory.NewBrowser();
}
