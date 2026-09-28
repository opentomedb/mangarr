using System;
using System.Threading;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Messaging.Commands
{
    [TestFixture]
    public class CommandExecutorFixture : TestBase<CommandExecutor>
    {
        private CommandQueue _commandQueue;
        private Mock<IExecute<CommandA>> _executorA;
        private Mock<IExecute<CommandB>> _executorB;
        private Mock<IExecute<CommandC>> _executorC;

        [SetUp]
        public void Setup()
        {
            _executorA = new Mock<IExecute<CommandA>>();
            _executorB = new Mock<IExecute<CommandB>>();
            _executorC = new Mock<IExecute<CommandC>>();

            Mocker.GetMock<IServiceFactory>()
                  .Setup(c => c.Build(typeof(IExecute<CommandA>)))
                  .Returns(_executorA.Object);

            Mocker.GetMock<IServiceFactory>()
                  .Setup(c => c.Build(typeof(IExecute<CommandB>)))
                  .Returns(_executorB.Object);

            Mocker.GetMock<IServiceFactory>()
                  .Setup(c => c.Build(typeof(IExecute<CommandC>)))
                  .Returns(_executorC.Object);
        }

        [TearDown]
        public void TearDown()
        {
            Subject.Handle(new ApplicationShutdownRequested());

            // Give the threads a bit of time to shut down.
            Thread.Sleep(10);
        }

        private void GivenCommandQueue()
        {
            _commandQueue = new CommandQueue();

            Mocker.GetMock<IManageCommandQueue>()
                  .Setup(s => s.Queue(It.IsAny<CancellationToken>()))
                  .Returns(_commandQueue.GetConsumingEnumerable);

            Mocker.GetMock<IManageCommandQueue>()
                  .Setup(s => s.Queue(It.IsAny<CancellationToken>(), It.IsAny<bool>()))
                  .Returns<CancellationToken, bool>((ct, high) => _commandQueue.GetConsumingEnumerable(ct, high));
        }

        private void QueueAndWaitForExecution(CommandModel commandModel, bool waitPublish = false)
        {
            var waitEventComplete = new ManualResetEventSlim();
            var waitEventPublish = new ManualResetEventSlim();

            Mocker.GetMock<IManageCommandQueue>()
                  .Setup(s => s.Complete(It.Is<CommandModel>(c => c == commandModel), It.IsAny<string>()))
                  .Callback<CommandModel, string>((c, m) =>
                  {
                      // Server messages (2026-09-26, task 1b review): mirrors the real
                      // CommandQueueManager.Complete, which clears MessageText via SetMessage --
                      // without this, a test asserting MessageText after QueueAndWaitForExecution would
                      // pass even if CommandExecutor never reassigned it after Complete.
                      c.Message = m;
                      c.MessageText = null;
                      waitEventComplete.Set();
                  });

            Mocker.GetMock<IManageCommandQueue>()
                  .Setup(s => s.Fail(It.Is<CommandModel>(c => c == commandModel), It.IsAny<string>(), It.IsAny<Exception>()))
                  .Callback(() => waitEventComplete.Set());

            Mocker.GetMock<IEventAggregator>()
                  .Setup(s => s.PublishEvent<CommandExecutedEvent>(It.IsAny<CommandExecutedEvent>()))
                  .Callback(() => waitEventPublish.Set());

            _commandQueue.Add(commandModel);

            if (!waitEventComplete.Wait(15000))
            {
                Assert.Fail("Command did not Complete/Fail within 15 sec");
            }

            if (waitPublish && !waitEventPublish.Wait(500))
            {
                Assert.Fail("Command did not Publish within 500 msec");
            }
        }

        [Test]
        public void should_start_executor_threads()
        {
            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(v => v.Queue(It.IsAny<CancellationToken>(), false), Times.AtLeast(3));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(v => v.Queue(It.IsAny<CancellationToken>(), true), Times.AtLeastOnce());
        }

        [Test]
        public void should_execute_on_executor()
        {
            GivenCommandQueue();
            var commandA = new CommandA();
            var commandModel = new CommandModel
            {
                Body = commandA
            };

            Subject.Handle(new ApplicationStartedEvent());

            QueueAndWaitForExecution(commandModel);

            _executorA.Verify(c => c.Execute(commandA), Times.Once());
        }

        [Test]
        public void should_not_execute_on_incompatible_executor()
        {
            GivenCommandQueue();
            var commandA = new CommandA();
            var commandModel = new CommandModel
            {
                Body = commandA
            };

            Subject.Handle(new ApplicationStartedEvent());

            QueueAndWaitForExecution(commandModel);

            _executorA.Verify(c => c.Execute(commandA), Times.Once());
            _executorB.Verify(c => c.Execute(It.IsAny<CommandB>()), Times.Never());
        }

        [Test]
        public void broken_executor_should_publish_executed_event()
        {
            GivenCommandQueue();
            var commandA = new CommandA();
            var commandModel = new CommandModel
            {
                Body = commandA
            };

            _executorA.Setup(s => s.Execute(It.IsAny<CommandA>()))
                      .Throws(new NotImplementedException());

            Subject.Handle(new ApplicationStartedEvent());

            QueueAndWaitForExecution(commandModel, true);

            VerifyEventPublished<CommandExecutedEvent>();

            ExceptionVerification.WaitForErrors(1, 500);
        }

        [Test]
        public void should_publish_executed_event_on_success()
        {
            GivenCommandQueue();
            var commandA = new CommandA();
            var commandModel = new CommandModel
            {
                Body = commandA
            };

            Subject.Handle(new ApplicationStartedEvent());

            QueueAndWaitForExecution(commandModel, true);

            VerifyEventPublished<CommandExecutedEvent>();
        }

        [Test]
        public void should_use_completion_message()
        {
            GivenCommandQueue();
            var commandA = new CommandA();
            var commandModel = new CommandModel
            {
                Body = commandA
            };

            Subject.Handle(new ApplicationStartedEvent());

            QueueAndWaitForExecution(commandModel);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(s => s.Complete(It.Is<CommandModel>(c => c == commandModel), commandA.CompletionMessage), Times.Once());
        }

        // A handler that reports what it did (converted/failed counts, the volume it flipped)
        // writes ResultMessage on the command it was handed; the executor must prefer it over the
        // static CompletionMessage so the toast the user sees carries the result, not "Completed".
        // CommandC is used rather than CommandA precisely because it declares a non-null
        // CompletionMessage — like every command that actually sets a result — so reordering the
        // two terms in CommandExecutor makes this test fail instead of passing on a null.
        [Test]
        public void should_prefer_result_message_over_a_non_null_completion_message()
        {
            GivenCommandQueue();
            var commandC = new CommandC { ResultMessage = "Converted 3 PDF volume(s) to CBZ, 1 failed" };
            var commandModel = new CommandModel
            {
                Body = commandC
            };

            Subject.Handle(new ApplicationStartedEvent());

            QueueAndWaitForExecution(commandModel);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(s => s.Complete(It.Is<CommandModel>(c => c == commandModel), "Converted 3 PDF volume(s) to CBZ, 1 failed"), Times.Once());
        }

        // The other half of the precedence: a handler that leaves ResultMessage null (a no-op run,
        // or any of the commands that never set it) still completes with its CompletionMessage.
        [Test]
        public void should_fall_back_to_completion_message_when_no_result_was_set()
        {
            GivenCommandQueue();
            var commandC = new CommandC();
            var commandModel = new CommandModel
            {
                Body = commandC
            };

            Subject.Handle(new ApplicationStartedEvent());

            QueueAndWaitForExecution(commandModel);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(s => s.Complete(It.Is<CommandModel>(c => c == commandModel), "Completed"), Times.Once());
        }

        [Test]
        public void should_use_last_progress_message_if_completion_message_is_null()
        {
            GivenCommandQueue();
            var commandB = new CommandB();
            var commandModel = new CommandModel
            {
                Body = commandB,
                Message = "Do work"
            };

            Subject.Handle(new ApplicationStartedEvent());

            QueueAndWaitForExecution(commandModel);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(s => s.Complete(It.Is<CommandModel>(c => c == commandModel), commandModel.Message), Times.Once());
        }

        // Server messages (2026-09-26): the completion text keeps its template for the sidebar in the UI
        // language. waitPublish: MessageText is set after Complete, before the finally block publishes.
        [Test]
        public void a_result_set_from_a_template_keeps_the_template()
        {
            GivenCommandQueue();
            var commandC = new CommandC();
            commandC.SetResultMessage(new ServerText("{0} changed, {1} blocked, {2} failed", 3, 1, 0));
            var commandModel = new CommandModel
            {
                Body = commandC
            };

            Subject.Handle(new ApplicationStartedEvent());

            QueueAndWaitForExecution(commandModel, true);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(s => s.Complete(It.Is<CommandModel>(c => c == commandModel), "3 changed, 1 blocked, 0 failed"), Times.Once());
            commandModel.MessageText.Template.Should().Be("{0} changed, {1} blocked, {2} failed");
        }

        [Test]
        public void the_last_progress_message_keeps_its_template_when_it_is_the_completion_text()
        {
            GivenCommandQueue();
            var text = new ServerText("Processing {0} releases", 3);
            var commandModel = new CommandModel
            {
                Body = new CommandB(),
                Message = text.English,
                MessageText = text
            };

            Subject.Handle(new ApplicationStartedEvent());

            QueueAndWaitForExecution(commandModel, true);

            commandModel.MessageText.Should().BeSameAs(text);
        }
    }

    public class CommandA : Command
    {
        public CommandA(int id = 0)
        {
        }
    }

    public class CommandB : Command
    {
        public override string CompletionMessage => null;
    }

    // Shaped like the real commands that report a result: a static CompletionMessage that
    // ResultMessage has to beat. CommandA deliberately keeps its inherited null one, which other
    // tests in this fixture rely on.
    public class CommandC : Command
    {
        public override string CompletionMessage => "Completed";
    }
}
