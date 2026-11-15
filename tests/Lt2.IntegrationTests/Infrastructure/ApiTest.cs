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

    /// <summary>The run's shared moderator on <see cref="Factory"/> (<see cref="StaffMembers.SharedAsync"/>).</summary>
    protected Task<StaffMember> ModeratorAsync() => StaffMembers.SharedAsync(Factory, Database, "moderator");

    /// <summary>The run's shared administrator on <see cref="Factory"/>.</summary>
    protected Task<StaffMember> AdministratorAsync() => StaffMembers.SharedAsync(Factory, Database, "administrator");

    /// <summary>The run's shared moderator and administrator on <see cref="Factory"/>.</summary>
    protected async Task<(StaffMember Moderator, StaffMember Administrator)> StaffAsync() => (await ModeratorAsync(), await AdministratorAsync());}
