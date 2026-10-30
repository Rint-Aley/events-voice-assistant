using NSubstitute;
using TaskManager.Application.Abstractions;
using TaskManager.Turns.Application.Clients.Stt;
using TaskManager.Turns.Application.Configuration;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Application.Scenarios;
using TaskManager.Turns.Domain;

namespace TaskManager.IntegrationTests.Turns;

public sealed class TranscribeTurnHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Transcribe_sends_content_type_of_uploaded_object()
    {
        // Arrange
        Turn turn = Turn.Create(
            Guid.NewGuid(),
            TurnState.Transcribing,
            Guid.NewGuid(),
            Now.AddMinutes(5),
            Now,
            audioObjectKey: "audio/voice.ogg").Value;

        var turns = Substitute.For<ITurnRepository>();
        turns.GetByIdAsync(turn.Id, Arg.Any<CancellationToken>()).Returns(turn);

        var storage = Substitute.For<IObjectStorage>();
        var audioUrl = new Uri("http://s3.local/audio/voice.ogg");
        storage.CreatePresignedDownloadUrlAsync("audio/voice.ogg", Arg.Any<CancellationToken>())
            .Returns(audioUrl);
        storage.GetContentType("audio/voice.ogg").Returns("audio/ogg");

        var stt = new CapturingSttClient(
            new SttResult(turn.Id, "создай задачу", "ru", 0.9m, false, 1200, "fake"));
        var resultHandler = new HandleSttResultHandler(
            turns,
            Substitute.For<IJobScheduler>(),
            Substitute.For<IUnitOfWork>(),
            new TestClock(Now));

        var handler = new TranscribeTurnHandler(
            stt,
            storage,
            turns,
            resultHandler,
            new SttTurnOptions("ru"));

        // Act
        await handler.HandleAsync(turn.Id);

        // Assert
        // Контракт stt требует тот же content_type, с которым клиент загрузил файл;
        // для .ogg конфигурационное значение audio/webm было бы ошибкой.
        stt.Request.Should().NotBeNull();
        stt.Request!.ContentType.Should().Be("audio/ogg");
        stt.Request.TurnId.Should().Be(turn.Id);
        stt.Request.AudioUrl.Should().Be(audioUrl);
    }

    private sealed class CapturingSttClient : ISttClient
    {
        private readonly SttResult _result;

        public CapturingSttClient(SttResult result)
        {
            _result = result;
        }

        public SttRequest? Request { get; private set; }

        public Task<SttResult> TranscribeAsync(
            SttRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(_result);
        }
    }
}
