using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using MediatR;
using NSubstitute;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using StreamJsonRpc;
using Xunit;
using Xunit.Abstractions;
using Arg = NSubstitute.Arg;

namespace JsonRpc.Tests
{
    public class MediatorTestsNotificationHandler : AutoTestBase
    {
        [Method("exit")]
        public class ExitParams : IRequest
        {
        }

        [Method("exit")]
        public interface IExitHandler : IJsonRpcNotificationHandler<ExitParams>
        {
        }

        public MediatorTestsNotificationHandler(ITestOutputHelper testOutputHelper) : base(testOutputHelper) => Container = JsonRpcTestContainer.Create(testOutputHelper);

        [Fact]
        public async Task ExecutesHandler()
        {
            var exitHandler = Substitute.For<IExitHandler>();

            var collection = new HandlerCollection(new StreamJsonRpc.JsonRpc(Substitute.For<IJsonRpcMessageHandler>()),Substitute.For<IResolverContext>(), new AssemblyScanningHandlerTypeDescriptorProvider(new [] { typeof(AssemblyScanningHandlerTypeDescriptorProvider).Assembly, typeof(HandlerResolverTests).Assembly })) { exitHandler };
            AutoSubstitute.Provide<IHandlersManager>(collection);
            var router = AutoSubstitute.Resolve<RequestRouter>();

            var notification = new Notification("exit", null);

            await router.RouteNotification(router.GetDescriptors(notification), notification, CancellationToken.None);

            await exitHandler.Received(1).Handle(Arg.Any<ExitParams>(), Arg.Any<CancellationToken>());
        }
    }
}
