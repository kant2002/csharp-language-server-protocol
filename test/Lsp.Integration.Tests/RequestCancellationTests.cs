﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OmniSharp.Extensions.JsonRpc.Server;
using OmniSharp.Extensions.JsonRpc.Testing;
using OmniSharp.Extensions.LanguageProtocol.Testing;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Server;
using TestingUtils;
using Xunit;
using Xunit.Abstractions;

namespace Lsp.Tests.Integration
{
    public class RequestCancellationTests : LanguageProtocolTestBase
    {
        public RequestCancellationTests(ITestOutputHelper outputHelper) : base(new JsonRpcTestOptions().ConfigureForXUnit(outputHelper))
        {
        }

        [Fact]
        public async Task Should_Cancel_Pending_Requests()
        {
            var (client, _) = await Initialize(ConfigureClient, ConfigureServer);

            Func<Task<CompletionList>> action = () => {
                var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
                CancellationToken.Register(cts.Cancel);
                return client.TextDocument.RequestCompletion(
                    new CompletionParams {
                        TextDocument = "/a/file.cs"
                    }, cts.Token
                ).AsTask();
            };
            await action.Should().ThrowAsync<TaskCanceledException>();
        }

        [Fact]
        public async Task Should_Abandon_Pending_Requests_For_Text_Changes()
        {
            var (client, _) = await Initialize(ConfigureClient, ConfigureServer);

            var request1 = client.TextDocument.RequestCompletion(
                new CompletionParams {
                    TextDocument = "/a/file.cs"
                }, CancellationToken
            ).AsTask();

            client.TextDocument.DidChangeTextDocument(
                new DidChangeTextDocumentParams {
                    TextDocument = new OptionalVersionedTextDocumentIdentifier {
                        Uri = "/a/file.cs",
                        Version = 123,
                    },
                    ContentChanges = new Container<TextDocumentContentChangeEvent>()
                }
            );

            Func<Task> action = () => request1;
            await action.Should().ThrowAsync<ContentModifiedException>();
        }

        [Fact]
        public async Task Should_Cancel_Requests_After_Timeout()
        {
            Func<Task> action = async () => {
                var (client, _) = await Initialize(
                    ConfigureClient, x => {
                        ConfigureServer(x);
                        x.WithMaximumRequestTimeout(TimeSpan.FromMilliseconds(3000));
                    }
                );

                await client.TextDocument.RequestCompletion(
                    new CompletionParams {
                        TextDocument = "/a/file.cs"
                    }, CancellationToken
                ).AsTask();
            };
            await action.Should().ThrowAsync<RequestCancelledException>();
        }

        [Fact]
        public async Task Should_Cancel_Requests_After_Timeout_without_Content_Modified()
        {
            Func<Task> action = async () => {
                var (client, _) = await Initialize(
                    ConfigureClient, x => {
                        ConfigureServer(x);
                        x.WithContentModifiedSupport(false).WithMaximumRequestTimeout(TimeSpan.FromMilliseconds(3000));
                    }
                );

                await client.TextDocument.RequestCompletion(
                    new CompletionParams {
                        TextDocument = "/a/file.cs"
                    }, CancellationToken
                ).AsTask();
            };
            await action.Should().ThrowAsync<RequestCancelledException>();
        }

        [Fact]
        public async Task Can_Publish_Diagnostics_Delayed()
        {
            var (_, server) = await Initialize(
                ConfigureClient, x => {
                    ConfigureServer(x);
                    x.WithMaximumRequestTimeout(TimeSpan.FromMilliseconds(10000));
                }
            );

            server.TextDocument.PublishDiagnostics(
                new PublishDiagnosticsParams {
                    Diagnostics = new Container<Diagnostic>(
                        new Diagnostic {
                            Message = "asdf",
                        }
                    ),
                    Uri = DocumentUri.File("/from/file"),
                    Version = 1
                }
            );

            await SettleNext();

            await _diagnostics.DelayUntilCount(1, CancellationToken);

            _diagnostics.Should().HaveCount(1);
        }

        private readonly ConcurrentDictionary<DocumentUri, IEnumerable<Diagnostic>> _diagnostics = new ConcurrentDictionary<DocumentUri, IEnumerable<Diagnostic>>();

        private void ConfigureClient(LanguageClientOptions options) =>
            options.OnPublishDiagnostics(
                async (request, ct) => {
                    try
                    {
                        TestOptions.ClientLoggerFactory.CreateLogger("test").LogCritical("start");
                        await Task.Delay(500, ct);
                        _diagnostics.AddOrUpdate(request.Uri, a => request.Diagnostics, (a, b) => request.Diagnostics);
                    }
                    catch (Exception e)
                    {
                        TestOptions.ClientLoggerFactory.CreateLogger("test").LogCritical(e, "error");
                    }
                }
            );

        private void ConfigureServer(LanguageServerOptions options)
        {
            options.WithContentModifiedSupport(true);
            options.OnCompletion(
                async (x, ct) => {
                    await Task.Delay(50000, ct);
                    return new CompletionList();
                }, (_, _) => new CompletionRegistrationOptions()
            );
            options.OnDidChangeTextDocument(async x => { await Task.Delay(20); }, (_, _) =>new TextDocumentChangeRegistrationOptions());
        }
    }
}
