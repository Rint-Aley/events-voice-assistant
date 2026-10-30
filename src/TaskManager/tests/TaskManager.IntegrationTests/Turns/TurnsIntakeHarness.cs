using Microsoft.EntityFrameworkCore;
using TaskManager.Events.Application.Scenarios;
using TaskManager.Infrastructure.Persistence;
using TaskManager.Infrastructure.Persistence.Queries;
using TaskManager.Infrastructure.Persistence.Repositories;
using TaskManager.Turns.Application.Scenarios;

namespace TaskManager.IntegrationTests.Turns;

/// <summary>
/// Собирает сценарии Turns на реальном Postgres и fake-nlu/tts/планировщике.
/// </summary>
internal sealed class TurnsIntakeHarness : IAsyncDisposable
{
    private readonly TaskManagerDbContext _dbContext;
    private readonly UnitOfWork _unitOfWork;

    private TurnsIntakeHarness(
        TaskManagerDbContext dbContext,
        UnitOfWork unitOfWork,
        FakeNluClient nlu,
        FakeTtsClient tts,
        FakeJobScheduler scheduler,
        TestClock clock)
    {
        _dbContext = dbContext;
        _unitOfWork = unitOfWork;
        Nlu = nlu;
        Tts = tts;
        Scheduler = scheduler;
        Clock = clock;

        var turns = new TurnRepository(dbContext);
        var drafts = new DraftRepository(dbContext);
        var answers = new AnswerRepository(dbContext);
        var commands = new DomainCommandRepository(dbContext);
        var events = new EventRepository(dbContext);
        var eventQuery = new EventQuery(dbContext);
        var conflicts = new DetectConflictsHandler(eventQuery);
        var createEvent = new CreateEventHandler(events, conflicts, unitOfWork);
        var listPeriod = new ListPeriodHandler(eventQuery);

        SubmitText = new SubmitTextHandler(turns, scheduler, unitOfWork, clock);
        SubmitVoice = new SubmitVoiceHandler(turns, scheduler, unitOfWork, clock);
        HandleSttResult = new HandleSttResultHandler(turns, scheduler, unitOfWork, clock);
        Interpret = new InterpretTurnHandler(
            turns,
            drafts,
            answers,
            commands,
            nlu,
            listPeriod,
            scheduler,
            unitOfWork,
            clock);
        CancelDraft = new CancelDraftHandler(turns, drafts, unitOfWork);
        Snapshot = new GetTurnSnapshotHandler(new TurnQuery(dbContext));
        Apply = new ApplyTurnPlanHandler(
            turns,
            commands,
            answers,
            createEvent,
            scheduler,
            unitOfWork,
            clock);
        Synthesize = new SynthesizeAnswerHandler(answers, tts, unitOfWork);
    }

    public TaskManagerDbContext DbContext => _dbContext;

    public FakeNluClient Nlu { get; }

    public FakeTtsClient Tts { get; }

    public FakeJobScheduler Scheduler { get; }

    public TestClock Clock { get; }

    public SubmitTextHandler SubmitText { get; }

    public SubmitVoiceHandler SubmitVoice { get; }

    public HandleSttResultHandler HandleSttResult { get; }

    public InterpretTurnHandler Interpret { get; }

    public CancelDraftHandler CancelDraft { get; }

    public GetTurnSnapshotHandler Snapshot { get; }

    public ApplyTurnPlanHandler Apply { get; }

    public SynthesizeAnswerHandler Synthesize { get; }

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
            new FakeTtsClient(),
            new FakeJobScheduler(),
            new TestClock(now, timeZone));
    }

    public async ValueTask DisposeAsync()
    {
        await _unitOfWork.DisposeAsync();
        await _dbContext.DisposeAsync();
    }
}
