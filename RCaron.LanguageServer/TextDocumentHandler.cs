using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Progress;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.WorkDone;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using RCaron.Parsing;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;


#pragma warning disable CS0618

namespace RCaron.LanguageServer
{
    internal class TextDocumentHandler : TextDocumentSyncHandlerBase
    {
        private readonly ILogger<TextDocumentHandler> _logger;
        private readonly ILanguageServerConfiguration _configuration;
        private readonly ILanguageServerFacade _facade;

        public TextDocumentHandler(ILogger<TextDocumentHandler> logger, Foo foo,
            ILanguageServerConfiguration configuration, ILanguageServerFacade facade)
        {
            _logger = logger;
            _configuration = configuration;
            foo.SayFoo();
            _facade = facade;
        }

        public TextDocumentSyncKind Change { get; } = TextDocumentSyncKind.Full;

        public override async Task<Unit> Handle(DidChangeTextDocumentParams notification, CancellationToken token)
        {
            foreach (var change in notification.ContentChanges)
            {
                // _logger.LogInformation("Change: {@Text}", change.Text);
                // if (change.Range != null)
                //     _logger.LogInformation("Range.Start: {@RangeStart}; Range.End: {@RangeEnd}", change.Range.Start,
                //         change.Range.End);
                // since we are using TextDocumentSyncKind.Full, we can ignore the range
                Util.DocumentTexts[notification.TextDocument.Uri.GetFileSystemPath()] = change.Text;
                _logger.LogInformation("Updated document text for {Path}", notification.TextDocument.Uri.Path);
            }

            var content = await Util.GetDocumentTextAsync(notification.TextDocument.Uri.GetFileSystemPath());
            var errorHandler = new ParsingErrorStoreHandler();
            RCaronParser.Parse(content, errorHandler: errorHandler);
            if (errorHandler.Exceptions.Count == 0)
            {
                _facade.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams()
                {
                    Diagnostics = new Container<Diagnostic>(),
                    Uri = notification.TextDocument.Uri,
                    Version = notification.TextDocument.Version,
                });
                return Unit.Value;
            }

            var diagnostics = new List<Diagnostic>();

            foreach (var exception in errorHandler.Exceptions)
            {
                diagnostics.Add(new Diagnostic()
                {
                    Code = exception.Code.ToString(),
                    Severity = DiagnosticSeverity.Error,
                    Message = exception.Message,
                    Range = Util.GetRange(exception.Location.Position,
                        exception.Location.Position + exception.Location.Length, content),
                    // Source = "XXX",
                    // Tags = new Container<DiagnosticTag>(new DiagnosticTag[] { DiagnosticTag.Unnecessary })
                });
            }

            _facade.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams()
            {
                Diagnostics = new Container<Diagnostic>(diagnostics.ToArray()),
                Uri = notification.TextDocument.Uri,
                Version = notification.TextDocument.Version,
            });

            return Unit.Value;
        }

        public override async Task<Unit> Handle(DidOpenTextDocumentParams notification, CancellationToken token)
        {
            await Task.Yield();
            _logger.LogInformation("Hello world!");
            await _configuration.GetScopedConfiguration(notification.TextDocument.Uri, token).ConfigureAwait(false);
            return Unit.Value;
        }

        public override Task<Unit> Handle(DidCloseTextDocumentParams notification, CancellationToken token)
        {
            if (_configuration.TryGetScopedConfiguration(notification.TextDocument.Uri, out var disposable))
            {
                disposable.Dispose();
            }

            return Unit.Task;
        }

        public override Task<Unit> Handle(DidSaveTextDocumentParams notification, CancellationToken token) => Unit.Task;

        protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
            SynchronizationCapability capability, ClientCapabilities clientCapabilities) =>
            new TextDocumentSyncRegistrationOptions()
            {
                DocumentSelector = Util.DocumentSelector,
                Change = Change,
                Save = new SaveOptions() { IncludeText = true }
            };

        public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) =>
            new TextDocumentAttributes(uri, "file", "rcaron");
    }

    internal class MyDocumentSymbolHandler : DocumentSymbolHandlerBase
    {
        private readonly ILogger _logger;

        public MyDocumentSymbolHandler(ILogger<MyDocumentSymbolHandler> logger)
        {
            _logger = logger;
        }

        public override async Task<SymbolInformationOrDocumentSymbolContainer> Handle(
            DocumentSymbolParams request,
            CancellationToken cancellationToken
        )
        {
            // you would normally get this from a common source that is managed by current open editor, current active editor, etc.
            var content = await Util.GetDocumentTextAsync(request.TextDocument.Uri.GetFileSystemPath());
            // var lines = content.Split('\n');
            var symbols = new List<SymbolInformationOrDocumentSymbol>();

            void AddSymbol(string name,
                (int Start, int End) range, (int Start, int End) selectionRange,
                SymbolKind kind, List<DocumentSymbol>? parentChildren, List<DocumentSymbol>? children = null)
            {
                var symbol = new DocumentSymbol
                {
                    Detail = name,
                    Deprecated = false,
                    Kind = kind,
                    Range = Util.GetRange(range.Start, range.End, content),
                    SelectionRange = Util.GetRange(selectionRange.Start, selectionRange.End, content),
                    Name = name,
                    Children = children,
                };
                if (parentChildren != null)
                    parentChildren.Add(symbol);
                else
                    symbols.Add(symbol);

                _logger.LogInformation(
                    $"Symbol name: {name}; {range.Start} - {range.End}; {selectionRange.Start} - {selectionRange.End}; kind: {kind}");
            }

            var parsed = RCaronParser.Parse(content, returnDescriptive: true,
                errorHandler: new ParsingErrorDontCareHandler());

            void EvaluateLines(IList<Line> lines, List<DocumentSymbol>? parentChildren = null, bool insideClass = false)
            {
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    switch (line.Type)
                    {
                        case LineType.Function when line is TokenLine tokenLine:
                        {
                            var token = (CallLikePosToken)tokenLine.Tokens[1];
                            var cbt = (CodeBlockToken)tokenLine.Tokens[2];
                            var children = new List<DocumentSymbol>();
                            EvaluateLines(cbt.Lines, children);
                            AddSymbol(token.Name,
                                (tokenLine.Tokens[0].Position.Start, cbt.Position.End),
                                (token.Position.Start, token.NameEndIndex), SymbolKind.Function, parentChildren,
                                children);
                            break;
                        }
                        case LineType.ClassDefinition when line is TokenLine tokenLine:
                        {
                            var cbt = (CodeBlockToken)tokenLine.Tokens[2];
                            var children = new List<DocumentSymbol>();
                            EvaluateLines(cbt.Lines, children, true);
                            AddSymbol(((KeywordToken)tokenLine.Tokens[1]).String,
                                (tokenLine.Tokens[0].Position.Start, cbt.Position.End),
                                (tokenLine.Tokens[1].Position.Start, tokenLine.Tokens[1].Position.End),
                                SymbolKind.Class, parentChildren, children);
                            break;
                        }
                        case LineType.CodeBlock when line is CodeBlockLine codeBlockLine:
                            EvaluateLines(codeBlockLine.Token.Lines, parentChildren);
                            break;
                        case LineType.PropertyWithoutInitializer
                            when line is SingleTokenLine singleTokenLine && insideClass:
                        {
                            var token = (VariableToken)singleTokenLine.Token;
                            AddSymbol(token.Name,
                                (singleTokenLine.Token.Position.Start, singleTokenLine.Token.Position.End),
                                (token.Position.Start, token.Position.End), SymbolKind.Property, parentChildren);
                            break;
                        }
                        case LineType.VariableAssignment when line is TokenLine tokenLine && insideClass:
                        {
                            var token = (VariableToken)tokenLine.Tokens[0];
                            AddSymbol(token.Name,
                                (tokenLine.Tokens[0].Position.Start, tokenLine.Tokens[^1].Position.End),
                                (token.Position.Start, token.Position.End), SymbolKind.Property, parentChildren);
                            break;
                        }
                    }
                }
            }

            EvaluateLines(parsed.FileScope.Lines);
            return symbols;
        }

        protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
            DocumentSymbolCapability capability,
            ClientCapabilities clientCapabilities) => new()
        {
            DocumentSelector = Util.DocumentSelector
        };
    }

    internal class MyWorkspaceSymbolsHandler : IWorkspaceSymbolsHandler
    {
        private readonly IServerWorkDoneManager _serverWorkDoneManager;
        private readonly IProgressManager _progressManager;
        private readonly ILogger<MyWorkspaceSymbolsHandler> _logger;

        public MyWorkspaceSymbolsHandler(IServerWorkDoneManager serverWorkDoneManager, IProgressManager progressManager,
            ILogger<MyWorkspaceSymbolsHandler> logger)
        {
            _serverWorkDoneManager = serverWorkDoneManager;
            _progressManager = progressManager;
            _logger = logger;
        }

        public async Task<Container<SymbolInformation>?> Handle(
            WorkspaceSymbolParams request,
            CancellationToken cancellationToken
        )
        {
            using var reporter = _serverWorkDoneManager.For(
                request, new WorkDoneProgressBegin
                {
                    Cancellable = true,
                    Message = "This might take a while...",
                    Title = "Some long task....",
                    Percentage = 0
                }
            );
            using var partialResults = _progressManager.For(request, cancellationToken);
            if (partialResults != null)
            {
                await Task.Delay(2000, cancellationToken).ConfigureAwait(false);

                reporter.OnNext(
                    new WorkDoneProgressReport
                    {
                        Cancellable = true,
                        Percentage = 20
                    }
                );
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);

                reporter.OnNext(
                    new WorkDoneProgressReport
                    {
                        Cancellable = true,
                        Percentage = 40
                    }
                );
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);

                reporter.OnNext(
                    new WorkDoneProgressReport
                    {
                        Cancellable = true,
                        Percentage = 50
                    }
                );
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);

                partialResults.OnNext(
                    new[]
                    {
                        new SymbolInformation
                        {
                            ContainerName = "Partial Container",
                            Deprecated = true,
                            Kind = SymbolKind.Constant,
                            Location = new Location
                            {
                                Range = new Range(
                                    new Position(2, 1),
                                    new Position(2, 10)
                                )
                            },
                            Name = "Partial name"
                        }
                    }
                );

                reporter.OnNext(
                    new WorkDoneProgressReport
                    {
                        Cancellable = true,
                        Percentage = 70
                    }
                );
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);

                reporter.OnNext(
                    new WorkDoneProgressReport
                    {
                        Cancellable = true,
                        Percentage = 90
                    }
                );

                partialResults.OnCompleted();
                return new SymbolInformation[] { };
            }

            try
            {
                return new[]
                {
                    new SymbolInformation
                    {
                        ContainerName = "Container",
                        Deprecated = true,
                        Kind = SymbolKind.Constant,
                        Location = new Location
                        {
                            Range = new Range(
                                new Position(1, 1),
                                new Position(1, 10)
                            )
                        },
                        Name = "name"
                    }
                };
            }
            finally
            {
                reporter.OnNext(
                    new WorkDoneProgressReport
                    {
                        Cancellable = true,
                        Percentage = 100
                    }
                );
            }
        }

        public WorkspaceSymbolRegistrationOptions GetRegistrationOptions(WorkspaceSymbolCapability capability,
            ClientCapabilities clientCapabilities) => new WorkspaceSymbolRegistrationOptions();
    }
}