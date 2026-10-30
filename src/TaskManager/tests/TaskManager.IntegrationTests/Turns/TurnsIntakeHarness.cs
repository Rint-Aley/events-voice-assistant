using Microsoft.EntityFrameworkCore;
using TaskManager.Infrastructure.Persistence;
using TaskManager.Infrastructure.Persistence.Queries;
using TaskManager.Infrastructure.Persistence.Repositories;
using TaskManager.Turns.Application.Scenarios;

namespace TaskManager.IntegrationTests.Turns;

/// <summary>
/// Собирает сценарии Turns на реальном Postgres и fake-nlu/планировщике.
/// </summary>
internal sealed class TurnsIntakeHarness : IAsyncDisposable
{
    private readonly TaskManagerDbContext _dbContext;
    private readonly UnitOfWork _unitOfWork;

    private TurnsIntakeHarness(
        TaskManagerDbContext dbContext,
        UnitOfWork unitOfWork,
        FakeNluClient nlu,
        FakeJobScheduler scheduler,
        TestClock clock)
    {
        _dbContext = dbContext;
        _unitOfWork = unitOfWork;
        Nlu = nlu;
        Scheduler = scheduler;
        Clock = clock;

        var turns = new TurnRepository(dbContext);
        var drafts = new DraftRepository(dbContext);
        var answers = new AnswerRepository(dbContext);

        SubmitText = new SubmitTextHandler(turns, scheduler, unitOfWork, clock);
        SubmitVoice = new SubmitVoiceHandler(turns, scheduler, unitOfWork, clock);
        HandleSttResult = new HandleSttResultHandler(turns, scheduler, unitOfWork, clock);
        Interpret = new InterpretTurnHandler(turns, drafts, answers, nlu, unitOfWork, clock);
        CancelDraft = new CancelDraftHandler(turns, drafts, unitOfWork);
        Snapshot = new GetTurnSnapshotHandler(new TurnQuery(dbContext));
    }

    public TaskManagerDbContext DbContext => _dbContext;

    public FakeNluClient Nlu { get; }

    public FakeJobScheduler Scheduler { get; }

    public TestClock Clock { get; }

    public SubmitTextHandler SubmitText { get; }

    public SubmitVoiceHandler SubmitVoice { get; }

    public HandleSttResultHandler HandleSttResult { get; }

    public InterpretTurnHandler Interpret { get; }

    public CancelDraftHandler CancelDraft { get; }

    public GetTurnSnapshotHandler Snapshot { get; }

    public static async Task<TurnsIntakeHarness> CreateAsync(
        string connectionString,
        DateTimeOffset now,
        TimeZoneInfo? timeZone = null)
    {
        var options = new DbContextOptionsBuilder<TaskManagerDbContext>()
            .UseTaskManagerPostgres(connectionString)
            .Options;

        var dbContext = new TaskManagerDbContext(options);
        await dbContext.Database.MigrateAsync();
        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE answer_events, answers, domain_commands, drafts, events, turns CASCADE");

        var unitOfWork = new UnitOfWork(dbContext);

        return new TurnsIntakeHarness(
            dbContext,
            unitOfWork,
            new FakeNluClient(),
            new FakeJobScheduler(),
            new TestClock(now, timeZone));
    }

    public async ValueTask DisposeAsync()
    {
        await _unitOfWork.DisposeAsync();
        await _dbContext.DisposeAsync();
    }
}
