using FakeItEasy;
using lucia.TimerAgent.ScheduledTasks;
using MongoDB.Driver;

namespace lucia.Tests.ScheduledTasks;

public sealed class MongoAlarmClockRepositoryTests
{
    private readonly IMongoClient _mongoClient = A.Fake<IMongoClient>();
    private readonly IMongoDatabase _database = A.Fake<IMongoDatabase>();
    private readonly IMongoCollection<AlarmClock> _alarmsCollection = A.Fake<IMongoCollection<AlarmClock>>();
    private readonly IMongoCollection<AlarmSound> _soundsCollection = A.Fake<IMongoCollection<AlarmSound>>();
    private readonly IMongoIndexManager<AlarmClock> _alarmIndexManager = A.Fake<IMongoIndexManager<AlarmClock>>();
    private readonly IMongoIndexManager<AlarmSound> _soundIndexManager = A.Fake<IMongoIndexManager<AlarmSound>>();

    private readonly MongoAlarmClockRepository _repository;

    public MongoAlarmClockRepositoryTests()
    {
        A.CallTo(() => _mongoClient.GetDatabase("luciatasks", null)).Returns(_database);
        A.CallTo(() => _database.GetCollection<AlarmClock>("alarm_clocks", null)).Returns(_alarmsCollection);
        A.CallTo(() => _database.GetCollection<AlarmSound>("alarm_sounds", null)).Returns(_soundsCollection);
        A.CallTo(() => _alarmsCollection.Indexes).Returns(_alarmIndexManager);
        A.CallTo(() => _soundsCollection.Indexes).Returns(_soundIndexManager);

        _repository = new MongoAlarmClockRepository(_mongoClient);
    }

    [Fact]
    public void Constructor_CreatesIndexes()
    {
        A.CallTo(() => _alarmIndexManager.CreateMany(
            A<IEnumerable<CreateIndexModel<AlarmClock>>>._,
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();

        A.CallTo(() => _soundIndexManager.CreateMany(
            A<IEnumerable<CreateIndexModel<AlarmSound>>>._,
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task GetAllAlarmsAsync_ReturnsAlarms()
    {
        var expected = new List<AlarmClock>
        {
            new() { Id = "a1", Name = "Morning", TargetEntity = "media_player.bedroom" },
        };

        var cursor = A.Fake<IAsyncCursor<AlarmClock>>();
        A.CallTo(() => cursor.MoveNextAsync(A<CancellationToken>._))
            .ReturnsNextFromSequence(true, false);
        A.CallTo(() => cursor.Current).Returns(expected);

        A.CallTo(() => _alarmsCollection.FindAsync(
            A<FilterDefinition<AlarmClock>>._,
            A<FindOptions<AlarmClock, AlarmClock>>._,
            A<CancellationToken>._)).Returns(cursor);

        var result = await _repository.GetAllAlarmsAsync();

        Assert.Single(result);
        Assert.Equal("Morning", result[0].Name);
    }

    [Fact]
    public async Task UpsertAlarmAsync_CallsReplaceOneWithUpsert()
    {
        var alarm = new AlarmClock
        {
            Id = "a1",
            Name = "Morning",
            TargetEntity = "media_player.bedroom"
        };

        await _repository.UpsertAlarmAsync(alarm);

        A.CallTo(() => _alarmsCollection.ReplaceOneAsync(
            A<FilterDefinition<AlarmClock>>._,
            alarm,
            A<ReplaceOptions>.That.Matches(o => o.IsUpsert),
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task DeleteAlarmAsync_CallsDeleteOne()
    {
        await _repository.DeleteAlarmAsync("a1");

        A.CallTo(() => _alarmsCollection.DeleteOneAsync(
            A<FilterDefinition<AlarmClock>>._,
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task UpsertSoundAsync_CallsReplaceOneWithUpsert()
    {
        var sound = new AlarmSound
        {
            Id = "s1",
            Name = "Gentle",
            MediaSourceUri = "media-source://media_source/local/alarms/gentle.wav"
        };

        await _repository.UpsertSoundAsync(sound);

        A.CallTo(() => _soundsCollection.ReplaceOneAsync(
            A<FilterDefinition<AlarmSound>>._,
            sound,
            A<ReplaceOptions>.That.Matches(o => o.IsUpsert),
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task DeleteSoundAsync_CallsDeleteOne()
    {
        await _repository.DeleteSoundAsync("s1");

        A.CallTo(() => _soundsCollection.DeleteOneAsync(
            A<FilterDefinition<AlarmSound>>._,
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }
}
