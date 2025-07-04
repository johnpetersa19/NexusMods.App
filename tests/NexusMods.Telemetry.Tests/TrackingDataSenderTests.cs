using System.Diagnostics;
using System.Net;
using System.Text;
using FluentAssertions;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NexusMods.Abstractions.NexusWebApi;
using NexusMods.Abstractions.NexusWebApi.Types;
using NSubstitute;
using Xunit;

namespace NexusMods.Telemetry.Tests;

public class TrackingDataSenderTests
{
    [Fact]
    public async Task Test()
    {
        var loginManager = Substitute.For<ILoginManager>();
        loginManager.UserInfo.Returns(new UserInfo
        {
            UserId = UserId.From(1337),
            Name = "",
            AvatarUrl = null,
            UserRole = UserRole.Free,
        });

        var expectedUserAgent = Encoding.UTF8.GetString(TrackingDataSender.CreateUserAgent());

        var tsc = new TaskCompletionSource();

        var messageHandler = Substitute.ForPartsOf<MockHttpMessageHandler>();
        messageHandler
            .SendMock(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("okay", Encoding.UTF8),
            }))
            .AndDoes(callInfo =>
            {
                var requestMessage = callInfo.ArgAt<HttpRequestMessage>(position: 0);
                var content = requestMessage.Content;
                content.Should().NotBeNull();

                using var stream = content!.ReadAsStream();
                using var textReader = new StreamReader(stream, Encoding.UTF8);
                var res = textReader.ReadToEnd();
                try
                {
                    ExpectJson($$"""{ "requests": ["?idsite=7&rec=1&apiv=1&ua={{expectedUserAgent}}&send_image=0&ca=1&uid=1337&e_c=Game&e_a=Add+Game&e_n=Mount+%26+Blade&h=0&m=0&s=0","?idsite=7&rec=1&apiv=1&ua={{expectedUserAgent}}&send_image=0&ca=1&uid=1337&e_c=Loadout&e_a=Create+Loadout&e_n=Mount+%26+Blade&h=0&m=0&s=1","?idsite=7&rec=1&apiv=1&ua={{expectedUserAgent}}&send_image=0&ca=1&uid=1337&cra=Foo&cra_tp=System.NotSupportedException&cra_ct=v0.0.1","?idsite=7&rec=1&apiv=1&ua={{expectedUserAgent}}&send_image=0&ca=1&uid=1337&cra=bar&cra_tp=System.Diagnostics.UnreachableException&cra_ct=v0.0.1","?idsite=7&rec=1&apiv=1&ua={{expectedUserAgent}}&send_image=0&ca=1&uid=1337&e_c=Loadout&e_a=Create+Loadout&e_n=Foo+bar+baz&e_v=100&h=0&m=0&s=3","?idsite=7&rec=1&apiv=1&ua={{expectedUserAgent}}&send_image=0&ca=1&uid=1337&e_c=Loadout&e_a=Create+Loadout&e_n=Foo+bar+baz&e_v=1131412.132&h=0&m=0&s=4"] }""", res);
                    tsc.SetResult();
                }
                catch (Exception e)
                {
                    tsc.SetException(e);
                }
            });

        var sender = new TrackingDataSender(logger: NullLogger<TrackingDataSender>.Instance, loginManager, new HttpClient(messageHandler));

        var timeProvider = new FakeTimeProvider();
        sender.AddEvent(definition: Events.Game.AddGame, metadata: new EventMetadata(name: "Mount & Blade", timeProvider: timeProvider));

        timeProvider.Advance(delta: TimeSpan.FromSeconds(1));
        sender.AddEvent(definition: Events.Loadout.CreateLoadout, metadata: new EventMetadata(name: "Mount & Blade", timeProvider: timeProvider));

        timeProvider.Advance(delta: TimeSpan.FromSeconds(1));
        var aggregateException = new AggregateException([
            new AggregateException([
                new NotSupportedException("Foo"),
            ]),
            new UnreachableException("bar"),
        ]);

        sender.AddException(aggregateException);

        timeProvider.Advance(delta: TimeSpan.FromSeconds(1));
        sender.AddEvent(definition: Events.Loadout.CreateLoadout, metadata: EventMetadata.Create(name: "Foo bar baz", value: 100, timeProvider: timeProvider));

        timeProvider.Advance(delta: TimeSpan.FromSeconds(1));
        sender.AddEvent(definition: Events.Loadout.CreateLoadout, metadata: EventMetadata.Create(name: "Foo bar baz", value: 1131412.132d, timeProvider: timeProvider));

        await sender.Run();
        await tsc.Task;
    }

    [Fact]
    public void Test_ParseSingleException()
    {
        List<ExceptionData> parsedExceptions;

        try
        {
            throw new NotSupportedException("Foo bar baz");
        }
        catch (Exception e)
        {
            parsedExceptions = ExceptionData.Create(e);
        }

        var parsedException = parsedExceptions.Should().ContainSingle().Which;
        parsedException.Type.Should().Be("System.NotSupportedException");
        parsedException.Message.Should().Be("Foo bar baz");
        parsedException.StackTrace.Should().NotBeNull();
    }

    [Fact]
    public void Test_ParseAggregateException()
    {
        var aggregateException = new AggregateException([
            new AggregateException([
                new NotSupportedException("Foo"),
            ]),
            new UnreachableException("bar"),
        ]);

        var parsedExceptions = ExceptionData.Create(aggregateException);
        parsedExceptions.Should().HaveCount(2);

        var a = parsedExceptions[0];
        a.Type.Should().Be("System.NotSupportedException");
        a.Message.Should().Be("Foo");

        var b = parsedExceptions[1];
        b.Type.Should().Be("System.Diagnostics.UnreachableException");
        b.Message.Should().Be("bar");
    }

    private static void ExpectJson([LanguageInjection(InjectedLanguage.JSON)] string expected, string actual)
    {
        actual.Should().Be(expected);
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public class MockHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return SendMock(request, cancellationToken);
        }

        public virtual Task<HttpResponseMessage> SendMock(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
